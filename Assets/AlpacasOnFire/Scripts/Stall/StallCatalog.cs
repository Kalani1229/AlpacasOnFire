using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 手提箱裡有哪些裝備、每一種佔幾格、怎麼解析成 prefab。
    ///
    /// 刻意沿用既有的 LevelElementType 而不是另外開一個平行的列舉：
    /// 這樣關卡編輯器、GameCatalog、擺攤系統共用同一套識別碼。
    /// </summary>
    public static class StallCatalog
    {
        /// <summary>
        /// 手提箱的完整內容。開箱時**全部一次彈出**，沒有選單、沒有取得流程。
        ///
        /// 剃毛器與噴槍是手持工具，用「工具架」的形式進網格 ——
        /// 這樣每一台裝備都佔一格、都能被記進佈局、都能用同一套方式拿起來重擺。
        /// </summary>
        public static readonly LevelElementType[] Devices =
        {
            LevelElementType.ToolRackShears,
            LevelElementType.SewingMachine,
            LevelElementType.Juicer,
            LevelElementType.Mannequin,
            LevelElementType.DeliveryCounter,
            LevelElementType.Conveyor,
            LevelElementType.Conveyor,   // 兩條：同方向擺在一起會自動串成一條長線
        };

        /// <summary>佈局陣列的容量。留了餘裕，之後加裝備不用改同步結構。</summary>
        public const int MaxSlots = 16;

        /// <summary>
        /// 每台裝備佔幾格（朝向 0 時的 寬 x 深）。
        /// v1 全部 1x1，但整條驗證路徑都照 w x d 算 ——
        /// 之後要把交貨窗口改成 2x1，只要改這裡一個值就好。
        /// </summary>
        public static Vector2Int Footprint(LevelElementType type) => type switch
        {
            _ => new Vector2Int(1, 1),
        };

        public static bool IsToolRack(LevelElementType type)
            => type == LevelElementType.ToolRackShears;

        /// <summary>工具架上放的是哪一種工具。</summary>
        public static ItemKind RackToolKind(LevelElementType type) => type switch
        {
            LevelElementType.ToolRackShears   => ItemKind.Shears,
            _                                 => ItemKind.None,
        };

        public static string DisplayName(LevelElementType type) => type switch
        {
            LevelElementType.ToolRackShears   => "剃毛器架",
            LevelElementType.SewingMachine    => "縫紉機",
            LevelElementType.Juicer           => "果汁機",
            LevelElementType.Mannequin        => "人偶",
            LevelElementType.DeliveryCounter  => "交貨窗口",
            LevelElementType.Conveyor         => "輸送帶",
            _                                 => type.ToString(),
        };

        /// <summary>這台裝備的朝向有什麼實際作用（提示玩家用，不只是視覺旋轉）。</summary>
        public static string FacingMeaning(LevelElementType type) => type switch
        {
            LevelElementType.Conveyor        => "輸送方向",
            LevelElementType.DeliveryCounter => "窗口面向（顧客站這邊）",
            _                                => "操作面在背面",
        };

        /// <summary>取得可以被 Runner.Spawn 的裝備 prefab。</summary>
        public static NetworkObject DevicePrefab(LevelElementType type)
        {
            var catalog = GameCatalog.Instance;
            if (catalog == null) return null;

            var go = catalog.GetElement(type);
            if (go == null) return null;

            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
                Debug.LogError($"[擺攤] {type} 的 prefab（{go.name}）上面沒有 NetworkObject，無法在執行期生成。");
            return netObj;
        }

        // ---------------- 幽靈預覽的外觀 ----------------

        /// <summary>幽靈預覽用的外框尺寸。</summary>
        public static Vector3 GhostSize(LevelElementType type) => type switch
        {
            LevelElementType.ToolRackShears   => new Vector3(0.7f, 1.0f, 0.55f),
            LevelElementType.Conveyor => new Vector3(GameTuning.ConveyorWidth,
                                                    GameTuning.ConveyorHeight,
                                                    GameTuning.ConveyorLength),
            LevelElementType.DeliveryCounter => new Vector3(GameTuning.MachineFootprint,
                                                           GameTuning.DeliveryCounterHeight,
                                                           GameTuning.MachineFootprint),
            _ => new Vector3(GameTuning.MachineFootprint,
                             GameTuning.MachineHeight,
                             GameTuning.MachineFootprint),
        };

        /// <summary>幽靈預覽的底色（合法時用，不合法一律轉紅）。</summary>
        public static Color GhostColor(LevelElementType type) => type switch
        {
            LevelElementType.ToolRackShears   => PlaceholderPalette.ShearsBlade,
            LevelElementType.SewingMachine    => PlaceholderPalette.SewingMachine,
            LevelElementType.Juicer           => PlaceholderPalette.Juicer,
            LevelElementType.Mannequin        => PlaceholderPalette.Mannequin,
            LevelElementType.DeliveryCounter  => PlaceholderPalette.Mailbox,
            LevelElementType.Conveyor         => PlaceholderPalette.BoxDispenser,
            _                                 => Color.white,
        };

        // ---------------- 預設佈局 ----------------

        /// <summary>
        /// 第一次開箱用的預設佈局（6 x 6 格，(0,0) 在左後角，z 往前 = 顧客那一側）。
        ///
        /// 這個排法是照白色 T-shirt 的動線設計的，開箱就能直接跑通：
        ///
        ///     z=5                    [交貨窗口]
        ///     z=4                    [輸送帶↑]
        ///     z=3                    [輸送帶↑]
        ///     z=2   [果汁機]         [縫紉機]      [人偶]
        ///     z=1                    [剃毛器架]
        ///           x=1              x=2           x=3
        ///
        /// 剃毛 -> 縫紉機 -> 縫紉機自動把成品送上輸送帶 -> 兩條輸送帶接力送到交貨窗口。
        /// 染色支線（果汁機榨出顏料 -> 拿去刷人偶身上的衣服）掛在旁邊，不擋主線，
        /// 而且刻意不跟輸送帶正交相鄰，免得染劑罐被自動送到交貨窗口去。
        /// </summary>
        public static readonly StallSlotRecord[] DefaultLayout =
        {
            StallSlotRecord.Create(LevelElementType.DeliveryCounter,  2, 5, (int)StallFacing.North),
            StallSlotRecord.Create(LevelElementType.Conveyor,         2, 4, (int)StallFacing.North),
            StallSlotRecord.Create(LevelElementType.Conveyor,         2, 3, (int)StallFacing.North),
            StallSlotRecord.Create(LevelElementType.SewingMachine,    2, 2, (int)StallFacing.North),
            StallSlotRecord.Create(LevelElementType.ToolRackShears,   2, 1, (int)StallFacing.North),
            StallSlotRecord.Create(LevelElementType.Juicer,           1, 2, (int)StallFacing.East),
            StallSlotRecord.Create(LevelElementType.Mannequin,        3, 2, (int)StallFacing.North),
        };

        /// <summary>
        /// 襯布一定放得下全部裝備嗎。規格要求「不做裝不下就留在箱子裡的處理」，
        /// 所以這是一個必須永遠成立的前提 —— 不成立就是設計錯了，要在建置時就抓出來。
        /// </summary>
        public static bool MatFitsAllDevices(out int required, out int available)
        {
            required = 0;
            foreach (var type in Devices)
            {
                var fp = Footprint(type);
                required += fp.x * fp.y;
            }
            available = StallGrid.CellCount;
            return required <= available && Devices.Length <= MaxSlots;
        }
    }
}
