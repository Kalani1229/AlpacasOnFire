using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>人偶：把衣服掛上去當噴漆對象；空手互動可以取回衣服。</summary>
    public class Mannequin : NetworkInteractable, IGarmentHost
    {
        [SerializeField] private Transform _garmentAnchor;
        [SerializeField] private Renderer _garmentRenderer;
        [SerializeField] private Transform _paintProgressBar;

        [Networked] public NetworkBool Wearing { get; set; }
        [Networked] public GarmentSpec Worn { get; set; }
        [Networked] public float PaintProgress { get; set; }
        [Networked] public int PaintColorRaw { get; set; }

        private MaterialPropertyBlock _mpb;

        public override int InteractionPriority => 1;

        // ---------------- IGarmentHost ----------------

        public Transform GarmentAnchor => _garmentAnchor != null ? _garmentAnchor : transform;
        public bool HasGarment => Wearing;
        public GarmentSpec Garment => Worn;

        public bool TryWear(GarmentSpec spec)
        {
            if (!HasStateAuthority || Wearing) return false;
            Wearing = true;
            Worn = spec;
            PaintProgress = 0f;
            PaintColorRaw = (int)DyeColorType.White;
            return true;
        }

        public bool TryTakeOff(out GarmentSpec spec)
        {
            spec = Worn;
            if (!HasStateAuthority || !Wearing) return false;
            Wearing = false;
            Worn = default;
            PaintProgress = 0f;
            return true;
        }

        public void AddPaint(DyeColorType color, float amount)
        {
            if (!HasStateAuthority || !Wearing) return;

            if (PaintColorRaw != (int)color)
            {
                PaintColorRaw = (int)color;
                PaintProgress = 0f;
            }

            PaintProgress += amount;
            if (PaintProgress < GameTuning.SprayPaintRequired) return;

            PaintProgress = 0f;
            var spec = Worn;
            spec.Color = color;
            Worn = spec;
        }

        // ---------------- IInteractable ----------------

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (ctx.Held is IGarmentHostUser user)
                return user.TryUseOnHost(this, in ctx, false, out _);
            return ctx.IsEmptyHanded && Wearing;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.Held is IGarmentHostUser user && user.TryUseOnHost(this, in ctx, false, out var prompt))
                return prompt;
            if (ctx.IsEmptyHanded && Wearing) return $"[Space] 取下 {Worn.Describe()}";
            return "人偶（空）";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (ctx.Held is IGarmentHostUser user && user.TryUseOnHost(this, in ctx, true, out _))
                return;

            if (ctx.IsEmptyHanded && Wearing && TryTakeOff(out var spec))
            {
                ItemFactory.SpawnIntoHands(Runner, ItemKind.Garment, spec, ctx.Player);
                GameAudio.PlayAt(SfxId.DressOff, transform.position);
            }
        }

        public override void Render()
        {
            if (_garmentRenderer != null)
            {
                _garmentRenderer.enabled = Wearing;
                if (Wearing)
                {
                    _mpb ??= new MaterialPropertyBlock();
                    var c = PlaceholderPalette.Dye(Worn.Color);
                    _garmentRenderer.GetPropertyBlock(_mpb);
                    _mpb.SetColor("_BaseColor", c);
                    _mpb.SetColor("_Color", c);
                    _garmentRenderer.SetPropertyBlock(_mpb);
                }
            }

            if (_paintProgressBar != null)
            {
                bool show = Wearing && PaintProgress > 0.01f;
                if (_paintProgressBar.gameObject.activeSelf != show) _paintProgressBar.gameObject.SetActive(show);
                if (show)
                {
                    float t = Mathf.Clamp01(PaintProgress / GameTuning.SprayPaintRequired);
                    var s = _paintProgressBar.localScale;
                    _paintProgressBar.localScale = new Vector3(Mathf.Max(0.02f, t), s.y, s.z);
                }
            }
        }
    }
}
