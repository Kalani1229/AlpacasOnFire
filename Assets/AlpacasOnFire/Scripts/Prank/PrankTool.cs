using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;

namespace AlpacasOnFire.Prank
{
    /// <summary>
    /// 手持惡搞道具的共用底座（大蔥、卡車）。口水不在這裡 ——
    /// 它是羊駝自帶的能力，沒有實體道具，走 PlayerController.TrySpit()。
    ///
    /// **這些道具走左鍵**，優先權壓在情境互動前面 ——
    /// 拿著大蔥面對地上的羊毛時，左鍵應該是揮大蔥，不是放下大蔥去撿羊毛。
    ///
    /// 曾經搬到右鍵過，那是因為當時左鍵在「面前空無一物」時會改成丟出，
    /// 拿著大蔥想打人會先把大蔥扔掉。丟出整個搬到 Q 之後這個衝突就消失了，
    /// 道具使用也就回到它本來該在的地方。
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

        // ---------------- 出手 ----------------

        /// <summary>
        /// 按下左鍵。找準心前方的目標並出手。只在 StateAuthority 呼叫。
        ///
        /// **打不到人也回傳 true。** 手上拿著道具時左鍵永遠屬於這個道具 ——
        /// 不能因為沒打到就掉回情境互動，那會變成「揮空的時候會順手把腳邊的
        /// 羊毛撿起來」，而且大蔥還得先被放下。手上有武器就是揮武器。
        /// </summary>
        public virtual bool TryUse(in InteractionContext ctx)
        {
            if (!IsUsable(in ctx)) return false;   // 這個道具現在不能用 -> 讓給情境互動

            var target = PrankTargeting.FindTargetInFront(in ctx);
            if (target == null) return true;

            Hit(target, in ctx);
            if (ConsumeOnUse) ctx.Player.Carry.ConsumeHeld();
            return true;
        }

        /// <summary>
        /// 這個道具現在算不算「能用的道具」。
        /// 回 false 代表左鍵讓給情境互動 —— 卡車就是這樣：它不是武器，
        /// 是要用 Q 丟出去的重物，所以拿著卡車按左鍵仍然可以撿東西、操作機台。
        /// </summary>
        protected virtual bool IsUsable(in InteractionContext ctx) => true;

        /// <summary>
        /// HUD 的提示字。拿著道具時，準心前方有沒有人都要講清楚，
        /// 不然玩家會不知道左鍵現在有沒有用。回 null 代表這個道具沒有話要說。
        /// </summary>
        public virtual string BuildPrompt(in InteractionContext ctx)
        {
            if (!IsUsable(in ctx)) return null;

            var target = PrankTargeting.FindTargetInFront(in ctx);
            return target != null
                ? $"[左鍵] {ActionVerb}{target.StaggerDisplayName}"
                : $"[左鍵] {ActionVerb}（前面沒有人）";
        }
    }
}
