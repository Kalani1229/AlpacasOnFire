using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 果汁機：放入染料原料，榨好的染劑罐留在機台裡，玩家按 Space 取走。
    /// 跟縫紉機採同一套「做好留在機台、手動取貨」的互動。
    ///
    /// 顏色規則：處理中維持一般的忙碌色（黃），**做好了才亮出染劑的顏色** ——
    /// 這樣「顏色」只代表一件事：可以來拿了。
    /// </summary>
    public class Juicer : MachineBase, IThrownItemReceiver
    {
        [Networked] public int PendingColorRaw { get; set; }

        public override int InteractionPriority => 1;

        protected override ItemKind OutputItemKind => ItemKind.DyeCanister;

        protected override GarmentSpec BuildOutputSpec()
            => new GarmentSpec { ColorRaw = PendingColorRaw };

        protected override string DescribeOutput()
            => PlaceholderPalette.DyeName((DyeColorType)OutputSpec.ColorRaw) + "染劑";

        // ---------------- 互動 ----------------

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (HasOutput) return ctx.IsEmptyHanded;
            if (Processing) return false;
            return ctx.HeldKind == ItemKind.DyeMaterial;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (HasOutput)
                return ctx.IsEmptyHanded
                    ? $"[Space] 取出 {DescribeOutput()}"
                    : "先空出雙手才能取出染劑";

            if (Processing) return $"榨取中… {Progress01 * 100f:F0}%";

            if (ctx.HeldKind == ItemKind.DyeMaterial)
                return $"[Space] 放入{PlaceholderPalette.DyeName(ctx.Held.Spec.Color)}染料";

            return "需要染料原料";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (HasOutput)
            {
                TryTakeOutput(in ctx);
                return;
            }

            if (Processing || ctx.HeldKind != ItemKind.DyeMaterial) return;

            var color = ctx.Held.Spec.Color;
            ctx.Player.Carry.ConsumeHeld();
            InsertDye(color);
        }

        /// <summary>放一份染料原料進去就開工。手放進去和被丟進來共用這段。</summary>
        private void InsertDye(DyeColorType color)
        {
            PendingColorRaw = (int)color;
            BeginProcess(GameTuning.JuicerProcessSeconds);
        }

        // ---------------- 被丟進來的染料 ----------------

        public bool CanAcceptThrown(CarriableItem item)
            => item != null && item.Kind == ItemKind.DyeMaterial && !Processing && !HasOutput;

        public bool AcceptThrown(CarriableItem item)
        {
            if (!HasStateAuthority || !CanAcceptThrown(item)) return false;
            InsertDye(item.Spec.Color);
            return true;
        }

        // ---------------- 外觀 ----------------

        /// <summary>做好了才亮染劑的顏色。</summary>
        protected override Color DoneColor => PlaceholderPalette.Dye((DyeColorType)OutputSpec.ColorRaw);
    }
}
