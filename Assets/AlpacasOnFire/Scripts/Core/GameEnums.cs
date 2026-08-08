namespace AlpacasOnFire.Core
{
    /// <summary>手上／地上可攜帶物件的種類。Phase 2 新增種類直接往後加，不要插在中間（存檔相容）。</summary>
    public enum ItemKind : byte
    {
        None = 0,
        Wool = 1,          // 羊毛（白球）
        DyeMaterial = 2,   // 染色劑原料（依配方色小球）
        DyeCanister = 3,   // 果汁機產出的染劑罐
        Accessory = 4,     // 飾品
        HairTonic = 5,     // 生髮水（Phase 1 僅有物件，無效果）
        Garment = 6,       // 衣服
        Box = 7,           // 箱子
        Shears = 8,        // 剃毛器（工具）
        SprayGun = 9,      // 噴槍（工具）
    }

    public enum PatternType : byte
    {
        None = 0,
        TShirt = 1,
        Pants = 2,
        Hat = 3,
    }

    /// <summary>衣服顏色。White = 未染色的原色。</summary>
    public enum DyeColorType : byte
    {
        White = 0,
        Red = 1,
        Blue = 2,
        Green = 3,
        Yellow = 4,
    }

    public enum AccessoryType : byte
    {
        None = 0,
        Button = 1,
        Ribbon = 2,
        Badge = 3,
    }

    /// <summary>關卡編輯器可放置的元件類型。Phase 2/3 新增類型往後加。</summary>
    public enum LevelElementType : byte
    {
        PlayerSpawn = 0,
        Shears = 1,
        SprayGun = 2,
        SewingMachine = 3,
        Juicer = 4,
        Mannequin = 5,
        BoxDispenser = 6,
        Mailbox = 7,
        DyeSourceRed = 8,
        DyeSourceBlue = 9,
        DyeSourceGreen = 10,
        DyeSourceYellow = 11,
        AccessoryDispenser = 12,
        RecyclingMachine = 13, // Phase 2 才有邏輯，本階段只是佔位方塊
        Npc = 14,              // Phase 2 才有 AI，本階段只是佔位方塊
        FloorTile = 15,
        Wall = 16,
        OrderBoardAnchor = 17,
    }
}
