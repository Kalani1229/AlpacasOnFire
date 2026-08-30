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
        Suitcase = 10,     // 手提箱（擺攤系統；拿著時佔用雙手）
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

        // ---- 擺攤系統（本批新增，一律往後加，維持存檔相容）----
        DeliveryCounter = 18,  // 交貨窗口（顧客會排在窗口外側）
        Conveyor = 19,         // 輸送帶
        SuitcaseSpawn = 20,    // 手提箱的初始生成點（測試場景用）
    }

    /// <summary>
    /// 擺攤的四個階段。所有玩家看到的一致（StallManager 的 [Networked] 狀態）。
    ///
    /// 注意「佈置模式 vs 營業模式」不是第五個狀態，而是從這個列舉推導出來的：
    ///   佈置模式 = 襯布已展開 且 State 不是 Open／Settling
    ///   營業模式 = State == Open
    /// 這樣 Settling 結束回到 Exploring 之後，攤位還在地上，仍然可以繼續搬機台或收攤。
    /// </summary>
    public enum StallState : byte
    {
        Exploring = 0, // 探索中：自由移動，沒有計時
        Deploying = 1, // 佈置中：襯布已展開，可擺放機台，沒有計時
        Open      = 2, // 營業中：計時進行，機台不能移動
        Settling  = 3, // 結算中：跳出本場結算
    }

    /// <summary>放置驗證的結果。失敗原因會直接顯示在幽靈模型旁邊。</summary>
    public enum PlacementResult : byte
    {
        Ok = 0,
        OutsideMat = 1,      // 超出襯布範圍
        Overlapping = 2,     // 和其他機台太近
        GroundTooSteep = 3,  // 地面不夠平
        NoGround = 4,        // 下方沒有地面
        NotDeploying = 5,    // 目前不是佈置模式
        NothingPending = 6,  // 沒有待放置的機台
    }
}
