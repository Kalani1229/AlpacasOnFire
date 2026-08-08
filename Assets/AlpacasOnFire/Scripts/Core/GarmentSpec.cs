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

        public bool IsValid => PatternRaw != (int)PatternType.None;

        public static GarmentSpec Create(PatternType pattern,
                                         DyeColorType color = DyeColorType.White,
                                         AccessoryType accessory = AccessoryType.None)
        {
            return new GarmentSpec
            {
                PatternRaw   = (int)pattern,
                ColorRaw     = (int)color,
                AccessoryRaw = (int)accessory,
            };
        }

        public bool Matches(GarmentSpec other)
        {
            return PatternRaw == other.PatternRaw
                && ColorRaw == other.ColorRaw
                && AccessoryRaw == other.AccessoryRaw;
        }

        public string Describe()
        {
            string s = PlaceholderPalette.DyeName(Color) + PlaceholderPalette.PatternName(Pattern);
            if (Accessory != AccessoryType.None)
                s += " + " + PlaceholderPalette.AccessoryName(Accessory);
            return s;
        }
    }
}
