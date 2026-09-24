using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Player;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Npc
{
    /// <summary>
    /// 大動物。體型是 WoolNpc 的兩倍，身上九份毛一次全掉，**一個人抓不到**。
    ///
    /// 核心規則只有一條：**牠永遠往「離最近的玩家最遠」的方向跑，而且比玩家快。**
    /// 難度曲線是這條規則自己長出來的，程式裡沒有任何難度設定：
    ///
    ///   一個人 -> 牠永遠背對你，6.5 對 5.0，追不上。抓不到是正確的
    ///   兩個人 -> 站兩側，牠往 A 跑就被 B 逼回來，可以慢慢夾
    ///   四個人 -> 切斷退路，很快逼到角落
    ///
    /// **這是獨立元件，不是 WoolNpc 的一種模式。** 兩者唯一的共同點是「身上有毛」，
    /// 行為、體型、收網方式全都不同，硬塞進同一支會讓兩邊都變難讀。
    ///
    /// 也**刻意不進 WoolNpc.All** —— 那份清單是 CustomerQueue 抽顧客用的，
    /// 大動物被抽去排隊買衣服會非常荒謬。
    ///
    /// 批 1 刻意**不實作 IStaggerable**：道具對牠完全無效。
    /// 先確認光用身體圍堵好不好玩 —— 好玩的話道具只會更好玩，不好玩的話道具也救不了。
    ///
    /// 移動用 NetworkCharacterController，跟玩家與 WoolNpc 一樣。
    /// 裸 CharacterController + NetworkTransform 撐不過 Fusion 的重模擬，
    /// 用戶端會在幾秒後開始抖動並瞬間大步移動 —— 這個坑已經踩過兩次。
    /// </summary>
    [RequireComponent(typeof(NetworkCharacterController))]
    public class WildBeast : NetworkBehaviour, IInteractable
    {
        /// <summary>場上所有的大動物。**跟 WoolNpc.All 分開**，理由見類別註解。</summary>
        public static readonly List<WildBeast> All = new();

        private enum BeastState : byte { Graze = 0, Alert = 1, Flee = 2, Return = 3 }

        [Header("Wild Beast")]
        [Tooltip("身上的毛是什麼顏色。最貴的顏色配最難抓的動物。")]
        [SerializeField] private DyeColorType _woolColor = DyeColorType.Red;
        [SerializeField] private Transform _interactionAnchor;
        [SerializeField] private Renderer _bodyRenderer;
        [Tooltip("身上的毛。有幾份就顯示幾撮，剃光全關。")]
        [SerializeField] private Renderer[] _fleeceTufts;

        [Networked] public int Fleece { get; set; }
        [Networked] public int StateRaw { get; set; }
        [Networked] public Vector3 HomePoint { get; set; }
        [Networked] public Vector3 TargetPoint { get; set; }

        /// <summary>目前跑的方向。每 0.25 秒重算一次，中間沿用。</summary>
        [Networked] public Vector3 MoveDirection { get; set; }

        /// <summary>至少要逃這麼久。沒有它的話你一退牠就停，圍堵會變成抖動。</summary>
        [Networked] private TickTimer FleeMinTimer { get; set; }

        /// <summary>下一次重算逃跑方向的時間。</summary>
        [Networked] private TickTimer RethinkTimer { get; set; }

        private NetworkCharacterController _ncc;
        private MaterialPropertyBlock _mpb;

        public DyeColorType WoolColor => _woolColor;
        public bool HasWool => Fleece > 0;
        private BeastState State => (BeastState)StateRaw;

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);

            _ncc = GetComponent<NetworkCharacterController>();
            ConfigureController();

            if (HasStateAuthority)
            {
                HomePoint = transform.position;
                Fleece = GameTuning.BeastFleece;
                EnterGraze();
            }

            ApplyVisual(true);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        private void ConfigureController()
        {
            if (_ncc == null) return;
            _ncc.gravity       = -GameTuning.BeastGravity;
            _ncc.acceleration  = 18f;
            _ncc.braking       = 18f;
            _ncc.maxSpeed      = GameTuning.BeastGrazeSpeed;
            _ncc.rotationSpeed = 6f;   // 自然轉向移動方向，看起來像活的
        }

        /// <summary>由場景建置器指定顏色與家點。</summary>
        public void Configure(DyeColorType color, Vector3 home)
        {
            _woolColor = color;
            HomePoint = home;
        }

        // ---------------- 模擬 ----------------

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            switch (State)
            {
                case BeastState.Alert:  TickAlert();  break;
                case BeastState.Flee:   TickFlee();   break;
                case BeastState.Return: TickReturn(); break;
                default:                TickGraze();  break;
            }
        }

        // ---------------- 各狀態 ----------------

        private void TickGraze()
        {
            _ncc.maxSpeed = GameTuning.BeastGrazeSpeed;

            // 有人靠得夠近就停下來盯著他
            if (NearestPlayer(out _, out float dist) && dist < GameTuning.BeastStareRadius)
            {
                EnterAlert();
                _ncc.Move(Vector3.zero);
                return;
            }

            var to = TargetPoint - transform.position;
            to.y = 0f;

            if (to.magnitude <= GameTuning.BeastArriveThreshold)
            {
                EnterGraze();          // 到了就換一個新的目標點
                _ncc.Move(Vector3.zero);
                return;
            }

            _ncc.Move(to.normalized);
        }

        /// <summary>
        /// 盯著。**這一段是整批最重要的表現** —— 玩家看不到 10 公尺的圈，
        /// 所以牠必須自己把距離講出來：停下來、轉身正對著你、換色。
        ///
        /// 玩家靠這個學會「再一步就會跑」，而那個學習過程本身就是玩法。
        /// 所以不要用 UI 畫警戒圈 —— 畫了就不用學了。
        /// </summary>
        private void TickAlert()
        {
            _ncc.Move(Vector3.zero);

            if (!NearestPlayer(out var player, out float dist))
            {
                EnterGraze();
                return;
            }

            // 轉身面向他。只轉 yaw，不要讓牠仰頭或低頭。
            var look = player.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

            if (dist < GameTuning.BeastAlertRadius) { EnterFlee(GameTuning.BeastFleeMinSeconds); return; }
            if (dist > GameTuning.BeastStareRadius) EnterGraze();
        }

        private void TickFlee()
        {
            _ncc.maxSpeed = GameTuning.BeastFleeSpeed;

            bool anyoneClose = NearestPlayer(out _, out float dist)
                            && dist < GameTuning.BeastStareRadius;

            // 沒人靠近而且最短逃跑時間到了 -> 收手
            if (!anyoneClose && FleeMinTimer.ExpiredOrNotRunning(Runner))
            {
                if (OutsideTerritory(transform.position)) EnterReturn();
                else EnterGraze();
                return;
            }

            // 方向每 0.25 秒重算一次。每個 tick 重算會讓牠原地抖。
            if (RethinkTimer.ExpiredOrNotRunning(Runner))
            {
                MoveDirection = PickFleeDirection();
                RethinkTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.BeastRethinkSeconds);
            }

            _ncc.Move(MoveDirection);
        }

        /// <summary>走回家，路上不理會玩家 —— 不然牠會在領域邊界來回彈。</summary>
        private void TickReturn()
        {
            _ncc.maxSpeed = GameTuning.BeastGrazeSpeed;

            var to = HomePoint - transform.position;
            to.y = 0f;

            if (to.magnitude <= GameTuning.BeastArriveThreshold)
            {
                EnterGraze();
                _ncc.Move(Vector3.zero);
                return;
            }

            _ncc.Move(to.normalized);
        }

        // ---------------- 逃跑方向 ----------------

        /// <summary>
        /// 繞一圈取樣，挑「離最近玩家最遠」的方向。
        ///
        /// **不要用「直接取反方向」。** 那個寫法在領域邊界與牆壁上會卡死：
        /// 玩家從牆的方向逼過來時，反方向就是牆，牠會貼著牆抖動直到被抓到 ——
        /// 那不是被圍住，那是 bug。
        ///
        /// 取樣的淘汰條件有三個，通過的才評分：
        ///   跑出領域、前方有障礙、前方沒有地面
        ///
        /// **全部被淘汰時保持目前方向**，不要改成隨機亂跑 ——
        /// 牠被逼到角落原地打轉正是這整個設計的高潮，那就是玩家靠近的窗口。
        /// 亂跑會讓那個瞬間消失。
        /// </summary>
        private Vector3 PickFleeDirection()
        {
            var origin = transform.position;
            float reach = GameTuning.BeastFleeSpeed * GameTuning.BeastLookaheadSeconds;

            Vector3 best = MoveDirection;
            float bestScore = float.MinValue;
            bool found = false;

            for (int i = 0; i < GameTuning.BeastDirectionSamples; i++)
            {
                float angle = 360f / GameTuning.BeastDirectionSamples * i;
                var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                var predicted = origin + dir * reach;

                if (OutsideTerritory(predicted)) continue;
                if (BlockedAhead(origin, dir)) continue;
                if (!HasGroundAt(predicted)) continue;

                float score = ScoreAgainstPlayers(predicted);
                if (score <= bestScore) continue;

                bestScore = score;
                best = dir;
                found = true;
            }

            // 一個方向都沒過 -> 被封死了，保持原方向（原地打轉）
            return found ? best : MoveDirection;
        }

        /// <summary>
        /// 這個預測點有多安全 = 離最近玩家多遠，再加上一點「離第二近的玩家也要遠」。
        ///
        /// 第二項很重要：少了它，牠會為了躲開 A 而直直撞進 B 的懷裡，
        /// 兩個人隨便站都夾得到。加了之後必須真的站對位置。
        /// </summary>
        private float ScoreAgainstPlayers(Vector3 predicted)
        {
            float nearest = float.MaxValue;
            float second = float.MaxValue;

            for (int i = 0; i < PlayerController.All.Count; i++)
            {
                var p = PlayerController.All[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;

                var to = p.transform.position - predicted;
                to.y = 0f;
                float d = to.magnitude;

                if (d < nearest) { second = nearest; nearest = d; }
                else if (d < second) { second = d; }
            }

            if (nearest == float.MaxValue) return 0f;          // 場上沒有玩家
            if (second == float.MaxValue) return nearest;      // 只有一個玩家

            return nearest + GameTuning.BeastSecondPlayerWeight * second;
        }

        private bool OutsideTerritory(Vector3 point)
        {
            var to = point - HomePoint;
            to.y = 0f;
            return to.magnitude > GameTuning.BeastTerritoryRadius;
        }

        private bool BlockedAhead(Vector3 origin, Vector3 dir)
        {
            var from = origin + Vector3.up * 0.8f;
            return Physics.Raycast(from, dir, GameTuning.BeastObstacleProbe,
                                   ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 預測點底下有沒有地面。
        ///
        /// 不在原始規格裡，是補的：現在的地圖只有 60x60 的地板，而領域半徑是 40 ——
        /// 領域比地板大，光靠領域檢查擋不住牠跑出邊緣摔下去。
        /// 有正式地形（四周有牆）之後這條就只是多一層保險。
        /// </summary>
        private static bool HasGroundAt(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * 2f, Vector3.down,
                                   GameTuning.BeastGroundProbe + 2f,
                                   ~0, QueryTriggerInteraction.Ignore);
        }

        // ---------------- 狀態切換 ----------------

        private void EnterGraze()
        {
            StateRaw = (int)BeastState.Graze;

            // Random 只在狀態權威上跑，結果透過 [Networked] TargetPoint 同步出去，
            // 所以重模擬不會產生不同的目標點
            var offset = Random.insideUnitCircle * (GameTuning.BeastTerritoryRadius * 0.5f);
            TargetPoint = HomePoint + new Vector3(offset.x, 0f, offset.y);
        }

        private void EnterAlert() => StateRaw = (int)BeastState.Alert;

        private void EnterFlee(float minSeconds)
        {
            StateRaw = (int)BeastState.Flee;
            FleeMinTimer = TickTimer.CreateFromSeconds(Runner, minSeconds);
            RethinkTimer = default;                 // 立刻算一次方向，不要等 0.25 秒
            MoveDirection = PickFleeDirection();
        }

        private void EnterReturn() => StateRaw = (int)BeastState.Return;

        // ---------------- 找玩家 ----------------

        /// <summary>
        /// 最近的玩家。**是「最近的」不是「主機的」** ——
        /// 用戶端玩家靠近也必須觸發，不然連線時只有房主能嚇到牠。
        /// </summary>
        private bool NearestPlayer(out PlayerController nearest, out float distance)
        {
            nearest = null;
            distance = float.MaxValue;

            for (int i = 0; i < PlayerController.All.Count; i++)
            {
                var p = PlayerController.All[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;

                var to = p.transform.position - transform.position;
                to.y = 0f;
                float d = to.magnitude;

                if (d >= distance) continue;
                distance = d;
                nearest = p;
            }
            return nearest != null;
        }

        // ---------------- IInteractable（剃毛）----------------

        public Transform InteractionAnchor =>
            _interactionAnchor != null ? _interactionAnchor : transform;

        /// <summary>比地面雜物高、比機台低，跟 WoolNpc 一樣。</summary>
        public int InteractionPriority => 1;

        public bool CanInteract(in InteractionContext ctx)
            => Fleece > 0 && ctx.HeldKind == ItemKind.Shears;

        public string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.HeldKind != ItemKind.Shears) return null;
            return Fleece > 0
                ? $"[左鍵] 剃毛（{Fleece} 份一次全掉）"
                : "牠身上的毛剃光了";
        }

        /// <summary>
        /// 剃毛。批 1 沒有暈眩，所以收網方式就是「靠到 2.5 公尺內」——
        /// 那本身就是兩個人夾到底的證明。
        ///
        /// 毛**生成在地上**、不是直接進背包：九份散一地要一份一份撿，
        /// 撿的過程就是這場圍堵的戰利品展示。跟撞暈掉毛是同一個規則。
        /// </summary>
        public void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || Fleece <= 0) return;

            int count = Fleece;
            Fleece = 0;

            var spec = GarmentSpec.Create(PatternType.None, _woolColor);
            for (int i = 0; i < count; i++)
            {
                // 繞著牠散開 1.5–2.5 公尺
                float angle = 360f / count * i + Random.Range(-12f, 12f);
                float radius = Random.Range(1.5f, 2.5f);
                var offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;

                ItemFactory.Spawn(Runner, ItemKind.Wool, spec,
                                  transform.position + offset + Vector3.up * 0.8f);
            }

            ctx.Player.TriggerShearVisual();
            GameAudio.PlayAt(SfxId.Shear, transform.position);

            // 剃完要真的跑掉，不是原地繼續被圍
            EnterFlee(GameTuning.BeastShearedFleeSeconds);
        }

        // ---------------- 外觀 ----------------

        public override void Render() => ApplyVisual(false);

        private int _renderedFleece = -1;
        private int _renderedState = -1;

        private void ApplyVisual(bool force)
        {
            if (!force && _renderedFleece == Fleece && _renderedState == StateRaw) return;
            _renderedFleece = Fleece;
            _renderedState = StateRaw;

            _mpb ??= new MaterialPropertyBlock();

            if (_bodyRenderer != null)
            {
                // Alert 換成明顯的警戒色 —— 這是玩家唯一能看到的「距離提示」
                var c = State == BeastState.Alert
                    ? new Color(1f, 0.55f, 0.15f)
                    : PlaceholderPalette.Dye(_woolColor);

                if (Fleece <= 0) c *= 0.45f;   // 剃光轉暗，遠遠就看得出沒毛了

                _bodyRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_Color", c);
                _bodyRenderer.SetPropertyBlock(_mpb);
            }

            if (_fleeceTufts == null) return;
            for (int i = 0; i < _fleeceTufts.Length; i++)
            {
                if (_fleeceTufts[i] == null) continue;

                // 九份毛配幾撮視覺：等比例換算，不要一份一撮（會太多）
                bool on = _fleeceTufts.Length > 0
                       && i < Mathf.CeilToInt(_fleeceTufts.Length * (Fleece / (float)GameTuning.BeastFleece));
                if (_fleeceTufts[i].enabled != on) _fleeceTufts[i].enabled = on;
            }
        }
    }
}
