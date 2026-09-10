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
        /// 目前這一場用哪一套 loadout。**預設是 Classic** ——
        /// 什麼都不設定時（Stall_Test）行為跟拆分前完全一樣。
        /// 由 StallManager.Spawned() 依場景設定切換。
        /// </summary>
        public static StallLoadout Active { get; set; } = StallLoadout.Classic;

        /// <summary>手提箱的完整內容。開箱時**全部一次彈出**，沒有選單。</summary>
        public static LevelElementType[] Devices => Active.Devices;

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
            LevelElementType.WeavingMachine   => "織布機",
            LevelElementType.MaterialCrate    => "素材箱",
            _                                 => type.ToString(),
        };

        /// <summary>這台裝備的朝向有什麼實際作用（提示玩家用，不只是視覺旋轉）。</summary>
        public static string FacingMeaning(LevelElementType type) => type switch
        {
            LevelElementType.Conveyor        => "輸送方向",
            LevelElementType.MaterialCrate   => "開口面向（隨意）",
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
            LevelElementType.MaterialCrate    => new Vector3(GameTuning.MachineFootprint * 0.85f,
                                                            0.9f,
                                                            GameTuning.MachineFootprint * 0.85f),
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
            LevelElementType.WeavingMachine   => PlaceholderPalette.SewingMachine,
            LevelElementType.MaterialCrate    => PlaceholderPalette.Box,
            _                                 => Color.white,
        };

        // ---------------- 預設佈局 ----------------

        /// <summary>
        /// 第一次開箱用的預設佈局。實際內容看 StallLoadout ——
        /// 兩套 loadout 各有自己的排法，這裡只是轉接。
        /// </summary>
        public static StallSlotRecord[] DefaultLayout => Active.DefaultLayout;

        /// <summary>
        /// 襯布一定放得下全部裝備嗎。規格要求「不做裝不下就留在箱子裡的處理」，
        /// 所以這是一個必須永遠成立的前提 —— 不成立就是設計錯了，要在建置時就抓出來。
        /// </summary>
        public static bool MatFitsAllDevices(out int required, out int available)
            => MatFits(Active, out required, out available);

        /// <summary>指定 loadout 放不放得下。診斷選單會對兩套各跑一次。</summary>
        public static bool MatFits(StallLoadout loadout, out int required, out int available)
        {
            required = 0;
            foreach (var type in loadout.Devices)
            {
                var fp = Footprint(type);
                required += fp.x * fp.y;
            }
            available = StallGrid.CellCount;
            return required <= available && loadout.Devices.Length <= MaxSlots;
        }
    }
}
