using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Player;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Items
{
    /// <summary>
    /// 所有能被拿在手上、丟出、接住的物件。
    /// 位置同步靠 NetworkTransform；被拿著的時候由各端在 Render() 直接貼到手上錨點，避免延遲感。
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class CarriableItem : NetworkBehaviour, IInteractable
    {
        /// <summary>目前場上所有可攜帶物件（給「接住」判定用，避免每幀 FindObjectsOfType）。</summary>
        public static readonly List<CarriableItem> All = new();

        [Header("Item")]
        [SerializeField] private ItemKind _kind = ItemKind.None;
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private Renderer[] _tintTargets;
        [SerializeField] private float _groundOffset = 0.15f;

        [Networked] public NetworkId HolderId { get; set; }
        [Networked] public NetworkBool InFlight { get; set; }
        [Networked] public Vector3 FlightVelocity { get; set; }
        [Networked] public float FlightElapsed { get; set; }
        /// <summary>誰丟出來的。剛丟出的短時間內丟的人自己接不到。</summary>
        [Networked] public NetworkId ThrowerId { get; set; }
        [Networked] public GarmentSpec Spec { get; set; }

        /// <summary>
        /// 從機台換出來的「做到一半」的進度（秒）。0 = 沒有進度（一般的物品）。
        ///   羊毛：已經紡了幾秒（放回任何一台紡線機都從這裡繼續）
        ///   半成品衣服：已經織了幾秒
        /// 物品被丟、被撿、被傳來傳去都跟著走，因為它就存在物品身上。
        /// </summary>
        [Networked] public float WorkSeconds { get; set; }

        /// <summary>這份進度的終點（秒）。紡線固定是 SpinSeconds；半成品是當時的目標長度（單色 4 / 雙色 7）。</summary>
        [Networked] public float WorkTotal { get; set; }

        /// <summary>進度比例 0~1。沒有進度時是 0。</summary>
        public float Work01 => WorkTotal > 0f ? Mathf.Clamp01(WorkSeconds / WorkTotal) : 0f;
        public bool HasWork => WorkSeconds > 0f && WorkTotal > 0f;

        private Collider[] _colliders;
        private MaterialPropertyBlock _mpb;
        private GarmentSpec _lastRenderedSpec;
        private bool _renderedOnce;

        public ItemKind Kind => _kind;
        public bool IsHeld => HolderId.IsValid;
        public Transform VisualRoot => _visualRoot != null ? _visualRoot : transform;

        public PlayerController Holder
        {
            get
            {
                if (!HolderId.IsValid || Runner == null) return null;
                return Runner.TryFindObject(HolderId, out var obj) ? obj.GetComponent<PlayerController>() : null;
            }
        }

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            _colliders = GetComponentsInChildren<Collider>(true);
            All.Add(this);
            ApplyTint(true);
            UpdateColliders();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var holder = Holder;
            if (holder != null)
            {
                transform.position = holder.HandAnchor.position;
                transform.rotation = holder.HandAnchor.rotation;
                return;
            }

            if (InFlight)
                TickFlight(Runner.DeltaTime);
        }

        public override void Render()
        {
            ApplyTint(false);
            UpdateColliders();
        }

        // ---------------- 進度條（做到一半的東西）----------------
        //
        // 執行期生出來的兩個小方塊，浮在物品上方、轉向鏡頭。
        // 不做進 prefab：只有極少數物品會有進度，而且這樣不用重建任何資產。

        private Transform _workBarRoot;
        private Transform _workBarFill;
        private readonly BarAnchor _workBar01 = new(BarAnchor.Axis.X);

        private void UpdateWorkBar()
        {
            bool show = HasWork;
            if (!show)
            {
                if (_workBarRoot != null && _workBarRoot.gameObject.activeSelf)
                    _workBarRoot.gameObject.SetActive(false);
                return;
            }

            if (_workBarRoot == null) BuildWorkBar();
            if (_workBarRoot == null) return;
            if (!_workBarRoot.gameObject.activeSelf) _workBarRoot.gameObject.SetActive(true);

            _workBarRoot.position = VisualRoot.position + Vector3.up * 0.38f;
            var cam = Camera.main;
            if (cam != null)
            {
                var dir = _workBarRoot.position - cam.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f) _workBarRoot.rotation = Quaternion.LookRotation(dir.normalized);
            }
            _workBar01.Apply(_workBarFill, Work01);
        }

        private void BuildWorkBar()
        {
            var mat = _tintTargets != null && _tintTargets.Length > 0 && _tintTargets[0] != null
                ? _tintTargets[0].sharedMaterial
                : null;
            if (mat == null) return;

            _workBarRoot = new GameObject("WorkBar").transform;
            _workBarRoot.SetParent(transform, false);

            MakeBarPiece("Back", new Vector3(0.44f, 0.07f, 0.02f), Vector3.zero,
                         new Color(0.12f, 0.12f, 0.14f), mat);
            _workBarFill = MakeBarPiece("Fill", new Vector3(0.4f, 0.045f, 0.03f), new Vector3(0f, 0f, -0.006f),
                                        new Color(0.98f, 0.8f, 0.2f), mat);
        }

        private Transform MakeBarPiece(string name, Vector3 size, Vector3 localPos, Color c, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.layer = gameObject.layer;
            go.transform.SetParent(_workBarRoot, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;

            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", c);
            mpb.SetColor("_Color", c);
            r.SetPropertyBlock(mpb);
            return go.transform;
        }

        /// <summary>
        /// 被拿著的時候，各端都直接貼到手上錨點，不等網路位置同步（拿在手上不該有延遲）。
        /// 放在 LateUpdate 而不是 Render，確保一定蓋過 NetworkTransform 的插值結果。
        /// </summary>
        protected virtual void LateUpdate()
        {
            var holder = Holder;
            if (holder != null)
            {
                transform.position = holder.HandAnchor.position;
                transform.rotation = holder.HandAnchor.rotation;
            }

            // 進度條要在物品貼到手上之後才擺位置，不然會落後一幀
            if (Object != null && Object.IsValid) UpdateWorkBar();
        }

        // ---------------- 持有 / 丟接 ----------------

        public void AttachTo(PlayerController player)
        {
            if (!HasStateAuthority) return;
            HolderId = player.Object.Id;
            InFlight = false;
            FlightVelocity = Vector3.zero;
            FlightElapsed = 0f;
        }

        public void DetachToGround(Vector3 position)
        {
            if (!HasStateAuthority) return;
            HolderId = default;
            InFlight = false;
            FlightVelocity = Vector3.zero;
            transform.position = SnapToGround(position);
        }

        /// <summary>
        /// 只解除「誰拿著我」，不動位置也不落地。
        ///
        /// 給那種「物件要被別的系統接管、但不是掉在地上」的情況用
        /// （手提箱展開成攤位）。**不解除的話它會永遠貼在那個人手上** ——
        /// LateUpdate 只看 HolderId，不管玩家那邊的 HeldId 已經清掉了。
        /// </summary>
        public void ReleaseHolder()
        {
            if (!HasStateAuthority) return;
            HolderId = default;
        }

        /// <summary>
        /// 這個東西要蓄多久才丟得到最遠 —— 也就是它的**重量**。
        ///
        /// 羊毛幾乎瞬間、卡車要兩秒。重物不是「不能丟」，是「要站著舉一會兒」，
        /// 所以在混亂中丟重物本身就是一種風險。
        ///
        /// 用 ItemKind 分級而不是每個 prefab 拉一個數字，是為了讓重量表集中在一處；
        /// 特例（卡車）覆寫這支就好。
        /// </summary>
        public virtual float ThrowChargeSeconds => _kind switch
        {
            ItemKind.Wool        => GameTuning.ThrowChargeLight,
            ItemKind.Thread      => GameTuning.ThrowChargeLight,
            ItemKind.DyeMaterial => GameTuning.ThrowChargeLight,
            ItemKind.Accessory   => GameTuning.ThrowChargeLight,
            ItemKind.HairTonic   => GameTuning.ThrowChargeLight,

            ItemKind.Garment     => GameTuning.ThrowChargeMedium,
            ItemKind.Box         => GameTuning.ThrowChargeMedium,
            ItemKind.DyeCanister => GameTuning.ThrowChargeMedium,
            ItemKind.Shears      => GameTuning.ThrowChargeMedium,
            ItemKind.Leek        => GameTuning.ThrowChargeMedium,

            ItemKind.Suitcase    => GameTuning.ThrowChargeHeavy,
            ItemKind.Truck       => GameTuning.ThrowChargeTruck,

            _                    => GameTuning.ThrowChargeMedium,
        };

        /// <summary>
        /// 左鍵也能拿來蓄力丟出嗎。
        ///
        /// 預設 false：左鍵是「對前面的目標做事」，丟出歸 Q。
        /// 卡車是例外 —— 它唯一的用法就是丟出去，拿著它的時候左鍵沒有別的事好做，
        /// 讓兩個鍵都能丟比較順手，不用特地去找 Q。
        ///
        /// 為 true 的東西，**拿在手上時左鍵不再做情境互動**（不然一按就會
        /// 同時撿東西又開始蓄力）。要騰出手就先用 Q 點按放下。
        /// </summary>
        public virtual bool ChargesOnPrimary => false;

        public void LaunchFrom(PlayerController player, Vector3 direction)
            => LaunchFrom(player, direction, GameTuning.ThrowSpeed);

        /// <summary>
        /// 指定初速的丟出。卡車用它做「蓄力越久飛越遠」——
        /// 同樣的拋物線，只有初速不同，所以射程自然跟著變。
        /// </summary>
        public void LaunchFrom(PlayerController player, Vector3 direction, float speed)
        {
            if (!HasStateAuthority) return;
            HolderId = default;
            InFlight = true;
            FlightElapsed = 0f;
            ThrowerId = player.Object.Id;
            var dir = (direction.normalized + Vector3.up * GameTuning.ThrowUpwardRatio).normalized;
            FlightVelocity = dir * speed;
            transform.position = player.HandAnchor.position;
        }

        /// <summary>
        /// 飛行結束（撞到東西或落地）。預設什麼都不做 —— 東西就停在那裡等人撿。
        /// 卡車覆寫它來引爆。
        ///
        /// 注意這支**在物件還活著的時候**呼叫，可以安全地讀寫欄位；
        /// 要 Despawn 的話請自己負責。
        /// </summary>
        protected virtual void OnFlightEnded(bool hitSomething, Vector3 point) { }

        private void TickFlight(float dt)
        {
            FlightElapsed += dt;
            var v = FlightVelocity + Vector3.down * (GameTuning.ThrowGravity * dt);
            var next = transform.position + v * dt;

            if (Physics.Linecast(transform.position, next, out var hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<CarriableItem>() != this)
            {
                // 撞到的東西收得下這個物品 -> 直接進去，不用有人站旁邊按 Space
                var receiver = hit.collider.GetComponentInParent<IThrownItemReceiver>();
                if (receiver != null && receiver.CanAcceptThrown(this) && receiver.AcceptThrown(this))
                {
                    // 收下之後這個物件就沒用了。Despawn 之後不能再碰任何欄位。
                    Runner.Despawn(Object);
                    return;
                }

                // 撞到牆或地板：停下來
                InFlight = false;
                FlightVelocity = Vector3.zero;
                transform.position = hit.point + Vector3.up * _groundOffset;
                OnFlightEnded(true, hit.point);
                return;
            }

            if (FlightElapsed > GameTuning.ItemFlightMaxTime || next.y < -20f)
            {
                InFlight = false;
                FlightVelocity = Vector3.zero;
                transform.position = SnapToGround(transform.position);
                OnFlightEnded(false, transform.position);
                return;
            }

            FlightVelocity = v;
            transform.position = next;
        }

        private Vector3 SnapToGround(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * _groundOffset;
            return p;
        }

        /// <summary>
        /// 這個滯空中的物品能不能被 player 接到。
        ///
        /// requireFacing = true  -> 自動接住（空手時每個 tick 自己判定），要大致面向物品
        /// requireFacing = false -> 主動接住（按 Space／左鍵），範圍大一點、不管面向
        ///
        /// 兩種都排除「剛丟出去的那一瞬間、丟的人自己」——
        /// 沒有這條的話站著不動丟出去會立刻被自己接回來，等於丟不出去。
        /// </summary>
        public bool CanBeCaughtBy(PlayerController player, float radius, bool requireFacing, out float distance)
        {
            distance = float.MaxValue;
            if (!InFlight || player == null || player.Object == null) return false;

            if (ThrowerId.IsValid && ThrowerId == player.Object.Id
                && FlightElapsed < GameTuning.ThrowerCatchGrace) return false;

            var toItem = transform.position - player.CatchAnchor.position;
            distance = toItem.magnitude;
            if (distance > radius) return false;

            if (!requireFacing) return true;

            var flat = new Vector3(toItem.x, 0f, toItem.z);
            if (flat.sqrMagnitude < 0.0001f) return true;   // 正好在頭頂上
            return Vector3.Dot(player.transform.forward, flat.normalized) >= GameTuning.CatchFacingDot;
        }

        // ---------------- 外觀 ----------------

        private void ApplyTint(bool force)
        {
            if (_tintTargets == null || _tintTargets.Length == 0) return;
            var spec = Spec;
            if (!force && _renderedOnce && spec.Matches(_lastRenderedSpec)) return;

            _lastRenderedSpec = spec;
            _renderedOnce = true;

            Color c = _kind switch
            {
                ItemKind.Garment       => PlaceholderPalette.Dye(spec.Color),
                ItemKind.DyeMaterial   => PlaceholderPalette.Dye(spec.Color),
                ItemKind.DyeCanister   => PlaceholderPalette.Dye(spec.Color),
                ItemKind.Wool          => PlaceholderPalette.Wool,
                ItemKind.Thread        => PlaceholderPalette.Dye(spec.Color),
                ItemKind.UnfinishedGarment => Color.Lerp(PlaceholderPalette.Dye(spec.Color), Color.white, 0.35f),
                ItemKind.HairTonic     => PlaceholderPalette.HairTonic,
                ItemKind.Accessory     => PlaceholderPalette.Accessory,
                ItemKind.Box           => PlaceholderPalette.Box,
                _                      => Color.white,
            };
            // 工具與惡搞道具維持 prefab 配色 —— 它們的顏色是辨識用的，
            // 被 Spec.Color（預設白）蓋掉就全部變成白色方塊，分不出誰是誰
            if (_kind == ItemKind.Shears
                || _kind == ItemKind.Spit
                || _kind == ItemKind.Leek
                || _kind == ItemKind.Truck) return;

            _mpb ??= new MaterialPropertyBlock();
            foreach (var r in _tintTargets)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_Color", c);
                r.SetPropertyBlock(_mpb);
            }
        }

        private void UpdateColliders()
        {
            if (_colliders == null) return;
            bool enable = !IsHeld;
            foreach (var c in _colliders)
            {
                if (c == null || c.isTrigger) continue;
                if (c.enabled != enable) c.enabled = enable;
            }
        }

        // ---------------- IInteractable（撿起 / 被別的東西使用） ----------------

        public Transform InteractionAnchor => transform;
        public virtual int InteractionPriority => 0;

        /// <summary>
        /// **不要求空手。** 手上有東西的話，Interact 會先把它放到腳邊再撿
        /// —— 撿東西不該逼玩家先找地方放手上的東西。主動接住（TryManualCatch）
        /// 早就是這個手感了，這裡跟它一致。
        ///
        /// 手上拿著「能用在這個物件上」的東西時（IItemUser，例如把衣服裝進箱子）
        /// 那件事優先，不會變成放下箱子去撿衣服。
        /// </summary>
        public virtual bool CanInteract(in InteractionContext ctx)
        {
            if (IsHeld) return false;            // Phase 2 才有搶奪
            return ctx.Player != null;
        }

        public virtual string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.Held is IItemUser user && user.TryUseOnItem(this, ctx, false, out var prompt))
                return prompt;

            // 手上有東西時要先講明會放下什麼，不然玩家會覺得東西莫名其妙掉了
            if (!ctx.IsEmptyHanded)
                return $"[左鍵] 撿起 {DisplayName}（先放下 {ctx.Held.DisplayName}）";

            return $"[左鍵] 撿起 {DisplayName}";
        }

        public virtual void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (ctx.Held is IItemUser user && user.TryUseOnItem(this, ctx, true, out _))
                return;

            ctx.Player.Carry.MakeRoomForPickup();
            ctx.Player.Carry.TryPickup(this);
        }

        public virtual string DisplayName => _kind switch
        {
            ItemKind.Wool        => HasWork ? $"羊毛（紡了 {Work01 * 100f:F0}%）" : "羊毛",
            ItemKind.Thread      => PlaceholderPalette.DyeName(Spec.Color) + "絲線",
            ItemKind.DyeMaterial => PlaceholderPalette.DyeName(Spec.Color) + "染料",
            ItemKind.DyeCanister => PlaceholderPalette.DyeName(Spec.Color) + "顏料",
            ItemKind.Accessory   => PlaceholderPalette.AccessoryName(Spec.Accessory),
            ItemKind.HairTonic   => "生髮水",
            ItemKind.Garment     => Spec.Describe(),
            ItemKind.UnfinishedGarment => $"半成品{Spec.Describe()}（織了 {Work01 * 100f:F0}%）",
            ItemKind.Box         => "箱子",
            ItemKind.Shears      => "剃毛器",
            ItemKind.Spit        => "口水",
            ItemKind.Leek        => "大蔥",
            ItemKind.Truck       => "卡車",
            _                    => "物品",
        };
    }
}
