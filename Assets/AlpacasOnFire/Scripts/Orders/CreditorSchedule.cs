using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Orders
{
    /// <summary>
    /// 債主的目標表（run 模式）。純靜態、沒有狀態 —— 同一個 day 永遠算出同一個數字，
    /// 所以兩端各自算也一定一致，不需要同步。
    ///
    /// 目標是**累積的資本額**，不是當天營收：資本額只進不出（還沒有商店／升級），
    /// 目標不累積的話過一次就永遠過。
    ///
    /// 每 <see cref="BalloonEveryDays"/> 天有一次「大額日」，那天的增量特別大。
    /// HUD 會用不同顏色標出來，並顯示未來三天的目標 —— 那是玩家唯一的預警。
    /// </summary>
    public static class CreditorSchedule
    {
        /// <summary>每幾天一次大額。</summary>
        public const int BalloonEveryDays = 3;

        public static bool IsBalloonDay(int day) => day > 0 && day % BalloonEveryDays == 0;

        /// <summary>第 day 天結束時，資本額至少要有多少（累積值）。</summary>
        public static int TargetFor(int day)
        {
            // 每天累加一筆「當日增量」，大額日的增量特別大。
            // 累加而不是直接給公式，是為了保證一定單調遞增。
            double total = 0;
            for (int d = 1; d <= day; d++) total += DailyIncrement(d);
            return Mathf.RoundToInt((float)total);
        }

        /// <summary>第 day 天的增量。HUD 想顯示「今天要多賺多少」時也用得到。</summary>
        public static double DailyIncrement(int day)
        {
            double baseline = GameTuning.DayTargetBase
                            * System.Math.Pow(GameTuning.DayTargetGrowth, day - 1);
            return IsBalloonDay(day) ? baseline * GameTuning.BalloonMultiplier : baseline;
        }
    }
}
