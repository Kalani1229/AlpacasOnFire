using Fusion;

namespace AlpacasOnFire.Core
{
    /// <summary>
    /// 一件衣服的規格，同時也是訂單的需求描述。
    /// 是 INetworkStruct，所以可以直接放進 [Networked] 屬性與 NetworkArray。
    /// </summary>
    public struct GarmentSpec : INetworkStruct
    {
        public int PatternRaw;
        public int ColorRaw;
        public int AccessoryRaw;

        /// <summary>
        /// 點綴色（v6 織布機的第二份羊毛）。
        ///
        /// **「沒有點綴」的表示法是「點綴色 == 主色」**，不是某個特殊值。
        /// 這樣才不會跟「白色點綴」撞在一起 —— 白毛是 v6 的正常素材，
        /// 紅底白紋必須是一件描述得出來的衣服。
        /// </summary>
        public int AccentColorRaw;

        public PatternType Pattern
        {
            get => (PatternType)PatternRaw;
            set => PatternRaw = (int)value;
        }

        public DyeColorType Color
        {
            get => (DyeColorType)ColorRaw;
            set => ColorRaw = (int)value;
        }

        public AccessoryType Accessory
        {
            get => (AccessoryType)AccessoryRaw;
            set => AccessoryRaw = (int)value;
        }

        public DyeColorType AccentColor
        {
            get => (DyeColorType)AccentColorRaw;
            set => AccentColorRaw = (int)value;
        }

        /// <summary>
        /// 有沒有第二個顏色。點綴色等於主色時代表單色。
        ///
        /// 額外要求 Pattern != None，理由是專案裡有三個地方
        /// （DyeSourceNode、Juicer、AccessoryDispenser）是用物件初始化式
        /// `new GarmentSpec { ColorRaw = ... }` 直接建的，繞過了 Create() 的預設值，
        /// AccentColorRaw 會停在 0（白）。那些是染料與飾品、不是衣服，
        /// 沒有這個守衛的話紅色染料會被描述成「紅底白紋」，Stall_Test 的顯示就變了。
        /// 加這一條就不必去動那三個檔案。
        /// </summary>
        public bool HasAccent => AccentColorRaw != ColorRaw && PatternRaw != (int)PatternType.None;

        public bool IsValid => PatternRaw != (int)PatternType.None;

        /// <summary>
        /// accent 刻意用 nullable 而且**預設等於主色**，不是預設 White。
        ///
        /// 這一點很重要：如果預設成 White，那 OrderBoard 生出來的紅色訂單
        /// 就會變成「主色紅、點綴白」，Describe() 立刻開始附加「紅底白紋」，
        /// Stall_Test 的訂單卡文字就變了。預設成主色的話，所有既有呼叫點
        /// （SewingMachine、Juicer、OrderBoard）產生的都是單色衣服，行為完全不變。
        ///
        /// 也因為 C# 的預設參數不能引用其他參數，只好用 nullable 在函式內解。
        /// </summary>
        public static GarmentSpec Create(PatternType pattern,
                                         DyeColorType color = DyeColorType.White,
                                         AccessoryType accessory = AccessoryType.None,
                                         DyeColorType? accent = null)
        {
            return new GarmentSpec
            {
                PatternRaw      = (int)pattern,
                ColorRaw        = (int)color,
                AccessoryRaw    = (int)accessory,
                AccentColorRaw  = (int)(accent ?? color),
            };
        }

        public bool Matches(GarmentSpec other)
        {
            return PatternRaw == other.PatternRaw
                && ColorRaw == other.ColorRaw
                && AccessoryRaw == other.AccessoryRaw
                && AccentColorRaw == other.AccentColorRaw;
        }

        public string Describe()
        {
            // 有點綴色才用「X 底 Y 紋」的講法；單色維持舊格式，Stall_Test 的顯示不會變
            string s = HasAccent
                ? $"{PlaceholderPalette.DyeName(Color)}底{PlaceholderPalette.DyeName(AccentColor)}紋"
                  + PlaceholderPalette.PatternName(Pattern)
                : PlaceholderPalette.DyeName(Color) + PlaceholderPalette.PatternName(Pattern);

            if (Accessory != AccessoryType.None)
                s += " + " + PlaceholderPalette.AccessoryName(Accessory);
            return s;
        }
    }
}
