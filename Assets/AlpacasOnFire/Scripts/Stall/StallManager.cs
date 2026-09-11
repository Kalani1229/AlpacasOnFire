using System;
using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
using AlpacasOnFire.Machines;
using AlpacasOnFire.Orders;
using AlpacasOnFire.Player;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 擺攤系統的權威與狀態機。放在場景的 [GameSystems] 上（跟 LevelDirector / OrderBoard 同一個
    /// NetworkObject），生命週期與場景一致，所以「跨場次的資本額」可以直接掛在這裡。
    ///
    /// 狀態流：Exploring -> Deploying -> Open -> Settling -> Exploring
    ///
    /// 擺放模型是 PlateUp! 式的網格：
    ///  - 開箱時**所有裝備一次彈出**到各自的格子上，沒有選單、沒有逐台放置
    ///  - 位置用整數格子座標同步，世界座標由 StallGrid 推算
    ///  - 重疊判定用格子佔用表，不用碰撞體互相檢查
    ///  - 地面檢測只在開箱時做一次，之後襯布就是一個平面
    ///
    /// 這個類別**不處理任何輸入**。所有互動都是新的 IInteractable 實作
    /// （SuitcaseItem / DeployableDevice / PlacementTarget / ToolRack / DeliveryCounter）打進來的，
    /// 既有的 PlayerInteractor、PlayerController、PlayerCarry 一行都不用改。
    /// </summary>
    public class StallManager : NetworkBehaviour
    {
        public static StallManager Instance { get; private set; }

        /// <summary>UI 用：狀態改變（新狀態）。</summary>
        public static event Action<StallState> OnStateChanged;
        /// <summary>UI 用：本場結算（營業額, 成交筆數, 錯過筆數, 結算後的資本額）。</summary>
        public static event Action<int, int, int, int> OnRoundSettled;
        /// <summary>UI 用：需要對玩家說一句話（放置失敗、開張條件不足……）。</summary>
        public static event Action<string> OnStallNotice;

        [Header("Loadout")]
        [Tooltip("勾起來就用 v6 羊駝村的裝備組（織布機／交貨窗口／輸送帶 x2，手提箱兼料倉）。" +
                 "不勾就是 Classic —— 舊場景不要動這一格。")]
        [SerializeField] private bool _villageLoadout = false;

        [Header("Suitcase")]
        [Tooltip("開場時要不要自動生成一個手提箱給隊伍。測試場景會開啟。")]
        [SerializeField] private bool _spawnSuitcaseOnStart = true;
        [SerializeField] private Vector3 _suitcaseSpawnPosition = new Vector3(0f, 0.4f, 0f);

        [Header("Mat Visual")]
        [Tooltip("襯布的視覺 prefab（非網路物件，每個用戶端各自生成）。留空會用程式生成。")]
        [SerializeField] private GameObject _matVisualPrefab;

        // ---------------- 網路狀態 ----------------

        /// <summary>
        /// 狀態用 int 存而不是直接存列舉 —— 沿用專案裡既有的 PatternRaw / PaintColorRaw 寫法，
        /// 這是已經驗證過能正常同步的形式。
        /// </summary>
        [Networked] public int StateRaw { get; set; }

        public StallState State => (StallState)StateRaw;

        [Networked] public NetworkBool MatDeployed { get; set; }
        [Networked] public Vector3 MatCenter { get; set; }
        [Networked] public float MatYaw { get; set; }

        /// <summary>目前這個攤位是哪一個手提箱開的（收攤時要把佈局寫回它）。</summary>
        [Networked] public NetworkId ActiveSuitcaseId { get; set; }

        /// <summary>目前那顆開張鈴。敲掉之後會失效，回到佈置模式時再生一顆。</summary>
        [Networked] public NetworkId BellId { get; set; }

        /// <summary>跨場次累積的資本額（先存在記憶體，不做存檔）。</summary>
        [Networked] public int Capital { get; set; }
        /// <summary>已經完成幾場營業。</summary>
        [Networked] public int RoundsCompleted { get; set; }

        /// <summary>本場的成交筆數與錯過（超時）筆數。</summary>
        [Networked] public int RoundDeliveries { get; set; }
        [Networked] public int RoundMissed { get; set; }
        [Networked] public int RoundRevenue { get; set; }

        private StallMatVisual _matVisual;
        private StallState _lastRenderedState = (StallState)255;
        private bool _pendingSuitcaseSpawn;

        private readonly StallSlotRecord[] _layoutBuffer = new StallSlotRecord[StallCatalog.MaxSlots];

        // ---------------- 推導出來的狀態 ----------------

        /// <summary>佈置模式：襯布已展開，而且不在營業／結算中。</summary>
        public bool IsArrangeMode => MatDeployed && State != StallState.Open && State != StallState.Settling;

        /// <summary>營業模式：機台照常運作，但不能再移動。</summary>
        public bool IsBusinessMode => State == StallState.Open;

        public float MatSize => GameTuning.StallMatSize;

        public SuitcaseItem ActiveSuitcase
        {
            get
            {
                if (!ActiveSuitcaseId.IsValid || Runner == null) return null;
                if (!Runner.TryFindObject(ActiveSuitcaseId, out var obj) || obj == null) return null;
                if (!obj.IsValid) return null;
                return obj.GetComponent<SuitcaseItem>();
            }
        }

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            Instance = this;

            // **每個場景都明確設定一次**，不要只在 village 時才設。
            // StallCatalog.Active 是 static，如果 Unity 關掉了 Domain Reload，
            // 上一次進 Village 的設定會殘留到下一次進 Stall_Test。
            StallCatalog.Active = _villageLoadout ? StallLoadout.Village : StallLoadout.Classic;

            StallUIRoot.EnsureExists();

            // 襯布放不下全部裝備是設計錯誤，不是執行期狀況 —— 一開場就吼出來
            if (!StallCatalog.MatFitsAllDevices(out int required, out int available))
                Debug.LogError($"[擺攤] 襯布放不下全部裝備：需要 {required} 格，只有 {available} 格。" +
                               "請調大 GameTuning.StallGridCells 或縮小裝備佔地。");

            if (HasStateAuthority)
            {
                StateRaw = (int)StallState.Exploring;
                MatDeployed = false;
                Capital = GameTuning.StallStartingCapital;
                RoundsCompleted = 0;

                // 手提箱刻意不在 Spawned() 裡生成，改成第一個 tick 才生。
                // 場景物件的 Spawned() 發生在 Runner 還在註冊場景物件的階段，
                // 這時候呼叫 Runner.Spawn() 是不保險的做法。
                _pendingSuitcaseSpawn = _spawnSuitcaseOnStart;
            }

            OrderBoard.OnOrderExpired += HandleOrderExpired;
            OrderBoard.OnDeliveryResult += HandleDeliveryResult;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            OrderBoard.OnOrderExpired -= HandleOrderExpired;
            OrderBoard.OnDeliveryResult -= HandleDeliveryResult;

            if (_matVisual != null) Destroy(_matVisual.gameObject);
            if (Instance == this) Instance = null;
        }

        /// <summary>統計只在狀態權威上累加，避免每個用戶端各加一次。</summary>
        private void HandleOrderExpired(string desc)
        {
            if (!HasStateAuthority || State != StallState.Open) return;
            RoundMissed++;
        }

        private void HandleDeliveryResult(bool success, string desc)
        {
            if (!HasStateAuthority || State != StallState.Open) return;
            if (success) RoundDeliveries++;
        }

        // ---------------- 模擬 ----------------

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            if (_pendingSuitcaseSpawn)
            {
                _pendingSuitcaseSpawn = false;
                SpawnSuitcase(_suitcaseSpawnPosition);
            }

            EnsureBell();
            SyncCrates();

            if (State != StallState.Open) return;

            var director = LevelDirector.Instance;
            if (director == null) return;

            // 計時交給既有的 LevelDirector（它已經是 [Networked] TickTimer），
            // 這裡只負責偵測「跑完了」並推進狀態機。
            if (director.Running) return;

            Settle();
        }

        // ---------------- 手提箱 ----------------

        public SuitcaseItem SpawnSuitcase(Vector3 position)
        {
            if (!HasStateAuthority) return null;

            var item = ItemFactory.Spawn(Runner, ItemKind.Suitcase, default, position);
            if (item == null)
                Debug.LogError("[擺攤] 生成手提箱失敗：GameCatalog 裡沒有 ItemKind.Suitcase 的 prefab。" +
                               "請執行選單「羊駝很忙 / 1. 建置佔位資產」。");
            return item as SuitcaseItem;
        }

        // ---------------- 開張鈴 ----------------

        public ServiceBell Bell
        {
            get
            {
                if (!BellId.IsValid || Runner == null) return null;
                if (!Runner.TryFindObject(BellId, out var obj) || obj == null) return null;
                // 已經 Despawn 但還沒被清掉的物件，讀它的 [Networked] 屬性會丟例外
                if (!obj.IsValid) return null;
                return obj.GetComponent<ServiceBell>();
            }
        }

        /// <summary>
        /// 讓鈴鐺的存在與否跟著模式走：
        ///  - 在佈置模式而且沒有鈴 -> 生一顆（含結算完回到佈置模式的那次，才能再開一場）
        ///  - 離開佈置模式 -> 收掉（敲過的那顆會自己縮完消失，不用管）
        /// 每個 tick 檢查一次，狀態機怎麼跳都不會漏掉。
        /// </summary>
        private void EnsureBell()
        {
            if (!HasStateAuthority) return;

            var bell = Bell;

            if (MatDeployed && IsArrangeMode)
            {
                if (bell != null) return;

                var prefab = StallCatalog.DevicePrefab(LevelElementType.ServiceBell);
                if (prefab == null) return;   // DevicePrefab 已經印過錯誤了

                var obj = Runner.Spawn(prefab,
                                       StallGeometry.BellRestPosition(MatCenter, MatYaw),
                                       Quaternion.Euler(0f, MatYaw, 0f));
                if (obj != null) BellId = obj.Id;
                return;
            }

            // 不在佈置模式：沒敲過的鈴要收掉（敲過的正在縮，讓它自己走完）
            if (bell != null && !bell.Rung)
            {
                Runner.Despawn(bell.Object);
                BellId = default;
            }
        }

        // ---------------- 素材箱 ----------------

        /// <summary>
        /// 讓場上的素材箱跟手提箱的選色一致。
        ///
        /// 這就是「選了哪幾種顏色，場上就直接冒出那幾個顏色的箱子」的實作：
        ///  - 選了某色但場上沒有那個箱子 -> 生一個到最靠近背緣的空格
        ///  - 場上有箱子但那個顏色被取消了 -> 把剩料退回背包再收掉
        ///
        /// 每個 tick 檢查一次，玩家在面板上點來點去都跟得上。
        /// **營業中不動** —— 那時候顏色已經鎖定，箱子也不該憑空增減。
        /// </summary>
        private void SyncCrates()
        {
            if (!HasStateAuthority) return;
            if (!StallCatalog.Active.SuitcaseIsStash) return;
            if (!MatDeployed || !IsArrangeMode) return;

            var suitcase = ActiveSuitcase;
            if (suitcase == null) return;

            // ---- 多出來的箱子（顏色被取消了）----
            for (int i = MaterialCrate.All.Count - 1; i >= 0; i--)
            {
                var crate = MaterialCrate.All[i];
                if (crate == null || crate.Object == null || !crate.Object.IsValid) continue;
                if (!IsStallOwned(crate)) continue;
                if (suitcase.IsSelected(crate.Color)) continue;

                int returned = crate.UnloadToStash();
                if (returned > 0)
                    Debug.Log($"[v6] 取消 {PlaceholderPalette.DyeName(crate.Color)} -> 退回 {returned} 份到背包。");

                Runner.Despawn(crate.Object);
            }

            // ---- 缺少的箱子（新選了顏色）----
            for (int slot = 0; slot < SuitcaseItem.ColorSlots; slot++)
            {
                if (!suitcase.HasColor(slot)) continue;
                var colour = suitcase.ColorAt(slot);

                // 場上找得到就不補。**拿在某個玩家手上的也算找得到** ——
                // 見 IsCratePending 的註解，漏掉這個條件就會分裂出第二個箱子。
                if (FindCrate(colour) != null) continue;
                if (IsCratePending(colour)) continue;

                SpawnCrateFor(colour);
            }
        }

        /// <summary>
        /// 有沒有人正舉著這個顏色的素材箱等著放下。
        ///
        /// **這是「箱子會分裂」那個 bug 的修正點。** 拿起裝備的流程是
        /// 「Despawn 場上那一個 + 進入放置預覽」，所以舉在手上的期間，
        /// 箱子在場上是**不存在**的。SyncCrates 每個 tick 都跑，下一個 tick 就會
        /// 判定「選了這個顏色卻沒有箱子」而補生一個；等玩家把手上那個放下，
        /// 同色箱子就變成兩個。兩個都是全新的（Remaining = 0、Loaded = false），
        /// 所以看起來就是「多出一個拿不了東西的箱子」。
        ///
        /// 修法是把「在某人手上」也算成存在。放下、取消、甚至玩家中途斷線，
        /// 都會讓 HasPending 變回 false，該補的下一個 tick 自然會補回來。
        /// </summary>
        private static bool IsCratePending(DyeColorType colour)
        {
            for (int i = 0; i < PlayerStallAgent.All.Count; i++)
            {
                var agent = PlayerStallAgent.All[i];
                if (agent == null || agent.Object == null || !agent.Object.IsValid) continue;
                if (!agent.HasPending) continue;
                if (agent.PendingType != LevelElementType.MaterialCrate) continue;
                if (agent.PendingVariant == (int)colour) return true;
            }
            return false;
        }

        /// <summary>場上有沒有任何一個素材箱。</summary>
        public MaterialCrate FindAnyCrate()
        {
            for (int i = 0; i < MaterialCrate.All.Count; i++)
            {
                var crate = MaterialCrate.All[i];
                if (crate == null || crate.Object == null || !crate.Object.IsValid) continue;
                if (IsStallOwned(crate)) return crate;
            }
            return null;
        }

        /// <summary>場上有沒有這個顏色的素材箱。</summary>
        public MaterialCrate FindCrate(DyeColorType colour)
        {
            for (int i = 0; i < MaterialCrate.All.Count; i++)
            {
                var crate = MaterialCrate.All[i];
                if (crate == null || crate.Object == null || !crate.Object.IsValid) continue;
                if (!IsStallOwned(crate)) continue;
                if (crate.Color == colour) return crate;
            }
            return null;
        }

        private static bool IsStallOwned(MaterialCrate crate)
        {
            var dev = crate.GetComponentInChildren<DeployableDevice>(true);
            return dev != null && dev.StallOwned;
        }

        /// <summary>
        /// 生一個素材箱。位置挑「最靠近背緣的空格」——
        /// TryFindFree 是從 z=0 開始掃的，而 z=0 就是玩家進場那一側，
        /// 剛好符合「原料在後場」的空間邏輯。玩家之後可以自己搬到織布機旁邊。
        /// </summary>
        private void SpawnCrateFor(DyeColorType colour)
        {
            StallGrid.RotatedFootprint(StallCatalog.Footprint(LevelElementType.MaterialCrate),
                                       (int)StallFacing.North, out int w, out int d);

            if (!BuildOccupancy().TryFindFree(w, d, out int cx, out int cz))
            {
                RPC_Notice("襯布上沒有空格可以放素材箱了");
                return;
            }

            SpawnDevice(LevelElementType.MaterialCrate, cx, cz, (int)StallFacing.North, 0, (int)colour);
        }

        /// <summary>開張時每個箱子從背包裝滿。</summary>
        private int LoadAllCrates()
        {
            int total = 0;
            for (int i = 0; i < MaterialCrate.All.Count; i++)
            {
                var crate = MaterialCrate.All[i];
                if (crate == null || !IsStallOwned(crate)) continue;
                total += crate.LoadFromStash();
            }
            return total;
        }

        /// <summary>收攤時每個箱子把剩料退回背包。</summary>
        private int UnloadAllCrates()
        {
            int total = 0;
            for (int i = 0; i < MaterialCrate.All.Count; i++)
            {
                var crate = MaterialCrate.All[i];
                if (crate == null || !IsStallOwned(crate)) continue;
                total += crate.UnloadToStash();
            }
            return total;
        }

        // ---------------- 開箱 ----------------

        /// <summary>開箱前的檢查：前方那塊地夠不夠平、上方夠不夠淨空。</summary>
        public PlacementResult CheckDeploySpot(Vector3 playerPos, float playerYaw,
                                               out Vector3 center, out float yaw)
        {
            StallGeometry.PlannedMat(playerPos, playerYaw, out center, out yaw);
            var result = StallGeometry.CheckDeployArea(center, yaw, GameTuning.StallMatSize,
                                                       out float groundY, out _);
            if (result == PlacementResult.Ok) center.y = groundY;
            return result;
        }

        /// <summary>
        /// 開箱：襯布展開，然後**所有裝備一次彈出**到各自的格子上。
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public bool TryDeployMat(Vector3 playerPos, float playerYaw, SuitcaseItem suitcase)
        {
            if (!HasStateAuthority) return false;
            if (MatDeployed) return false;

            StallGeometry.PlannedMat(playerPos, playerYaw, out var center, out float yaw);
            var reason = StallGeometry.CheckDeployArea(center, yaw, GameTuning.StallMatSize,
                                                       out float groundY, out string detail);

            Debug.Log($"[擺攤] 開箱地面檢查：{reason}（{detail}）於 {center}");

            if (reason != PlacementResult.Ok)
            {
                RPC_Notice($"這裡不能擺攤：{StallGeometry.Describe(reason)}");
                GameAudio.PlayAt(SfxId.PlaceRejected, playerPos);
                return false;
            }

            center.y = groundY;
            MatCenter = center;
            MatYaw = yaw;
            MatDeployed = true;
            ActiveSuitcaseId = suitcase != null && suitcase.Object != null ? suitcase.Object.Id : default;
            SetState(StallState.Deploying);
            GameAudio.PlayAt(SfxId.StallOpen, center);

            PopOutAllDevices(suitcase);
            return true;
        }

        /// <summary>
        /// 依佈局把所有裝備一次生出來。
        /// 佈局有衝突（例如之後改了佔地大小）時會自動找一個空格塞進去，
        /// 不做「裝不下就留在箱子裡」的處理 —— 襯布保證放得下全部裝備。
        /// </summary>
        private void PopOutAllDevices(SuitcaseItem suitcase)
        {
            if (!HasStateAuthority) return;

            int count = suitcase != null
                ? suitcase.ReadLayout(_layoutBuffer)
                : CopyDefaultLayout();

            var occupancy = new StallGrid.Occupancy();
            int placed = 0;

            for (int i = 0; i < count; i++)
            {
                var rec = _layoutBuffer[i];
                if (!rec.IsValid) continue;

                var footprint = StallCatalog.Footprint(rec.DeviceType);
                StallGrid.RotatedFootprint(footprint, rec.Facing, out int w, out int d);

                int cx = rec.CellX, cz = rec.CellZ;
                if (occupancy.Check(cx, cz, w, d) != PlacementResult.Ok)
                {
                    if (!occupancy.TryFindFree(w, d, out cx, out cz))
                    {
                        Debug.LogError($"[擺攤] {rec.DeviceType} 找不到空格可以放 —— 襯布格數不足。");
                        continue;
                    }
                    Debug.LogWarning($"[擺攤] {rec} 的格子被佔住了，改放到 ({cx},{cz})。");
                }

                if (!SpawnDevice(rec.DeviceType, cx, cz, rec.Facing, placed, rec.Variant)) continue;

                occupancy.OccupyFootprint(cx, cz, w, d);
                placed++;
            }

            Debug.Log($"[擺攤] 開箱彈出 {placed}/{count} 台裝備。");
        }

        private int CopyDefaultLayout()
        {
            int n = Mathf.Min(StallCatalog.DefaultLayout.Length, _layoutBuffer.Length);
            for (int i = 0; i < n; i++) _layoutBuffer[i] = StallCatalog.DefaultLayout[i];
            return n;
        }

        /// <summary>生成一台裝備到指定格子。只在 StateAuthority 呼叫。</summary>
        private bool SpawnDevice(LevelElementType type, int cellX, int cellZ, int facing, int popOrder,
                                 int variant = 0)
        {
            var prefab = StallCatalog.DevicePrefab(type);
            if (prefab == null)
            {
                Debug.LogError($"[擺攤] GameCatalog 裡沒有 {type} 的 prefab，或它上面沒有 NetworkObject。");
                return false;
            }

            var position = StallGrid.CellToWorld(cellX, cellZ, MatCenter, MatYaw);
            var rotation = StallGrid.FacingToRotation(facing, MatYaw);

            var obj = Runner.Spawn(prefab, position, rotation, null, (r, o) =>
            {
                var dev = o.GetComponentInChildren<DeployableDevice>(true);
                if (dev != null) dev.MarkDeployed(type, cellX, cellZ, facing, popOrder, variant);

                // 素材箱的顏色是型別專屬參數，走 variant 帶進來
                var crate = o.GetComponent<Machines.MaterialCrate>();
                if (crate != null) crate.SetColor((DyeColorType)variant);
            });

            if (obj == null)
            {
                Debug.LogError($"[擺攤] Runner.Spawn({type}) 回傳 null。" +
                               "多半是 prefab 沒有被登錄在 Fusion 的 prefab 表裡 —— " +
                               "請先執行「羊駝很忙 / 1. 建置佔位資產」，再跑選單 " +
                               "Tools > Fusion > Rebuild Prefab Table。");
                return false;
            }
            return true;
        }

        // ---------------- 收攤 ----------------

        /// <summary>
        /// 收攤：**先把目前的佈局寫回手提箱**，再收走所有裝備與襯布。
        /// 玩家調好的動線就是這樣跟著箱子走的。
        /// </summary>
        public void CollectStall(PlayerController requester = null, SuitcaseItem suitcase = null)
        {
            if (!HasStateAuthority || !MatDeployed) return;

            suitcase ??= ActiveSuitcase ?? FindSuitcaseNearMat();

            // v6：素材箱剩下的毛退回背包。**一定要排在 Despawn 之前**，
            // 不然玩家的毛會憑空消失，而且他們會不敢多裝。
            int returnedWool = 0;
            if (StallCatalog.Active.SuitcaseIsStash)
            {
                returnedWool = UnloadAllCrates();
                suitcase?.Unlock();
            }

            int saved = SaveLayoutTo(suitcase);

            // **先把箱子交回手上，再開始清場。**
            // 清場的掃描是「襯布範圍內、沒被拿著的東西」，箱子一旦在手上就絕對不會被掃到；
            // 反過來（先清場再交還）只要有任何一條排除條件失效，箱子就會被 Despawn，
            // 然後 HeldId 指向一個不存在的物件 —— 畫面上沒東西、UI 卻說手上有東西。
            bool returned = false;
            if (suitcase != null && requester != null)
            {
                returned = suitcase.ReturnToHands(requester);
                if (!returned)
                {
                    // 手不空：放在收攤的人腳邊，不要留在襯布邊緣 ——
                    // 襯布收掉之後那個位置沒有任何視覺參考，等於把箱子丟掉
                    suitcase.DetachToGround(requester.transform.position
                                            + requester.transform.forward * 0.8f);
                    RPC_Notice("手上有東西，手提箱放在你腳邊");
                }
            }

            int devices = DespawnDeployedDevices();
            int loose = DespawnLooseItemsOnMat(suitcase);

            DespawnBell();

            MatDeployed = false;
            ActiveSuitcaseId = default;
            SetState(StallState.Exploring);
            GameAudio.PlayAt(SfxId.StallClose, MatCenter);

            Debug.Log($"[擺攤] 收攤完成：記住 {saved} 台的排法、收回裝備 {devices} 台、" +
                      $"退回羊毛 {returnedWool} 份、襯布上的物品 {loose} 個、" +
                      $"手提箱{(returned ? "回到手上" : "留在地上")}。");
        }

        /// <summary>把場上所有裝備的格子與朝向寫回手提箱。</summary>
        private int SaveLayoutTo(SuitcaseItem suitcase)
        {
            int count = 0;
            for (int i = 0; i < DeployableDevice.All.Count && count < _layoutBuffer.Length; i++)
            {
                var d = DeployableDevice.All[i];
                if (d == null || !d.StallOwned) continue;
                _layoutBuffer[count++] = d.ToRecord();
            }

            if (suitcase == null)
            {
                Debug.LogWarning("[擺攤] 收攤時找不到手提箱，這次的佈局沒有被記住。");
                return 0;
            }

            suitcase.WriteLayout(_layoutBuffer, count);
            return count;
        }

        /// <summary>收攤時把鈴鐺一起收掉（敲過的那顆正在縮，讓它自己走完就好）。</summary>
        private void DespawnBell()
        {
            var bell = Bell;
            if (bell != null && !bell.Rung && bell.Object != null)
                Runner.Despawn(bell.Object);
            BellId = default;
        }

        private SuitcaseItem FindSuitcaseNearMat()
        {
            float limit = GameTuning.StallMatSize * 0.5f
                        + GameTuning.StallSuitcaseBackOffset + GameTuning.StallCollectRadius;

            for (int i = 0; i < CarriableItem.All.Count; i++)
            {
                if (CarriableItem.All[i] is not SuitcaseItem suitcase) continue;
                if (suitcase.IsHeld) continue;

                var local = StallGeometry.WorldToMat(suitcase.transform.position, MatCenter, MatYaw);
                if (Mathf.Abs(local.x) > limit || Mathf.Abs(local.z) > limit) continue;
                return suitcase;
            }
            return null;
        }

        private int DespawnDeployedDevices()
        {
            int n = 0;
            // 反向走訪：Despawn 會讓 DeployableDevice.Despawned 把自己從 All 移除
            for (int i = DeployableDevice.All.Count - 1; i >= 0; i--)
            {
                var d = DeployableDevice.All[i];
                if (d == null || !d.StallOwned) continue;
                var obj = d.OwnerObject;
                if (obj == null) continue;
                Runner.Despawn(obj);
                n++;
            }
            return n;
        }

        private int DespawnLooseItemsOnMat(SuitcaseItem keep = null)
        {
            int n = 0;
            float limit = GameTuning.StallMatSize * 0.5f + GameTuning.StallCollectRadius;

            for (int i = CarriableItem.All.Count - 1; i >= 0; i--)
            {
                var item = CarriableItem.All[i];
                if (item == null || item.Object == null) continue;
                if (item.IsHeld) continue;                     // 拿在手上的不動
                if (item is SuitcaseItem) continue;            // 手提箱一律留著（用型別判斷，不靠 Kind 設對）
                if (keep != null && item == keep) continue;

                var local = StallGeometry.WorldToMat(item.transform.position, MatCenter, MatYaw);
                if (Mathf.Abs(local.x) > limit || Mathf.Abs(local.z) > limit) continue;
                if (Mathf.Abs(local.y) > 3f) continue;

                Runner.Despawn(item.Object);
                n++;
            }
            return n;
        }

        // ---------------- 格子放置 ----------------

        /// <summary>
        /// 現在場上的格子佔用表。ignore 用來排除「正在被拿起來的那一台」。
        /// **這就是重疊判定的唯一依據** —— 不用碰撞體互相檢查。
        /// </summary>
        public StallGrid.Occupancy BuildOccupancy(DeployableDevice ignore = null)
        {
            var occupancy = new StallGrid.Occupancy();

            for (int i = 0; i < DeployableDevice.All.Count; i++)
            {
                var d = DeployableDevice.All[i];
                if (d == null || !d.StallOwned) continue;
                if (ReferenceEquals(d, ignore)) continue;

                StallGrid.RotatedFootprint(d.Footprint, d.Facing, out int w, out int h);
                occupancy.OccupyFootprint(d.CellX, d.CellZ, w, h);
            }
            return occupancy;
        }

        /// <summary>
        /// 驗證一格放不放得下。**本機預覽與狀態權威的實際放置都呼叫這一支**，
        /// 而且只吃整數，所以玩家看到高亮的那一格就是真的會放進去的那一格。
        /// </summary>
        public PlacementResult ValidateCell(LevelElementType type, int cellX, int cellZ, int facing)
        {
            if (!MatDeployed || !IsArrangeMode) return PlacementResult.NotDeploying;

            StallGrid.RotatedFootprint(StallCatalog.Footprint(type), facing, out int w, out int d);
            return BuildOccupancy().Check(cellX, cellZ, w, d);
        }

        /// <summary>把一台裝備放進指定格子。只在 StateAuthority 呼叫。</summary>
        public bool TryPlaceDevice(LevelElementType type, int cellX, int cellZ, int facing,
                                   out PlacementResult reason, int variant = 0)
        {
            reason = ValidateCell(type, cellX, cellZ, facing);
            if (!HasStateAuthority) return false;

            if (reason != PlacementResult.Ok)
            {
                Debug.Log($"[擺攤] 放置 {type} 到 ({cellX},{cellZ}) 被擋下：{reason}");
                GameAudio.PlayAt(SfxId.PlaceRejected, StallGrid.CellToWorld(cellX, cellZ, MatCenter, MatYaw));
                return false;
            }

            if (!SpawnDevice(type, cellX, cellZ, facing, 0, variant))
            {
                reason = PlacementResult.NothingPending;
                return false;
            }

            GameAudio.PlayAt(SfxId.DevicePlace, StallGrid.CellToWorld(cellX, cellZ, MatCenter, MatYaw));
            return true;
        }

        // ---------------- 開張 / 結算 ----------------

        /// <summary>可不可以開張。沒有交貨窗口就賣不出東西，所以擋下來並說明原因。</summary>
        public bool CanOpenForBusiness(out string reason)
        {
            reason = null;
            if (!MatDeployed) { reason = "還沒擺攤"; return false; }
            if (State != StallState.Deploying && State != StallState.Exploring)
            {
                reason = State == StallState.Open ? "已經在營業中" : "結算還沒結束";
                return false;
            }
            // 缺哪一台由 loadout 決定 —— Classic 要縫紉機、Village 要織布機
            foreach (var required in StallCatalog.Active.RequiredToOpen)
            {
                if (CountDeployed(required) > 0) continue;
                reason = $"沒有{StallCatalog.DisplayName(required)}，" +
                         (required == LevelElementType.DeliveryCounter
                             ? "客人沒地方付錢" : "做不出衣服");
                return false;
            }

            // v6：手提箱兼料倉，沒選材料就開張等於空手做生意
            if (StallCatalog.Active.SuitcaseIsStash)
            {
                var suitcase = ActiveSuitcase;
                if (suitcase == null)
                {
                    reason = "找不到手提箱";
                    return false;
                }
                if (suitcase.SelectedCount == 0)
                {
                    reason = "還沒選材料 —— 對手提箱按右鍵選顏色";
                    return false;
                }
                if (suitcase.SelectedStashTotal() == 0)
                {
                    reason = "選的顏色背包裡都沒有存量";
                    return false;
                }
                if (FindAnyCrate() == null)
                {
                    reason = "素材箱還沒生出來，等一下";
                    return false;
                }
            }

            return true;
        }

        public int CountDeployed(LevelElementType type)
        {
            int n = 0;
            for (int i = 0; i < DeployableDevice.All.Count; i++)
            {
                var d = DeployableDevice.All[i];
                if (d != null && d.StallOwned && d.DeviceType == type) n++;
            }
            return n;
        }

        public int DeployedCount()
        {
            int n = 0;
            for (int i = 0; i < DeployableDevice.All.Count; i++)
                if (DeployableDevice.All[i] != null && DeployableDevice.All[i].StallOwned) n++;
            return n;
        }

        /// <summary>開張。只在 StateAuthority 呼叫（房主判定在 RPC 端做）。</summary>
        public void OpenForBusiness()
        {
            if (!HasStateAuthority) return;
            if (!CanOpenForBusiness(out string reason))
            {
                RPC_Notice(reason);
                return;
            }

            RoundDeliveries = 0;
            RoundMissed = 0;
            RoundRevenue = 0;

            // v6：每個素材箱從背包把自己那個顏色的存量整批裝滿，此後鎖定
            if (StallCatalog.Active.SuitcaseIsStash)
            {
                ActiveSuitcase?.Lock();
                int loaded = LoadAllCrates();
                Debug.Log($"[v6] 開張：素材箱共裝載 {loaded} 份羊毛。");
            }

            SetState(StallState.Open);
            OrderBoard.Instance?.ResetSpawnSchedule();
            LevelDirector.Instance?.BeginStallRound(GameTuning.StallDurationSeconds);
            GameAudio.PlayAt(SfxId.BusinessOpen, MatCenter);
        }

        private void Settle()
        {
            if (!HasStateAuthority) return;

            var director = LevelDirector.Instance;
            int revenue = director != null ? director.Money : 0;

            RoundRevenue = revenue;
            Capital += revenue;
            RoundsCompleted++;

            OrderBoard.Instance?.ClearAllOrders();
            SetState(StallState.Settling);
            GameAudio.PlayAt(SfxId.BusinessClose, MatCenter);
            RPC_RoundSettled(revenue, RoundDeliveries, RoundMissed, Capital);
        }

        /// <summary>結算畫面關掉之後回到 Exploring（攤位還在地上，可以繼續搬或收攤）。</summary>
        public void DismissSettlement()
        {
            if (!HasStateAuthority) return;
            if (State != StallState.Settling) return;
            SetState(StallState.Exploring);
        }

        private void SetState(StallState next)
        {
            if (!HasStateAuthority || State == next) return;
            StateRaw = (int)next;
        }

        // ---------------- RPC ----------------

        // 註：以前有一支 RPC_RequestOpenForBusiness，是給 HUD 按鈕用的。
        // 開張改成敲鈴之後，ServiceBell.Interact() 本來就只在狀態權威上跑，
        // 直接呼叫 OpenForBusiness() 就好，那支 RPC 沒有人再用，已經移除。

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestDismissSettlement()
        {
            DismissSettlement();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Notice(string message)
        {
            OnStallNotice?.Invoke(message);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_RoundSettled(int revenue, int deliveries, int missed, int capital)
        {
            OnRoundSettled?.Invoke(revenue, deliveries, missed, capital);
        }

        /// <summary>給本機呼叫的提示（不需要走網路的那種）。</summary>
        public static void LocalNotice(string message) => OnStallNotice?.Invoke(message);

        // ---------------- 表現層 ----------------

        public override void Render()
        {
            if (_lastRenderedState != State)
            {
                _lastRenderedState = State;
                OnStateChanged?.Invoke(State);
            }

            if (MatDeployed)
            {
                if (_matVisual == null) _matVisual = StallMatVisual.Create(_matVisualPrefab);
                _matVisual.Apply(MatCenter, MatYaw, IsArrangeMode);
            }
            else if (_matVisual != null)
            {
                _matVisual.Hide();
            }
        }
    }
}
