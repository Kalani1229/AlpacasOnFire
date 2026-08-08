using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Orders;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 郵箱：把裝好的箱子交出去，觸發訂單比對與計分。
    /// 出貨失敗時箱子會留在玩家手上（規格書明訂），只扣款並讓訂單區閃紅。
    /// </summary>
    public class Mailbox : NetworkInteractable
    {
        public override int InteractionPriority => 1;

        public override bool CanInteract(in InteractionContext ctx)
            => ctx.Held is BoxItem box && box.HasContent;

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.Held is BoxItem box)
                return box.HasContent ? $"[Space] 出貨 {box.Content.Describe()}" : "箱子是空的";
            return "需要裝好的箱子";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;
            if (ctx.Held is not BoxItem box || !box.HasContent) return;

            bool ok = OrderBoard.Instance != null && OrderBoard.Instance.TryDeliver(box.Content);
            if (!ok) return;   // 失敗：箱子留在手上

            ctx.Player.Carry.ConsumeHeld();
        }
    }
}
