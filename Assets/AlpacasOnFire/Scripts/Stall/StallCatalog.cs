using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 手提箱裡有哪些裝備、每一種怎麼解析成 prefab。
    ///
    /// 刻意沿用既有的 LevelElementType 而不是另外開一個平行的列舉：
    /// 這樣關卡編輯器、GameCatalog、擺攤系統共用同一套識別碼，
    /// 之後 Phase 3 要把攤位佈局存進 LevelDefinition 時可以直接沿用。
    /// </summary>
    public static class StallCatalog
    {
        /// <summary>
        /// v1 一開始就給全部裝備（規格書：不做取得流程與手提箱升級）。
        /// 順序就是選單上的顯示順序。
        /// </summary>
        public static readonly LevelElementType[] Devices =
        {
            LevelElementType.Shears,
            LevelElementType.SewingMachine,
            LevelElementType.Juicer,
            LevelElementType.SprayGun,
            LevelElementType.Mannequin,
            LevelElementType.DeliveryCounter,
            LevelElementType.Conveyor,
        };

        /// <summary>工具類：放置時直接生成可攜帶物品，不需要 DeployableDevice。</summary>
        public static bool IsToolDevice(LevelElementType type)
            => type == LevelElementType.Shears || type == LevelElementType.SprayGun;

        public static ItemKind ToolKind(LevelElementType type) => type switch
        {
            LevelElementType.Shears   => ItemKind.Shears,
            LevelElementType.SprayGun => ItemKind.SprayGun,
            _                         => ItemKind.None,
        };

        public static string DisplayName(LevelElementType type) => type switch
        {
            LevelElementType.Shears          => "剃毛器",
            LevelElementType.SprayGun        => "噴槍",
            LevelElementType.SewingMachine   => "縫紉機",
            LevelElementType.Juicer          => "果汁機",
            LevelElementType.Mannequin       => "人偶",
            LevelElementType.DeliveryCounter => "交貨窗口",
            LevelElementType.Conveyor        => "輸送帶",
            _                                => type.ToString(),
        };

        /// <summary>取得可以被 Runner.Spawn 的機台 prefab。</summary>
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

        /// <summary>幽靈預覽用的外框尺寸。</summary>
        public static Vector3 GhostSize(LevelElementType type) => type switch
        {
            LevelElementType.Shears   => new Vector3(0.5f, 0.35f, 0.6f),
            LevelElementType.SprayGun => new Vector3(0.5f, 0.35f, 0.6f),
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
            LevelElementType.Shears          => PlaceholderPalette.ShearsBlade,
            LevelElementType.SprayGun        => PlaceholderPalette.SprayNozzle,
            LevelElementType.SewingMachine   => PlaceholderPalette.SewingMachine,
            LevelElementType.Juicer          => PlaceholderPalette.Juicer,
            LevelElementType.Mannequin       => PlaceholderPalette.Mannequin,
            LevelElementType.DeliveryCounter => PlaceholderPalette.Mailbox,
            LevelElementType.Conveyor        => PlaceholderPalette.BoxDispenser,
            _                                => Color.white,
        };
    }
}
