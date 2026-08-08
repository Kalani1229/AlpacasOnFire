using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Items
{
    /// <summary>箱子：裝一件衣服，拿去郵箱出貨。</summary>
    public class BoxItem : CarriableItem, IItemUser
    {
        [Networked] public NetworkBool HasContent { get; set; }
        [Networked] public GarmentSpec Content { get; set; }

        [SerializeField] private Renderer _contentIndicator;
        private MaterialPropertyBlock _mpb;

        public override int InteractionPriority => 1;

        /// <summary>手上拿著箱子，對地上的衣服按 Space -> 裝箱。</summary>
        public bool TryUseOnItem(CarriableItem target, in InteractionContext ctx, bool execute, out string prompt)
        {
            prompt = null;
            if (target == null || target.Kind != ItemKind.Garment) return false;
            if (HasContent) return false;

            prompt = $"[Space] 把 {target.Spec.Describe()} 裝進箱子";
            if (!execute) return true;

            Pack(target.Spec);
            Runner.Despawn(target.Object);
            return true;
        }

        public void Pack(GarmentSpec spec)
        {
            if (!HasStateAuthority) return;
            HasContent = true;
            Content = spec;
            GameAudio.PlayAt(SfxId.Pickup, transform.position);
        }

        public void Clear()
        {
            if (!HasStateAuthority) return;
            HasContent = false;
            Content = default;
        }

        /// <summary>地上的箱子 + 手上拿著衣服 -> 也能裝箱（反方向）。</summary>
        public override bool CanInteract(in InteractionContext ctx)
        {
            if (!IsHeld && !HasContent && ctx.HeldKind == ItemKind.Garment) return true;
            return base.CanInteract(in ctx);
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (!IsHeld && !HasContent && ctx.HeldKind == ItemKind.Garment)
                return $"[Space] 把 {ctx.Held.Spec.Describe()} 裝進箱子";
            return HasContent ? $"[Space] 撿起箱子（{Content.Describe()}）" : base.GetPrompt(in ctx);
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (!IsHeld && !HasContent && ctx.HeldKind == ItemKind.Garment)
            {
                Pack(ctx.Held.Spec);
                ctx.Player.Carry.ConsumeHeld();
                return;
            }
            base.Interact(in ctx);
        }

        public override void Render()
        {
            base.Render();
            if (_contentIndicator == null) return;

            _contentIndicator.enabled = HasContent;
            if (!HasContent) return;

            _mpb ??= new MaterialPropertyBlock();
            var c = PlaceholderPalette.Dye(Content.Color);
            _contentIndicator.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _contentIndicator.SetPropertyBlock(_mpb);
        }

        public override string DisplayName => HasContent ? $"箱子（{Content.Describe()}）" : "空箱子";
    }
}
