using UnityEngine;

namespace AlpacasOnFire.Core
{
    /// <summary>
    /// Phase 1 的所有可調數值集中在這裡。
    /// 之後 Phase 3 做多關卡資料驅動時，關卡相關的數值會搬到 LevelDefinition，
    /// 這裡只留下「全域預設值」。
    /// </summary>
    public static class GameTuning
    {
        // ---------- 角色 ----------
        public const float AlpacaHeight        = 1.8f;   // 羊駝站立高度（膠囊）
        public const float AlpacaRadius        = 0.35f;
        // 以下四個值會在 PlayerController.Spawned() 餵給 Fusion 的 NetworkCharacterController
        public const float MoveSpeed           = 5.0f;   // m/s（maxSpeed）
        public const float MoveAcceleration    = 40f;    // m/s^2（acceleration）
        public const float MoveBraking         = 30f;    // 放開按鍵的減速（braking）
        public const float Gravity             = 20f;    // m/s^2（會以 -Gravity 傳入）
        public const float CarrySlowFactor     = 0.85f;  // 手上有東西時的速度倍率

        // ---------- 攝影機 ----------
        public const float CameraDistance      = 4.5f;   // 理想距離
        public const float CameraHeight        = 1.5f;   // 以角色腳底為基準的樞紐高度
        public const float CameraSideOffset    = 0.35f;  // 稍微偏右，避免角色擋住準心
        public const float CameraPitchMin      = -70f;  // 負值＝往上看。畫布很高，要抬得夠
        public const float CameraPitchMax      = 70f;
        public const float CameraDefaultPitch  = 18f;
        public const float CameraFollowSmooth  = 0.08f;  // SmoothDamp 時間
        public const float CameraCollisionRadius = 0.25f;// SphereCast 半徑
        public const float CameraCollisionBuffer = 0.20f;// 撞到牆後再往前縮的距離
        public const float CameraZoomInSpeed   = 40f;    // 遇到遮擋時往前縮（瞬間感）
        public const float CameraZoomOutSpeed  = 6f;     // 離開遮擋後往後放（慢慢來）
        public const float MouseSensitivityX   = 0.12f;  // 度 / 像素
        public const float MouseSensitivityY   = 0.10f;

        // ---------- 互動 ----------
        public const float InteractRange       = 2.5f;   // 從頭部沿鏡頭方向
        public const float InteractProbeRadius = 0.35f;  // SphereCast 半徑，容錯用
        public const float EyeHeight           = 1.5f;

        // ---------- 丟出 / 接住 ----------
        // 平視丟出時的水平射程約 7.8 公尺 ＝ 襯布網格 5 格多一點。
        // 速度拉高、上拋比例壓低 —— 射程一樣但飛得又快又平（滯空 0.5 秒，原本 0.62 秒）。
        // 平一點也比較好瞄準機台，扔進機台才有機會成功。
        public const float ThrowSpeed          = 16f;    // m/s（蓄滿力的初速）
        public const float ThrowUpwardRatio    = 0.18f;  // 往上加的比例
        public const float ThrowGravity        = 20f;

        // ---------- Q：點按放下、長按蓄力丟出 ----------
        //
        // 同一個鍵做兩件事，靠「按多久」分開：
        //   按一下就放 -> 放下（掉在腳邊）
        //   按住再放開 -> 丟出，蓄多久就飛多遠
        //
        // 為什麼不拆成兩個鍵：放下與丟出是同一個意圖（我不要這個東西了）的
        // 兩種力道，綁在同一個鍵上，手比較不用記東西。

        /// <summary>按不到這麼久就算「點按」＝放下。太長會讓放下變得遲鈍。</summary>
        public const float ThrowTapSeconds     = 0.12f;

        /// <summary>沒蓄滿力的最低初速。**蓄一點點也丟得出去，只是很近。**</summary>
        public const float ThrowSpeedMin       = 5f;

        // 蓄滿力需要的時間 ＝ 道具的重量。輕的東西幾乎不用蓄，重的要站著舉。
        // 這是「拿著重物就不能靈活行動」這條規則的時間版本。
        public const float ThrowChargeLight    = 0.15f;  // 羊毛、染料、飾品 —— 幾乎瞬間
        public const float ThrowChargeMedium   = 0.5f;   // 衣服、箱子、工具
        public const float ThrowChargeHeavy    = 1.1f;   // 手提箱
        public const float ThrowChargeTruck    = 2f;     // 卡車
        // 接住分兩層：
        //  自動 —— 空手 + 大致面向 + 小範圍，什麼都不用按
        //  主動 —— 按接住鍵（Space／左鍵），範圍大一點、也不要求面向，
        //          而且**接到了才會**把原本手上的東西放到腳邊
        public const float CatchAutoRadius     = 1.8f;   // 自動接住的半徑
        public const float CatchManualRadius   = 2.6f;   // 按鍵接住的半徑（比自動大一點點）
        public const float CatchFacingDot      = 0.25f;  // 自動接住要「大致面向」的夾角（約 ±75 度）
        /// <summary>剛丟出去的這段時間，丟的人自己接不到 —— 不然站著不動丟會馬上被自己接回來。</summary>
        public const float ThrowerCatchGrace   = 0.45f;
        public const float ItemFlightMaxTime   = 6f;     // 保險：飛太久就落地

        // ---------- 剃毛 ----------
        public const int   FleeceMax           = 3;      // 每隻羊駝身上最多 3 份毛
        public const float FleeceRegenSeconds  = 6f;     // 每 6 秒長回 1 份
        public const int   WoolPerShear        = 1;      // 每次剃毛掉 1 顆羊毛

        // 剃毛器伸出來多久。它不是道具、沒有攜帶狀態，只是動作的一個瞬間，
        // 所以要短到像揮一下、又長到看得見。
        public const float ShearVisualSeconds  = 0.35f;

        // ---------- 縫紉機 ----------
        public const int   SewingWoolRequired  = 1;      // 1 份羊毛 = 1 件衣服
        public const float SewingProcessSeconds= 6.0f;

        // ---------- 果汁機 ----------
        public const float JuicerProcessSeconds= 4.0f;   // 1 份原料 -> 1 罐染劑

        // ---------- 塗抹（染劑罐）----------
        public const float PaintCapacity        = 100f;  // 一罐顏料的總量
        public const float PaintDrainPerSecond  = 4.4f;  // 按住右鍵每秒消耗，滿罐約可刷 22 秒
        public const float PaintRange           = 2.6f;  // 刷得到的距離
        public const float PaintBrushRadiusUv   = 0.05f; // 筆刷半徑（UV 空間，0~1）
        public const float PaintCoverageRequired= 0.18f; // 塗到不到兩成就算是這個顏色的衣服
        /// <summary>達標時邊框發光幾秒，告訴玩家「這件完成了」。</summary>
        public const float PaintCompleteGlowSeconds = 3f;

        // 衣服外形：從上往下看是「兩邊寬、兩邊很窄」的長方體，像一片掛起來的布。
        // 正反面共用同一張塗抹圖（圖案會透過去），所以用平面投影而不是圓柱投影。
        // 掛在人偶上的那件是「畫布」。做成正方形（取較寬的那邊）——
        // 這樣 32x32 的遮罩不會被拉長，筆刷在畫面上才是圓的。
        public static readonly Vector3 GarmentOnHostSize = new Vector3(4.0f, 4.0f, 0.22f);
        /// <summary>畫布中心離地多高（正方形之後不能再用人偶身高推算，會插到地板下）。</summary>
        public const float GarmentHostCenterY = 2.15f;
        public static readonly Vector3 GarmentItemSize   = new Vector3(0.58f, 0.42f, 0.08f);
        /// <summary>掛在人偶身上時往操作面推出來多少，避免整片埋進人偶本體裡。</summary>
        public const float GarmentFrontOffset = 0.72f;
        /// <summary>自己的身體擋住畫布時淡到多透明。</summary>
        public const float LocalPlayerFadeAlpha = 0.28f;

        // ---------- 素材點 ----------
        public const float DyeSourceRespawnSeconds = 5f;

        // ---------- 訂單 / 經濟 ----------
        public const float LevelDurationSeconds   = 180f; // 第一關 3 分鐘
        public const int   MaxActiveOrders         = 4;
        public const float FirstOrderDelaySeconds  = 3f;
        public const float OrderIntervalSeconds    = 20f;
        public const float OrderLifetimeSeconds    = 75f; // 訂單卡倒數
        public const int   OrderBaseReward         = 100;
        public const int   OrderAccessoryBonus     = 20;
        public const int   OrderTimeoutPenalty     = 40;  // 超時扣款
        public const int   WrongDeliveryPenalty    = 25;  // 錯誤出貨扣款

        // ---------- 星級門檻 ----------
        public const int   Star1Threshold = 300;
        public const int   Star2Threshold = 600;
        public const int   Star3Threshold = 900;

        // ---------- 機台外型 ----------
        public const float MachineHeight    = 1.9f;  // 略高於羊駝站立高度 1.8
        public const float MachineFootprint = 1.2f;

        // ================= 擺攤系統（本批新增；以上既有數值一律沒有改動）=================

        // ---------- 襯布網格（PlateUp! 式）----------
        public const float StallCellSize           = 1.5f;   // 網格單格邊長（公尺）
        public const int   StallGridCells          = 8;      // 襯布是 8 x 8 格（12 x 12 公尺）
        /// <summary>襯布邊長 = 6 x 1.5 = 9 公尺。改格數或格子大小，這裡會自動跟著變。</summary>
        public const float StallMatSize            = StallGridCells * StallCellSize;

        public const float StallMatDeployDistance  = 1.2f;   // 襯布近邊離玩家的距離
        public const float StallMatHeightOffset    = 0.02f;  // 襯布貼地的抬升，避免 Z-fighting
        public const float StallGridLineWidth      = 0.05f;  // 佈置模式的網格線寬度
        public const float StallSuitcaseBackOffset = 0.9f;   // 手提箱擺在襯布背緣外多遠（不佔格子）
        // ---------- 開張鈴 ----------
        public const float StallBellSideOffset     = 1.8f;   // 鈴鐺在背緣往右偏多遠（不佔格子）
        public const float StallBellHeight         = 1.0f;   // 鈴鐺台面高度
        public const float StallBellShrinkDuration = 0.45f;  // 敲下去之後縮起來消失的時間

        // ---------- 開箱時的地面檢測（只有開箱做一次，裝備不再各自檢測）----------
        public const float StallDeployProbeInset   = 0.35f;  // 四角檢測往內縮，避免剛好卡在邊界
        public const float StallGroundProbeHeight  = 3.0f;   // 平坦度檢測射線的起點高度
        public const float StallGroundProbeLength  = 6.0f;   // 平坦度檢測射線長度
        public const float StallMaxGroundAngle     = 12.0f;  // 地面法線與垂直的最大容許角度（度）
        public const float StallMaxGroundStep      = 0.45f;  // 四角高低差的最大容許值（公尺）
        public const float StallClearanceHeight    = 2.2f;   // 襯布上方要淨空多高才攤得開
        public const float StallClearanceInset     = 0.25f;  // 淨空檢測往內縮，避免擦到旁邊的牆

        // ---------- 裝備放置 ----------
        public const float StallPlaceMaxDistance   = 12.0f;  // 準心投影的最遠距離
        public const float StallDeployDuration     = 0.0f;   // 擺放／收回耗時（0 = 瞬間，先不做長按）
        public const float StallCollectRadius      = 1.2f;   // 收攤時清除襯布上物品的額外邊界
        /// <summary>擺攤期間玩家能走出襯布邊緣多遠。襯布 + 這個邊界＝「擺攤區域」。</summary>
        // 擺攤期間的空氣牆已經移除，這個邊界值目前沒有人在用。
        // 留著是因為它是「攤位範圍」的定義之一，之後要做「走太遠就自動收攤」
        // 或是攤位地面的視覺範圍時會再用到。
        public const float StallZoneMargin         = 2.5f;

        // ---------- 第一人稱（Tab 切換）----------
        //
        // 鏡頭從 HeadAnchor 再往前推一點點。0 的話鏡頭正好在頭的中心，
        // 近裁切面會切進自己的鼻子（Snout 那塊方塊）；推出去一點就乾淨了。
        // 推太多又會變成「靈魂出竅」，走到牆邊會穿牆，所以只推一點。
        public const float FirstPersonForward      = 0.32f;

        // ---------- 開箱彈出動畫（純本機視覺，不同步）----------
        public const float StallPopDuration        = 0.35f;  // 單台裝備彈出的時間
        public const float StallPopStagger         = 0.06f;  // 每台之間錯開多久
        public const float StallPopHeight          = 0.9f;   // 彈出時往上拋多高
        public const float StallPopOvershoot       = 1.12f;  // 縮放的回彈幅度

        // ---------- 輸送帶 ----------
        public const float ConveyorSpeed           = 1.6f;   // m/s
        // 帶面長度要放得進一格（1.5 公尺），所以比上一版短。
        // 之後如果把輸送帶改成 1x2 佔地，這裡就可以拉長到 2.8 左右。
        public const float ConveyorLength          = 1.35f;  // 帶面長度
        public const float ConveyorWidth           = 0.85f;  // 帶面寬度
        public const float ConveyorHeight          = 0.55f;  // 帶面高度
        public const float ConveyorCaptureHeight   = 0.75f;  // 帶面上方多高之內的物品會被推動
        // 帶面外圍的吸附：丟到輸送帶「附近」也會自己爬上去，不用剛好丟中
        public const float ConveyorAttractRadius   = 1.0f;   // 從帶面邊緣往外算
        public const float ConveyorAttractSpeed    = 2.5f;   // 被吸過去的速度（m/s）
        public const float ConveyorDeliverRadius   = 1.1f;   // 末端前方多遠內的機台算「接得到」
        // 物品躺在地上時大約比帶面低 0.35，所以下界要明顯低於它，
        // 不然「丟到旁邊會爬上輸送帶」會卡在浮點誤差上時靈時不靈
        public const float ConveyorPickupDrop      = -0.7f;   // 低於帶面多少之內還撿得到
        public const float ConveyorOnBeltDrop      = -0.2f;   // 判定「已經在帶面上」的下界
        // 相鄰格是 1.5 公尺、斜角是 2.12 公尺，門檻取 1.9 剛好只認正交相鄰
        public const float ConveyorLinkRadius        = 1.9f;   // 兩條輸送帶串在一起的判定
        public const float MachineConveyorLinkRadius = 1.9f;   // 機台旁有輸送帶就自動出貨

        // ---------- 交貨窗口 ----------
        public const float DeliveryCounterHeight   = 1.3f;
        public const float CustomerQueueDistance   = 1.6f;   // 排隊錨點離窗口的距離（下一批顧客用）

        // ---------- 擺攤計時與經濟 ----------
        public const float StallDurationSeconds    = 180f;   // 一場營業 3 分鐘（＝ LevelDurationSeconds）
        public const int   StallStartingCapital    = 0;      // 資本額起始值

        // ================= v6 羊駝村（以上既有數值一個都沒有改）=================

        // ---------- 會走動的羊毛 NPC ----------
        public const float NpcWanderSpeed        = 1.6f;   // 閒晃速度 m/s
        public const float NpcWanderRadius       = 18f;    // 以出生點為圓心的活動範圍
        public const float NpcWanderPauseMin     = 1.5f;   // 走到目標後站著發呆的最短時間
        public const float NpcWanderPauseMax     = 4f;
        public const float NpcFleeSpeed          = 4.5f;   // 被剃之後逃跑速度（比玩家的 5.0 慢一點，追得到）
        public const float NpcFleeSeconds        = 4f;
        public const int   NpcFleeceMax          = 3;      // 身上最多 3 份毛
        public const float NpcFleeceRegenSeconds = 8f;     // 每 8 秒長回 1 份
        public const float NpcShearRange         = 2.2f;   // 剃毛的互動距離
        public const float NpcArriveThreshold    = 0.6f;   // 走到多近算抵達目標點
        public const float NpcGravity            = 20f;    // 給 NetworkCharacterController 用

        // ---------- 惡搞系統 ----------
        //
        // 三個道具 = 對付「羊會跑」的三種解法：強攻（暈）／驅趕（推）／潛行（矇眼）。
        // 數值的原則：**失控要短**。笑點在爬起來的過程，不在被按在地上的那段。
        // 一秒上下就夠了 —— 再長就從惡搞變成霸凌，被害者會開始生氣而不是笑。

        /// <summary>口水：視覺干擾持續多久。比失控長很多，因為它不剝奪控制權。</summary>
        public const float SpitBlindSeconds     = 4f;

        /// <summary>
        /// 口水的冷卻。它是羊駝自帶的能力、不消耗任何東西，
        /// 沒有冷卻就會變成按住不放的機槍，被害者永遠看不見畫面 ——
        /// 那就不是惡搞而是單方面壓制了。
        /// </summary>
        public const float SpitCooldownSeconds  = 1.2f;

        /// <summary>
        /// 大蔥：擊退的初速與衰減時間。
        /// 9 推起來太軟、看不出被打到，加倍成 18 —— 現在是真的會被撞飛一段。
        /// 時間不動：要的是「一下子推很遠」而不是「被推著走很久」。
        /// </summary>
        public const float LeekKnockbackSpeed   = 18f;
        public const float LeekKnockbackSeconds = 0.35f;

        /// <summary>大蔥：被打到的人螢幕震一下的時間。</summary>
        public const float LeekShakeSeconds     = 0.25f;

        /// <summary>大蔥：揮一次的動作長度。要短，連打才順。</summary>
        public const float LeekSwingSeconds     = 0.28f;

        // ---- 口水（投射物）----
        //
        // 口水改成看得見的投射物之後，就變成一個**需要瞄準**的能力：
        // 噴出去要時間、會掉、會落空。原本「按了就中」太無腦了。

        /// <summary>
        /// 口水的初速。原本 13 噴出去軟趴趴、還沒到人就掉了，直接加到五倍。
        /// 65 比丟東西（16）快四倍，幾乎是直線 —— 現在它是「射」出去的。
        ///
        /// **這個數字快到會影響命中判定**：一個 tick 走 1.08 公尺，比命中半徑
        /// （0.55）大，用單點檢查會直接跨過目標。所以 SpitProjectile 的人身判定
        /// 是沿路徑掃過去的（SphereCast），不是在終點檢查一次。
        /// </summary>
        public const float SpitSpeed            = 65f;

        /// <summary>口水的重力。比一般物品輕，飛得比較直。</summary>
        public const float SpitGravity          = 7f;

        /// <summary>口水的命中半徑。做得寬鬆一點，不然瞄準會太難。</summary>
        public const float SpitHitRadius        = 0.55f;

        /// <summary>口水最多飛多久。超時就自己消失，不會留在場上。</summary>
        public const float SpitLifeSeconds      = 1.6f;

        // ---- 卡車（投擲物）----
        //
        // 卡車沒有自己的蓄力與初速數值 —— 它走的是所有可丟物共用的 Q 蓄力
        // （ThrowChargeTruck / ThrowSpeedMin / ThrowSpeed），只是重量特別大。
        // 它獨有的只有「落地會爆」這件事。

        /// <summary>卡車落地爆炸的波及半徑。</summary>
        public const float TruckBlastRadius     = 3.4f;

        /// <summary>爆炸動畫演多久，演完卡車就消失。</summary>
        public const float TruckBlastSeconds    = 0.45f;

        /// <summary>卡車：砸中之後失控多久。**刻意很短**，見上面的註解。</summary>
        public const float TruckStaggerSeconds  = 1.1f;

        /// <summary>卡車：砸中時附帶的擊退（比大蔥弱，主要的效果是倒地）。</summary>
        public const float TruckKnockbackSpeed  = 4f;

        /// <summary>惡搞道具的作用距離。比一般互動（2.5）遠一點，追著打才追得到。</summary>
        public const float PrankRange           = 3.2f;

        /// <summary>被打倒時，毛散落的半徑。</summary>
        public const float KnockdownWoolSpread  = 1.2f;

        /// <summary>螢幕震動的位移幅度（公尺）。只動位置不動旋轉，準心不會飄。</summary>
        public const float CameraShakeAmplitude = 0.09f;

        /// <summary>視覺干擾最濃的時候，畫面被蓋掉多少（0~1）。刻意不到全黑。</summary>
        public const float BlindMaxOpacity      = 0.82f;

        // ---------- 全隊共用背包 ----------
        // 每種顏色各自的上限（不是總量）。滿了就整筆拒收，毛留在動物身上。
        public const int   StashCapacityPerColor = 16;

        // ---------- 素材箱 ----------
        // 沒有「一箱幾份」的上限：開張時該色背包有多少就裝多少。
        // 這個值只是剩餘量條的滿格參考，不是容量限制。
        public const int   CrateFillBarReference = 12;

        // ---------- 織布機（批 B）----------
        // 織布機是一道**限時決策**：放下第一份毛就開始織，想做雙色的話
        // 第二份毛必須在單色織完之前送到。所以雙色秒數一定要大於單色，
        // 這是玩法前提，不是可調的美術數字。
        public const float WeaveSingleSeconds    = 4f;   // 單色衣服
        public const float WeaveDoubleSeconds    = 7f;   // 雙色衣服
        public const int   WeaveMaxWool          = 2;

        // 換目標時長時，剩餘時間的下限。
        // 沒有這個下限的話，「剛好在最後一瞬間塞進第二份毛」會變成瞬間完成，
        // 玩家看不到那件衣服是怎麼變成雙色的。
        public const float WeaveRetargetFloor    = 0.2f;

        // ---------- 顧客（批 B）----------
        public const int   CustomerMaxConcurrent   = 3;
        public const float CustomerPatienceSeconds = 45f;
        public const float CustomerIntervalSeconds = 12f;
        public const float CustomerWalkSpeed       = 2.2f;
        public const int   CustomerLeavePenalty    = 30;   // 等太久走掉的扣款

        /// <summary>
        /// 羊毛價格。衣服售價 = 主色 + 點綴色相加；單色衣服就只算一份。
        /// 集中在這裡，不要散到各處去。
        /// </summary>
        public static int WoolPrice(DyeColorType c) => c switch
        {
            DyeColorType.White  => 10,
            DyeColorType.Yellow => 15,
            DyeColorType.Green  => 20,
            DyeColorType.Blue   => 30,
            DyeColorType.Red    => 45,
            _                   => 10,
        };

        public static int StarsFor(int money)
        {
            if (money >= Star3Threshold) return 3;
            if (money >= Star2Threshold) return 2;
            if (money >= Star1Threshold) return 1;
            return 0;
        }
    }
}
