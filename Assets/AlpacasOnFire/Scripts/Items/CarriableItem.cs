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
        {
            if (!HasStateAuthority) return;
            HolderId = default;
            InFlight = true;
            FlightElapsed = 0f;
            var dir = (direction.normalized + Vector3.up * GameTuning.ThrowUpwardRatio).normalized;
            FlightVelocity = dir * GameTuning.ThrowSpeed;
            transform.position = player.HandAnchor.position;
        }

        private void TickFlight(float dt)
        {
            FlightElapsed += dt;
            var v = FlightVelocity + Vector3.down * (GameTuning.ThrowGravity * dt);
            var next = transform.position + v * dt;

            // 撞到場景或落地就停下來
            if (Physics.Linecast(transform.position, next, out var hit, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<CarriableItem>() != this)
            {
                InFlight = false;
                FlightVelocity = Vector3.zero;
                transform.position = hit.point + Vector3.up * _groundOffset;
                return;
            }

            if (FlightElapsed > GameTuning.ItemFlightMaxTime || next.y < -20f)
            {
                InFlight = false;
                FlightVelocity = Vector3.zero;
                transform.position = SnapToGround(transform.position);
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

        /// <summary>這個物件是不是正朝著 player 飛過來（Q 鍵接住判定用）。</summary>
        public bool IsFlyingToward(PlayerController player, out float distance)
        {
            distance = float.MaxValue;
            if (!InFlight || player == null) return false;

            var toPlayer = player.CatchAnchor.position - transform.position;
            distance = toPlayer.magnitude;
            if (distance > GameTuning.CatchRadius) return false;

            // 必須是正在接近，不然剛丟出去的東西會被自己接回來
            float closing = Vector3.Dot(FlightVelocity, toPlayer.normalized);
            return closing >= GameTuning.CatchMinClosingSpeed;
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
            if (_kind == ItemKind.Shears || _kind == ItemKind.SprayGun) return; // 工具維持 prefab 配色

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

        public virtual bool CanInteract(in InteractionContext ctx)
        {
            if (IsHeld) return false;            // Phase 2 才有搶奪
            if (ctx.Player == null) return false;

            if (ctx.Held is IItemUser user)
                return user.TryUseOnItem(this, ctx, false, out _);

            return ctx.IsEmptyHanded;
        }

        public virtual string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.Held is IItemUser user && user.TryUseOnItem(this, ctx, false, out var prompt))
                return prompt;
            return $"[Space] 撿起 {DisplayName}";
        }

        public virtual void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (ctx.Held is IItemUser user && user.TryUseOnItem(this, ctx, true, out _))
                return;

            if (ctx.IsEmptyHanded)
                ctx.Player.Carry.TryPickup(this);
        }

        public virtual string DisplayName => _kind switch
        {
            ItemKind.Wool        => "羊毛",
            ItemKind.DyeMaterial => PlaceholderPalette.DyeName(Spec.Color) + "染料",
            ItemKind.DyeCanister => PlaceholderPalette.DyeName(Spec.Color) + "染劑",
            ItemKind.Accessory   => PlaceholderPalette.AccessoryName(Spec.Accessory),
            ItemKind.HairTonic   => "生髮水",
            ItemKind.Garment     => Spec.Describe(),
            ItemKind.Box         => "箱子",
            ItemKind.Shears      => "剃毛器",
            ItemKind.SprayGun    => "噴槍",
            _                    => "物品",
        };
    }
}
