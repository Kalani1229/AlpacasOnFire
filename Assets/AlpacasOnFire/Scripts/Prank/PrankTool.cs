using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;

namespace AlpacasOnFire.Prank
{
    /// <summary>
    /// 手持惡搞道具的共用底座（大蔥、卡車）。口水不在這裡 ——
    /// 它是羊駝自帶的能力，沒有實體道具，走 PlayerController.TrySpit()。
    ///
    /// **這些道具走右鍵。** 左鍵已經被「接住 / 互動 / 丟出」三段佔滿了，
    /// 拿著大蔥想打人卻先把大蔥丟出去是最容易發生的誤操作。右鍵原本是
    /// 「設定類動作 + 持續使用工具」，出手歸在後者底下剛好。
    ///
    /// 效果本身（StaggerStatus）不知道是誰打的，所以要加第四個惡搞道具，
    /// 只要繼承這支、覆寫 Hit()，其他一行都不用動。
    /// </summary>
    public abstract class PrankTool : CarriableItem
    {
        /// <summary>用完就消失嗎（卡車是單次，大蔥可重複）。</summary>
        protected virtual bool ConsumeOnUse => false;

        /// <summary>提示字裡的動詞，例如「打」「砸」。</summary>
        protected abstract string ActionVerb { get; }

        /// <summary>真的打中了。只在 StateAuthority 呼叫。</summary>
        protected abstract void Hit(IStaggerable target, in InteractionContext ctx);

        /// <summary>可以打了嗎（卡車要蓄滿力才行）。</summary>
        protected virtual bool IsReady(in InteractionContext ctx) => true;

        /// <summary>還沒準備好時的提示字。</summary>
        protected virtual string NotReadyPrompt(in InteractionContext ctx) => null;

        /// <summary>
        /// 每個網路 tick 呼叫一次（只在 StateAuthority），held = **右鍵**是否按著。
        /// 只有需要蓄力的道具會用到，預設什麼都不做。
        /// </summary>
        public virtual void PrankTick(in InteractionContext ctx, bool held, float deltaTime) { }

        // ---------------- 出手 ----------------

        /// <summary>
        /// 按下右鍵。找準心前方的目標並出手。只在 StateAuthority 呼叫。
        /// 打不到人也回傳 true —— 右鍵在拿著惡搞道具時**永遠屬於這個道具**，
        /// 不能因為沒打到就掉回「次要互動」去打開手提箱面板。
        /// </summary>
        public virtual bool TryUse(in InteractionContext ctx)
        {
            if (!IsReady(in ctx)) return true;   // 蓄力中，吃掉這次右鍵但不出手

            var target = PrankTargeting.FindTargetInFront(in ctx);
            if (target == null) return true;

            Hit(target, in ctx);
            if (ConsumeOnUse) ctx.Player.Carry.ConsumeHeld();
            return true;
        }

        /// <summary>
        /// HUD 的提示字。拿著惡搞道具時，準心前方有沒有人都要講清楚，
        /// 不然玩家會不知道右鍵現在有沒有用。
        /// </summary>
        public string BuildPrompt(in InteractionContext ctx)
        {
            if (!IsReady(in ctx)) return NotReadyPrompt(in ctx);

            var target = PrankTargeting.FindTargetInFront(in ctx);
            return target != null
                ? $"[右鍵] {ActionVerb}{target.StaggerDisplayName}"
                : $"[右鍵] {ActionVerb}（前面沒有人）";
        }
    }
}
