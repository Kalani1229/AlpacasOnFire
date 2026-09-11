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
        Suitcase = 10,     // 手提箱（擺攤系統；拿著時佔用雙手）
    }

    public enum PatternType : byte
    {
        None = 0,
        TShirt = 1,
        Pants = 2,
        Hat = 3,

        // 一律往後加，不重排 —— 既有的 prefab 與 LevelDefinition 存的是數值
        Shirt = 4,   // 襯衫：襯衫織布機的產出
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

        // ---- 擺攤系統（一律往後加，維持存檔相容）----
        DeliveryCounter = 18,  // 交貨窗口（顧客會排在窗口外側）
        Conveyor = 19,         // 輸送帶
        SuitcaseSpawn = 20,    // 手提箱的初始生成點（測試場景用）

        // 工具架：讓剃毛器與噴槍也能在網格上佔一格、也能被記進佈局。
        // 兩個類型共用同一個 prefab，由 DeviceType 決定架上放哪一種工具。
        ToolRackShears = 21,

        // 開張鈴：襯布邊上的固定設施，不進網格、不佔格子、不能搬動。
        // 敲下去就開張，然後鈴鐺自己縮起來消失；下一場要開張時會再出現。
        ServiceBell = 23,

        // ---- v6 羊駝村 ----
        // 24 與 25 是批 B 才會實作的裝備，這裡先把編號佔住 ——
        // 列舉值一旦用過就不能重排（存檔相容），先留位子比之後插隊安全。
        MaterialCrate  = 24,  // 素材箱：佈置時從背包擺出，互動一次跳一份羊毛
        WeavingMachine = 25,  // T恤織布機：吃 1-2 份毛 -> 主色 + 點綴色的 T-shirt
        WoolNpc        = 26,  // 會走動、可剃毛、也會來當顧客的 NPC

        // 第二台織布機。跟 25 共用 WeavingMachine.cs，只是 _outputPattern 不同 ——
        // 一台機器只做一種版型，想要兩種版型就得擺兩台，這是佔格子的取捨。
        // **不要把 25 改名或改值**：它已經被寫進 prefab 與佈局存檔了。
        WeavingMachineShirt = 27,  // 襯衫織布機
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

    /// <summary>
    /// 驗證結果。分成兩組用途：
    ///  - 開箱時（整塊襯布）：GroundTooSteep / NoGround / Obstructed
    ///  - 放裝備時（單一格子）：OutsideMat / CellOccupied
    /// 失敗原因會直接顯示在幽靈模型旁邊。
    /// </summary>
    public enum PlacementResult : byte
    {
        Ok = 0,
        OutsideMat = 1,      // 超出襯布範圍
        CellOccupied = 2,    // 這格已經被其他裝備佔住
        GroundTooSteep = 3,  // 地面不夠平
        NoGround = 4,        // 下方沒有地面
        NotDeploying = 5,    // 目前不是佈置模式
        NothingPending = 6,  // 沒有待放置的裝備
        Obstructed = 7,      // 襯布範圍內有障礙物，攤不開
    }

    /// <summary>
    /// 裝備在網格上的朝向。只有四向，沒有自由角度。
    /// 朝向有實際功能：+Z 是「正面」——
    ///   輸送帶往正面送、交貨窗口的窗口朝正面（顧客站那邊）、
    ///   機台的操作面在背面（玩家站 −Z 那一側操作）。
    /// </summary>
    public enum StallFacing : byte
    {
        North = 0, // +Z
        East  = 1, // +X
        South = 2, // −Z
        West  = 3, // −X
    }
}
