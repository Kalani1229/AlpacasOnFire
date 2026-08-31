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
        public const float CameraPitchMin      = -35f;
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
        public const float ThrowSpeed          = 9.0f;   // m/s
        public const float ThrowUpwardRatio    = 0.25f;  // 往上加的比例
        public const float ThrowGravity        = 20f;
        public const float CatchRadius         = 1.8f;   // 飛行物在這個半徑內可被接住
        public const float CatchMinClosingSpeed= 0.5f;   // 必須是「正在接近」才算飛向你
        public const float ItemFlightMaxTime   = 6f;     // 保險：飛太久就落地

        // ---------- 剃毛 ----------
        public const int   FleeceMax           = 3;      // 每隻羊駝身上最多 3 份毛
        public const float FleeceRegenSeconds  = 6f;     // 每 6 秒長回 1 份
        public const int   WoolPerShear        = 1;      // 每次剃毛掉 1 顆羊毛

        // ---------- 縫紉機 ----------
        public const int   SewingWoolRequired  = 3;      // 3 份羊毛 = 1 件衣服
        public const float SewingProcessSeconds= 6.0f;

        // ---------- 果汁機 ----------
        public const float JuicerProcessSeconds= 4.0f;   // 1 份原料 -> 1 罐染劑

        // ---------- 噴槍 ----------
        public const float SprayCapacity       = 100f;   // 一罐染劑裝滿噴槍
        public const float SprayDrainPerSecond = 25f;    // 滿容量可噴 4 秒
        public const float SprayPaintRequired  = 40f;    // 一件衣服要噴滿 40 單位（約 1.6 秒）
        public const float SprayRange          = 3.0f;
        public const float SprayConeDot        = 0.85f;  // 噴灑判定的夾角

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
        public const int   StallGridCells          = 6;      // 襯布是 6 x 6 格
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

        // ---------- 交貨窗口 ----------
        public const float DeliveryCounterHeight   = 1.3f;
        public const float CustomerQueueDistance   = 1.6f;   // 排隊錨點離窗口的距離（下一批顧客用）

        // ---------- 擺攤計時與經濟 ----------
        public const float StallDurationSeconds    = 180f;   // 一場營業 3 分鐘（＝ LevelDurationSeconds）
        public const int   StallStartingCapital    = 0;      // 資本額起始值

        public static int StarsFor(int money)
        {
            if (money >= Star3Threshold) return 3;
            if (money >= Star2Threshold) return 2;
            if (money >= Star1Threshold) return 1;
            return 0;
        }
    }
}
