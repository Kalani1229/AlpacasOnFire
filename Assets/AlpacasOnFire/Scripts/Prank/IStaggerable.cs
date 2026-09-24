using UnityEngine;

namespace AlpacasOnFire.Prank
{
    /// <summary>
    /// 「可以被惡搞的東西」。羊駝玩家與野生動物都實作它，所以三個道具
    /// 完全不必知道自己打到的是隊友還是羊 —— 打誰都是同一段程式碼。
    ///
    /// **道具只負責施加效果，效果本身不知道是誰打的。** 這是刻意的：
    /// 之後要加第四個惡搞，只會是「既有三個欄位的另一種組合」，
    /// 不需要新的介面、也不需要動被打的那一方。
    ///
    /// 三個效果對應「羊會跑」的三種解法：
    ///   矇眼（潛行）-> 看不見你就不逃
    ///   擊退（驅趕）-> 把牠推到你要的位置
    ///   失控（強攻）-> 打倒，身上的毛一次掉光
    /// </summary>
    public interface IStaggerable
    {
        /// <summary>被惡搞的判定位置（通常是身體中心）。</summary>
        Transform StaggerAnchor { get; }

        /// <summary>還活著而且可以被打（已經 Despawn 的、當顧客中的都回 false）。</summary>
        bool CanBeStaggered { get; }

        /// <summary>提示字用的稱呼（「隊友」「紅毛羊」……）。</summary>
        string StaggerDisplayName { get; }

        /// <summary>視覺干擾。只在 StateAuthority 呼叫。</summary>
        void ApplyBlind(float seconds);

        /// <summary>擊退。direction 會被壓平成水平。只在 StateAuthority 呼叫。</summary>
        void ApplyKnockback(Vector3 direction, float speed, float seconds);

        /// <summary>
        /// 失控倒地。**保留視覺、只拿走控制** —— 黑畫面會讓被害者連笑話都看不到。
        /// dropWool 為 true 時身上的毛會一次全部掉在地上。只在 StateAuthority 呼叫。
        /// </summary>
        void ApplyStagger(float seconds, bool dropWool);
    }
}
