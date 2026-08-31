using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 縫紉機：放入羊毛就開始縫，做好的衣服留在機台裡，玩家按 Space 取走。
    ///
    /// **一台機器只做一種版型**（由 _outputPattern 決定），不再有選版型的介面。
    /// 之後要做褲子機或帽子機，複製 prefab 改這個欄位就好，程式不用動。
    /// </summary>
    public class SewingMachine : MachineBase, IThrownItemReceiver
    {
        [Header("Sewing")]
        [Tooltip("這台機器產出的版型。一台機器只做一種。")]
        [SerializeField] private PatternType _outputPattern = PatternType.TShirt;
        [SerializeField] private Renderer[] _woolSlots;

        [Networked] public int WoolCount { get; set; }

        public PatternType OutputPattern => _outputPattern;

        public override int InteractionPriority => 1;

        protected override ItemKind OutputItemKind => ItemKind.Garment;

        protected override GarmentSpec BuildOutputSpec()
            => GarmentSpec.Create(_outputPattern, DyeColorType.White, AccessoryType.None);

        // ---------------- 互動 ----------------

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (HasOutput) return ctx.IsEmptyHanded;              // 先把成品拿走
            if (Processing) return false;
            return ctx.HeldKind == ItemKind.Wool;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (HasOutput)
                return ctx.IsEmptyHanded
                    ? $"[Space] 取出 {DescribeOutput()}"
                    : "先空出雙手才能取出成品";

            if (Processing) return $"縫製中… {Progress01 * 100f:F0}%";

            if (ctx.HeldKind == ItemKind.Wool)
                return GameTuning.SewingWoolRequired > 1
                    ? $"[Space] 放入羊毛（{WoolCount}/{GameTuning.SewingWoolRequired}）"
                    : $"[Space] 放入羊毛，縫製{PlaceholderPalette.PatternName(_outputPattern)}";

            return $"需要羊毛（做{PlaceholderPalette.PatternName(_outputPattern)}）";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (HasOutput)
            {
                TryTakeOutput(in ctx);
                return;
            }

            if (Processing || ctx.HeldKind != ItemKind.Wool) return;

            ctx.Player.Carry.ConsumeHeld();
            InsertWool();
        }

        /// <summary>放一份羊毛進去，湊滿就開工。手放進去和被丟進來共用這段。</summary>
        private void InsertWool()
        {
            WoolCount++;
            GameAudio.PlayAt(SfxId.Pickup, transform.position);

            if (WoolCount < GameTuning.SewingWoolRequired) return;

            WoolCount = 0;
            BeginProcess(GameTuning.SewingProcessSeconds);
        }

        // ---------------- 被丟進來的羊毛 ----------------

        public bool CanAcceptThrown(CarriableItem item)
            => item != null && item.Kind == ItemKind.Wool && !Processing && !HasOutput;

        public bool AcceptThrown(CarriableItem item)
        {
            if (!HasStateAuthority || !CanAcceptThrown(item)) return false;
            InsertWool();
            return true;
        }

        // ---------------- 外觀 ----------------

        public override void Render()
        {
            base.Render();
            if (_woolSlots == null) return;

            // prefab 上有三顆羊毛槽，但實際需要幾份由 GameTuning 決定；
            // 多出來的一律隱藏（之後美術重做 prefab 時再處理位置）
            for (int i = 0; i < _woolSlots.Length; i++)
            {
                if (_woolSlots[i] == null) continue;
                _woolSlots[i].enabled = i < GameTuning.SewingWoolRequired && i < WoolCount;
            }
        }
    }
}
