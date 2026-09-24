using UnityEngine;

namespace AlpacasOnFire.Core
{
    /// <summary>
    /// 佔位美術的統一色表（規格書第七節）。
    /// 注意：縫紉機改為橙色、人偶改為米色，避免和 P1 藍／P3 黃玩家撞色。
    /// </summary>
    public static class PlaceholderPalette
    {
        public static readonly Color[] PlayerColors =
        {
            new Color(0.20f, 0.45f, 0.95f), // P1 藍
            new Color(0.90f, 0.22f, 0.22f), // P2 紅
            new Color(0.95f, 0.82f, 0.20f), // P3 黃
            new Color(0.25f, 0.75f, 0.35f), // P4 綠
        };

        public static readonly Color Npc            = new Color(0.55f, 0.55f, 0.55f);
        public static readonly Color Wool           = new Color(0.96f, 0.96f, 0.94f);
        public static readonly Color HairTonic      = new Color(0.70f, 0.95f, 0.72f);
        public static readonly Color ToolBody       = new Color(0.72f, 0.74f, 0.78f); // 銀灰
        public static readonly Color ShearsBlade    = new Color(0.88f, 0.16f, 0.16f); // 紅刀鋒
        public static readonly Color SprayNozzle    = new Color(0.20f, 0.85f, 0.88f); // 青噴嘴
        public static readonly Color SewingMachine  = new Color(0.95f, 0.55f, 0.15f); // 橙（原本藍，避開 P1）
        public static readonly Color Juicer         = new Color(0.60f, 0.30f, 0.85f); // 紫
        public static readonly Color Mannequin      = new Color(0.92f, 0.87f, 0.75f); // 米（原本黃，避開 P3）
        public static readonly Color Recycling      = new Color(0.22f, 0.28f, 0.36f); // 深藍灰
        public static readonly Color Box            = new Color(0.72f, 0.54f, 0.32f); // 木箱色
        public static readonly Color Mailbox        = new Color(0.30f, 0.55f, 0.45f);
        public static readonly Color BoxDispenser   = new Color(0.55f, 0.42f, 0.26f);
        public static readonly Color Accessory      = new Color(0.95f, 0.45f, 0.75f);
        public static readonly Color Floor          = new Color(0.78f, 0.76f, 0.72f);
        public static readonly Color Wall           = new Color(0.52f, 0.50f, 0.48f);
        public static readonly Color Ground         = new Color(0.45f, 0.62f, 0.35f);

        public static Color PlayerColor(int index)
        {
            if (index < 0) index = 0;
            return PlayerColors[index % PlayerColors.Length];
        }

        public static Color Dye(DyeColorType c) => c switch
        {
            DyeColorType.Red    => new Color(0.85f, 0.18f, 0.18f),
            DyeColorType.Blue   => new Color(0.20f, 0.40f, 0.90f),
            DyeColorType.Green  => new Color(0.22f, 0.72f, 0.32f),
            DyeColorType.Yellow => new Color(0.93f, 0.83f, 0.22f),
            _                   => new Color(0.96f, 0.96f, 0.94f),
        };

        public static string DyeName(DyeColorType c) => c switch
        {
            DyeColorType.Red    => "紅",
            DyeColorType.Blue   => "藍",
            DyeColorType.Green  => "綠",
            DyeColorType.Yellow => "黃",
            _                   => "白",
        };

        public static string PatternName(PatternType p) => p switch
        {
            PatternType.TShirt => "T-shirt",
            PatternType.Shirt  => "襯衫",
            PatternType.Pants  => "褲子",
            PatternType.Hat    => "帽子",
            _                  => "無",
        };

        public static string AccessoryName(AccessoryType a) => a switch
        {
            AccessoryType.Button => "鈕扣",
            AccessoryType.Ribbon => "緞帶",
            AccessoryType.Badge  => "徽章",
            _                    => "無",
        };
    }
}
