using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>戶外的染料原料點：採集後消失，過幾秒固定重生。</summary>
    public class DyeSourceNode : NetworkInteractable
    {
        [SerializeField] private DyeColorType _color = DyeColorType.Red;
        [SerializeField] private Renderer _blobRenderer;

        [Networked] public NetworkBool Available { get; set; }
        [Networked] private TickTimer RespawnTimer { get; set; }

        private MaterialPropertyBlock _mpb;

        public DyeColorType Color => _color;

        public override void Spawned()
        {
            if (HasStateAuthority) Available = true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || Available) return;
            if (!RespawnTimer.Expired(Runner)) return;

            Available = true;
            RespawnTimer = default;
        }

        public override bool CanInteract(in InteractionContext ctx) => Available && ctx.IsEmptyHanded;

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (!Available)
            {
                float t = RespawnTimer.RemainingTime(Runner) ?? 0f;
                return $"重生中… {t:F1}s";
            }
            return ctx.IsEmptyHanded ? $"[Space] 採集{PlaceholderPalette.DyeName(_color)}染料" : "先空出雙手";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !Available || !ctx.IsEmptyHanded) return;

            var spec = new GarmentSpec { ColorRaw = (int)_color };
            ItemFactory.SpawnIntoHands(Runner, ItemKind.DyeMaterial, spec, ctx.Player);

            Available = false;
            RespawnTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.DyeSourceRespawnSeconds);
        }

        public override void Render()
        {
            if (_blobRenderer == null) return;
            _blobRenderer.enabled = Available;
            _mpb ??= new MaterialPropertyBlock();
            var c = PlaceholderPalette.Dye(_color);
            _blobRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _blobRenderer.SetPropertyBlock(_mpb);
        }
    }
}
