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

        /// <summary>
        /// 被拿著的時候，各端都直接貼到手上錨點，不等網路位置同步（拿在手上不該有延遲）。
        /// 放在 LateUpdate 而不是 Render，確保一定蓋過 NetworkTransform 的插值結果。
        /// </summary>
        protected virtual void LateUpdate()
        {
            var holder = Holder;
            if (holder == null) return;
            transform.position = holder.HandAnchor.position;
            transform.rotation = holder.HandAnchor.rotation;
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
            ItemKind.Wool        => "羊毛",
            ItemKind.DyeMaterial => PlaceholderPalette.DyeName(Spec.Color) + "染料",
            ItemKind.DyeCanister => PlaceholderPalette.DyeName(Spec.Color) + "顏料",
            ItemKind.Accessory   => PlaceholderPalette.AccessoryName(Spec.Accessory),
            ItemKind.HairTonic   => "生髮水",
            ItemKind.Garment     => Spec.Describe(),
            ItemKind.Box         => "箱子",
            ItemKind.Shears      => "剃毛器",
            ItemKind.Spit        => "口水",
            ItemKind.Leek        => "大蔥",
            ItemKind.Truck       => "卡車",
            _                    => "物品",
        };
    }
}
