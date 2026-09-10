using AlpacasOnFire.Core;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 手提箱裡帶什麼。兩套並存 —— 這是「新場景不會弄壞舊場景」的第一道接縫。
    ///
    /// `Classic` 的內容跟拆分前**一字不差**，而 `StallCatalog.Active` 的預設值就是它，
    /// 所以什麼都不設定時（例如 Stall_Test）行為完全不變。
    /// 只有 Village 場景的建置器會把 StallManager 的 `_villageLoadout` 勾起來。
    /// </summary>
    public sealed class StallLoadout
    {
        public readonly string Name;

        /// <summary>開箱時會全部彈出來的裝備。</summary>
        public readonly LevelElementType[] Devices;

        /// <summary>第一次開箱用的排法。</summary>
        public readonly StallSlotRecord[] DefaultLayout;

        /// <summary>少了這些就不能開張（賣不出東西 / 做不出東西）。</summary>
        public readonly LevelElementType[] RequiredToOpen;

        /// <summary>手提箱要不要當料倉用（v6 才有；Classic 的素材是場上的染料點）。</summary>
        public readonly bool SuitcaseIsStash;

        private StallLoadout(string name, LevelElementType[] devices, StallSlotRecord[] layout,
                             LevelElementType[] required, bool suitcaseIsStash)
        {
            Name = name;
            Devices = devices;
            DefaultLayout = layout;
            RequiredToOpen = required;
            SuitcaseIsStash = suitcaseIsStash;
        }

        // ================= Classic（拆分前的原樣，不要改）=================

        /// <summary>
        /// Stall_Test 用的那一套。內容照抄拆分前的 `StallCatalog.Devices` 與 `DefaultLayout`，
        /// **一個值都不要動** —— 這是回歸測試的對照組。
        /// </summary>
        public static readonly StallLoadout Classic = new(
            "Classic",
            new[]
            {
                LevelElementType.ToolRackShears,
                LevelElementType.SewingMachine,
                LevelElementType.Juicer,
                LevelElementType.Mannequin,
                LevelElementType.DeliveryCounter,
                LevelElementType.Conveyor,
                LevelElementType.Conveyor,
            },
            new[]
            {
                StallSlotRecord.Create(LevelElementType.DeliveryCounter,  3, 6, (int)StallFacing.North),
                StallSlotRecord.Create(LevelElementType.Conveyor,         3, 5, (int)StallFacing.North),
                StallSlotRecord.Create(LevelElementType.Conveyor,         3, 4, (int)StallFacing.North),
                StallSlotRecord.Create(LevelElementType.SewingMachine,    3, 3, (int)StallFacing.North),
                StallSlotRecord.Create(LevelElementType.ToolRackShears,   3, 2, (int)StallFacing.North),
                StallSlotRecord.Create(LevelElementType.Juicer,           2, 3, (int)StallFacing.East),
                StallSlotRecord.Create(LevelElementType.Mannequin,        5, 3, (int)StallFacing.North),
            },
            new[]
            {
                LevelElementType.DeliveryCounter,
                LevelElementType.SewingMachine,
            },
            suitcaseIsStash: false);

        // ================= Village（v6 羊駝村）=================

        /// <summary>
        /// v6 的內容。沒有剃毛器架（剃毛器改成 E 鍵隨身工具）、
        /// 沒有果汁機與人偶（v6 沒有染色這條線）、也沒有素材箱
        /// （素材出口整合進手提箱本身，見 V6_WOOL_ECONOMY.md）。
        ///
        /// 只有 4 台裝備，襯布空很多 —— 這是刻意的，
        /// 動線優化的重點變成「織布機離手提箱多近」。
        ///
        ///     z=6            [交貨窗口]
        ///     z=5            [輸送帶↑]
        ///     z=4            [輸送帶↑]
        ///     z=3            [織布機]
        ///                    x=3
        ///
        /// 手提箱固定在襯布背緣外（不佔格子），玩家從那裡拿料、走到織布機、
        /// 成品自動上輸送帶送到交貨窗口。
        /// </summary>
        public static readonly StallLoadout Village = new(
            "Village",
            new[]
            {
                LevelElementType.WeavingMachine,
                LevelElementType.DeliveryCounter,
                LevelElementType.Conveyor,
                LevelElementType.Conveyor,
            },
            new[]
            {
                StallSlotRecord.Create(LevelElementType.DeliveryCounter, 3, 6, (int)StallFacing.North),
                StallSlotRecord.Create(LevelElementType.Conveyor,        3, 5, (int)StallFacing.North),
                StallSlotRecord.Create(LevelElementType.Conveyor,        3, 4, (int)StallFacing.North),
                StallSlotRecord.Create(LevelElementType.WeavingMachine,  3, 3, (int)StallFacing.North),
            },
            new[]
            {
                LevelElementType.DeliveryCounter,
                LevelElementType.WeavingMachine,
            },
            suitcaseIsStash: true);
    }
}
