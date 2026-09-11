using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Npc
{
    /// <summary>
    /// 會走動、身上長著某個顏色羊毛的 NPC。v6 的素材來源。
    ///
    /// 三個狀態，沒有更多：
    ///   Wander — 走向活動範圍內的隨機目標點
    ///   Pause  — 站著發呆一段時間
    ///   Flee   — 被剃之後背對玩家跑走
    ///
    /// **移動用 NetworkCharacterController**，跟玩家一樣，不是裸 CharacterController +
    /// NetworkTransform。這在玩家身上已經踩過一次坑了：Fusion 重模擬前必須先
    /// disable/enable CharacterController，transform 的新位置才會被它認得；
    /// 少了這步，用戶端會在幾秒後開始抖動並瞬間大步移動。
    ///
    /// 剃毛不生成掉在地上的羊毛，直接進 TeamStash —— 這是 v6 跟舊版最大的差別。
    /// </summary>
    [RequireComponent(typeof(NetworkCharacterController))]
    public class WoolNpc : NetworkBehaviour, IInteractable
    {
        /// <summary>場上所有的 NPC。批 B 的顧客系統要從這裡挑人，避免每次 FindObjectsOfType。</summary>
        public static readonly List<WoolNpc> All = new();

        private enum NpcState : byte { Wander = 0, Pause = 1, Flee = 2 }

        [Header("Wool NPC")]
        [Tooltip("開場時身上長哪一種顏色的毛。場景建置器會逐隻設定。" +
                 "**不要設成白色** —— 白毛是玩家互剃專屬的產出，野生動物不長白毛。")]
        [SerializeField] private DyeColorType _startColor = DyeColorType.Yellow;
        [SerializeField] private Transform _interactionAnchor;
        [SerializeField] private Renderer _bodyRenderer;
        [Tooltip("身上的毛。有幾份毛就顯示幾撮，剃光就全部關掉。")]
        [SerializeField] private Renderer[] _fleeceTufts;

        // ---- 網路狀態 ----
        [Networked] public int ColorRaw { get; set; }
        [Networked] public int Fleece { get; set; }
        [Networked] public int StateRaw { get; set; }
        [Networked] public Vector3 HomePoint { get; set; }
        [Networked] public Vector3 TargetPoint { get; set; }
        [Networked] public Vector3 FleeDirection { get; set; }
        [Networked] private TickTimer RegenTimer { get; set; }
        [Networked] private TickTimer FleeTimer { get; set; }
        [Networked] private TickTimer PauseTimer { get; set; }

        /// <summary>批 B：被顧客系統徵召之後就不再閒晃，也不能剃毛。</summary>
        [Networked] public NetworkBool IsCustomer { get; set; }

        private NetworkCharacterController _ncc;
        private MaterialPropertyBlock _mpb;

        public DyeColorType WoolColor => (DyeColorType)ColorRaw;
        public bool HasWool => Fleece > 0;
        private NpcState State => (NpcState)StateRaw;

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);

            _ncc = GetComponent<NetworkCharacterController>();
            ConfigureController();

            if (HasStateAuthority)
            {
                // [Networked] 屬性沒辦法在編輯期預先寫入，所以顏色是走序列化欄位帶進來的
                ColorRaw = (int)_startColor;
                HomePoint = transform.position;
                Fleece = GameTuning.NpcFleeceMax;
                EnterPause();
            }

            ApplyColour(true);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        /// <summary>
        /// 把數值餵給 NCC。rotationSpeed 保留預設的轉向行為 ——
        /// NPC 沒有「共用朝向」的包袱，讓它自然轉向移動方向就好，看起來比較像活的。
        /// </summary>
        private void ConfigureController()
        {
            if (_ncc == null) return;
            _ncc.gravity      = -GameTuning.NpcGravity;
            _ncc.acceleration = 20f;
            _ncc.braking      = 20f;
            _ncc.maxSpeed     = GameTuning.NpcWanderSpeed;
            _ncc.rotationSpeed = 8f;
        }

        /// <summary>由場景建置器或生成端指定顏色。</summary>
        public void Configure(DyeColorType color, Vector3 home)
        {
            ColorRaw = (int)color;
            HomePoint = home;
            Fleece = GameTuning.NpcFleeceMax;
        }

        // ---------------- 模擬 ----------------

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            TickRegen();

            // 當顧客的時候完全讓開 —— Customer 那支會接管 NCC。
            // **這裡不能呼叫 _ncc.Move()**，兩個元件同一 tick 都推同一個
            // CharacterController 的話，後跑的會覆蓋先跑的，顧客就走不動了。
            if (IsCustomer) return;

            switch (State)
            {
                case NpcState.Flee:  TickFlee();  break;
                case NpcState.Pause: TickPause(); break;
                default:             TickWander(); break;
            }
        }

        private void TickRegen()
        {
            if (Fleece >= GameTuning.NpcFleeceMax) return;
            if (!RegenTimer.ExpiredOrNotRunning(Runner)) return;

            Fleece = Mathf.Min(GameTuning.NpcFleeceMax, Fleece + 1);
            RegenTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.NpcFleeceRegenSeconds);
        }

        private void TickWander()
        {
            _ncc.maxSpeed = GameTuning.NpcWanderSpeed;

            var to = TargetPoint - transform.position;
            to.y = 0f;

            if (to.magnitude <= GameTuning.NpcArriveThreshold)
            {
                EnterPause();
                _ncc.Move(Vector3.zero);
                return;
            }

            _ncc.Move(to.normalized);
        }

        private void TickPause()
        {
            _ncc.Move(Vector3.zero);
            if (!PauseTimer.ExpiredOrNotRunning(Runner)) return;
            EnterWander();
        }

        private void TickFlee()
        {
            if (FleeTimer.ExpiredOrNotRunning(Runner))
            {
                EnterPause();
                return;
            }

            _ncc.maxSpeed = GameTuning.NpcFleeSpeed;

            // 跑出活動範圍就轉向繞回來，不然會一路跑到世界邊緣
            var fromHome = transform.position - HomePoint;
            fromHome.y = 0f;
            var dir = FleeDirection;
            if (fromHome.magnitude > GameTuning.NpcWanderRadius)
                dir = Vector3.Slerp(dir, -fromHome.normalized, 0.6f);

            _ncc.Move(dir.normalized);
        }

        // ---------------- 狀態切換 ----------------

        private void EnterWander()
        {
            StateRaw = (int)NpcState.Wander;

            // Random 只在狀態權威上跑，結果透過 [Networked] TargetPoint 同步出去，
            // 所以重模擬不會產生不同的目標點
            var offset = Random.insideUnitCircle * GameTuning.NpcWanderRadius;
            TargetPoint = HomePoint + new Vector3(offset.x, 0f, offset.y);
        }

        private void EnterPause()
        {
            StateRaw = (int)NpcState.Pause;
            PauseTimer = TickTimer.CreateFromSeconds(Runner,
                Random.Range(GameTuning.NpcWanderPauseMin, GameTuning.NpcWanderPauseMax));
        }

        private void EnterFlee(Vector3 awayFrom)
        {
            StateRaw = (int)NpcState.Flee;

            var dir = transform.position - awayFrom;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;

            FleeDirection = dir.normalized;
            FleeTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.NpcFleeSeconds);
        }

        // ---------------- 剃毛 ----------------

        /// <summary>剃一份毛，直接進共用背包。只在 StateAuthority 呼叫。</summary>
        private bool TryShear(Vector3 shearerPosition)
        {
            if (!HasStateAuthority || Fleece <= 0) return false;

            var stash = TeamStash.Instance;
            if (stash == null)
            {
                Debug.LogError("[v6] 場景裡沒有 TeamStash，剃下來的毛沒有地方放。" +
                               "請確認 [GameSystems] 上掛了 TeamStash。");
                return false;
            }

            // 背包滿了就不剃 —— 毛留在 NPC 身上，玩家看得出來沒拿到
            if (!stash.TryAdd(WoolColor, 1)) return false;

            Fleece--;
            RegenTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.NpcFleeceRegenSeconds);
            EnterFlee(shearerPosition);

            GameAudio.PlayAt(SfxId.Shear, transform.position);
            return true;
        }

        // ---------------- IInteractable ----------------

        public Transform InteractionAnchor =>
            _interactionAnchor != null ? _interactionAnchor : transform;

        /// <summary>比地面雜物高、比機台低。NPC 不該蓋過攤位上的裝備。</summary>
        public int InteractionPriority => 1;

        /// <summary>
        /// **不需要手上拿著剃毛器。** 剃毛器已經不是要攜帶的道具了 ——
        /// 對著羊按左鍵就是剃毛，剃毛器只在動作的那一瞬間伸出來再收回去。
        ///
        /// 也不要求空手：剃下來的毛直接進共同背包，不經過玩家的手，
        /// 所以手上拿著什麼都不影響。
        /// </summary>
        public bool CanInteract(in InteractionContext ctx)
        {
            if (IsCustomer) return false;
            if (ctx.Player == null) return false;

            // 毛剃光了也要能互動 —— GetPrompt 要說「剃光了」而不是靜默無反應
            return true;
        }

        public string GetPrompt(in InteractionContext ctx)
        {
            if (!CanInteract(in ctx)) return null;

            if (Fleece <= 0) return "牠身上的毛剃光了";

            string colour = PlaceholderPalette.DyeName(WoolColor);
            return $"[左鍵] 剃{colour}毛（{Fleece}/{GameTuning.NpcFleeceMax}）";
        }

        public void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;
            if (Fleece <= 0) return;

            // 先伸出剃毛器再結算 —— 剃不成功（背包滿了）也該看到動作，
            // 不然玩家不知道自己到底有沒有按到
            ctx.Player.TriggerShearVisual();
            TryShear(ctx.Player.transform.position);
        }

        // ---------------- 外觀 ----------------

        public override void Render()
        {
            ApplyColour(false);
        }

        private int _renderedFleece = -1;
        private int _renderedColour = -1;

        private void ApplyColour(bool force)
        {
            if (!force && _renderedFleece == Fleece && _renderedColour == ColorRaw) return;
            _renderedFleece = Fleece;
            _renderedColour = ColorRaw;

            _mpb ??= new MaterialPropertyBlock();

            // 身體：有毛時是毛的顏色，剃光轉成灰白的素體 —— 遠遠就看得出誰還有毛
            if (_bodyRenderer != null)
            {
                var c = Fleece > 0
                    ? PlaceholderPalette.Dye(WoolColor)
                    : new Color(0.78f, 0.76f, 0.72f);
                Tint(_bodyRenderer, c);
            }

            // 毛：剩幾份就顯示幾撮
            if (_fleeceTufts == null) return;
            for (int i = 0; i < _fleeceTufts.Length; i++)
            {
                if (_fleeceTufts[i] == null) continue;
                bool on = i < Fleece;
                if (_fleeceTufts[i].enabled != on) _fleeceTufts[i].enabled = on;
                if (on) Tint(_fleeceTufts[i], PlaceholderPalette.Dye(WoolColor));
            }
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.5f);
            var home = Application.isPlaying ? HomePoint : transform.position;
            Gizmos.DrawWireSphere(home, GameTuning.NpcWanderRadius);
        }
    }
}
