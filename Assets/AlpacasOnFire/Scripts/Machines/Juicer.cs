using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>果汁機：放入染料原料，處理一段時間後產出染劑罐（供噴槍填裝）。</summary>
    public class Juicer : MachineBase
    {
        [Networked] public int PendingColorRaw { get; set; }

        public override int InteractionPriority => 1;

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (Processing) return false;
            return ctx.HeldKind == ItemKind.DyeMaterial;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (Processing) return $"榨取中… {Progress01 * 100f:F0}%";
            if (ctx.HeldKind == ItemKind.DyeMaterial)
                return $"[Space] 放入{PlaceholderPalette.DyeName(ctx.Held.Spec.Color)}染料";
            return "需要染料原料";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || Processing) return;
            if (ctx.HeldKind != ItemKind.DyeMaterial) return;

            PendingColorRaw = (int)ctx.Held.Spec.Color;
            ctx.Player.Carry.ConsumeHeld();
            BeginProcess(GameTuning.JuicerProcessSeconds);
        }

        protected override void OnProcessComplete()
        {
            var spec = new GarmentSpec { ColorRaw = PendingColorRaw };
            ItemFactory.Spawn(Runner, ItemKind.DyeCanister, spec, OutputAnchor.position);
        }

        protected override Color StatusColor()
            => Processing ? PlaceholderPalette.Dye((DyeColorType)PendingColorRaw) : base.StatusColor();
    }
}
