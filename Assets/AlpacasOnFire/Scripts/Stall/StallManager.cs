using System;
using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
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
    /// 這個類別**不處理任何輸入**。所有互動都是新的 IInteractable 實作
    /// （SuitcaseItem / DeployableDevice / PlacementTarget / DeliveryCounter）打進來的，
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

        [Header("Suitcase")]
        [Tooltip("開場時要不要自動生成一個手提箱給隊伍。測試場景會開啟。")]
        [SerializeField] private bool _spawnSuitcaseOnStart = true;
        [SerializeField] private Vector3 _suitcaseSpawnPosition = new Vector3(0f, 0.4f, 0f);

        [Header("Mat Visual")]
        [Tooltip("襯布的視覺 prefab（非網路物件，每個用戶端各自生成）。留空會用程式生成一個方塊。")]
        [SerializeField] private GameObject _matVisualPrefab;

        // ---------------- 網路狀態 ----------------

        /// <summary>
        /// 狀態用 int 存而不是直接存列舉 —— 沿用專案裡既有的 PatternRaw / PaintColorRaw 寫法，
        /// 這是已經驗證過能正常同步的形式，不要為了好看改成列舉。
        /// </summary>
        [Networked] public int StateRaw { get; set; }

        public StallState State => (StallState)StateRaw;

        [Networked] public NetworkBool MatDeployed { get; set; }
        [Networked] public Vector3 MatCenter { get; set; }
        [Networked] public float MatYaw { get; set; }

        /// <summary>跨場次累積的資本額（先存在記憶體，本批不做存檔）。</summary>
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

        // ---------------- 推導出來的狀態 ----------------

        /// <summary>佈置模式：襯布已展開，而且不在營業／結算中。</summary>
        public bool IsArrangeMode => MatDeployed && State != StallState.Open && State != StallState.Settling;

        /// <summary>營業模式：機台照常運作，但不能再移動。</summary>
        public bool IsBusinessMode => State == StallState.Open;

        public float MatSize => GameTuning.StallMatSize;

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            Instance = this;
            StallUIRoot.EnsureExists();

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

        // ---------------- 開箱 / 收攤 ----------------

        /// <summary>開箱前的檢查：前方有沒有一塊夠平的空地。</summary>
        public PlacementResult CheckDeploySpot(Vector3 playerPos, float playerYaw,
                                               out Vector3 center, out float yaw)
        {
            StallGeometry.PlannedMat(playerPos, playerYaw, out center, out yaw);
            var result = StallGeometry.CheckGround(center, yaw, GameTuning.StallMatSize, out float groundY);
            if (result == PlacementResult.Ok) center.y = groundY;
            return result;
        }

        /// <summary>展開襯布。只在 StateAuthority 呼叫。</summary>
        public bool TryDeployMat(Vector3 playerPos, float playerYaw, out PlacementResult reason)
        {
            reason = PlacementResult.Ok;
            if (!HasStateAuthority) return false;
            if (MatDeployed) return false;

            reason = CheckDeploySpot(playerPos, playerYaw, out var center, out float yaw);
            if (reason != PlacementResult.Ok)
            {
                RPC_Notice($"這裡沒辦法擺攤：{StallGeometry.Describe(reason)}");
                GameAudio.PlayAt(SfxId.PlaceRejected, playerPos);
                return false;
            }

            MatCenter = center;
            MatYaw = yaw;
            MatDeployed = true;
            SetState(StallState.Deploying);
            GameAudio.PlayAt(SfxId.StallOpen, center);
            return true;
        }

        /// <summary>
        /// 收攤：所有擺出來的機台收回箱中、襯布收起、箱子回到收攤那個人手上。
        /// 襯布上沒被拿在手上的物品也一併收走（等於整攤打包）。
        /// </summary>
        public void CollectStall(PlayerController requester = null)
        {
            if (!HasStateAuthority || !MatDeployed) return;

            var suitcase = FindSuitcaseOnMat();

            int devices = DespawnDeployedDevices();
            int loose = DespawnLooseItemsOnMat();

            MatDeployed = false;
            SetState(StallState.Exploring);
            GameAudio.PlayAt(SfxId.StallClose, MatCenter);

            // 箱子回到收攤那個人手上；手不空的話就留在襯布原處，並讓大家知道為什麼
            if (suitcase != null && requester != null && !suitcase.ReturnToHands(requester))
                RPC_Notice("手上有東西，手提箱先留在原地");

            Debug.Log($"[擺攤] 收攤完成：收回機台 {devices} 台、襯布上的物品 {loose} 個。");
        }

        private SuitcaseItem FindSuitcaseOnMat()
        {
            float limit = GameTuning.StallMatSize * 0.5f + GameTuning.StallCollectRadius;

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

        private int DespawnLooseItemsOnMat()
        {
            int n = 0;
            float limit = GameTuning.StallMatSize * 0.5f + GameTuning.StallCollectRadius;

            for (int i = CarriableItem.All.Count - 1; i >= 0; i--)
            {
                var item = CarriableItem.All[i];
                if (item == null || item.Object == null) continue;
                if (item.IsHeld) continue;                    // 拿在手上的不動
                if (item.Kind == ItemKind.Suitcase) continue; // 手提箱本身留著

                var local = StallGeometry.WorldToMat(item.transform.position, MatCenter, MatYaw);
                if (Mathf.Abs(local.x) > limit || Mathf.Abs(local.z) > limit) continue;
                if (Mathf.Abs(local.y) > 3f) continue;

                Runner.Despawn(item.Object);
                n++;
            }
            return n;
        }

        // ---------------- 機台放置 ----------------

        /// <summary>
        /// 驗證一個放置位置。**本機預覽與狀態權威的實際放置都呼叫這一支**，
        /// 所以玩家看到綠色就一定放得下去。
        /// </summary>
        public PlacementResult ValidatePlacement(Vector3 point, DeployableDevice ignore, out Vector3 snapped)
        {
            snapped = point;

            if (!MatDeployed) return PlacementResult.NotDeploying;
            if (!IsArrangeMode) return PlacementResult.NotDeploying;

            if (!StallGeometry.InsideMat(point, MatCenter, MatYaw,
                                         GameTuning.StallMatSize, GameTuning.StallDeviceEdgeMargin))
                return PlacementResult.OutsideMat;

            var ground = StallGeometry.CheckGround(point, MatYaw, GameTuning.MachineFootprint, out float groundY);
            if (ground != PlacementResult.Ok) return ground;
            snapped = new Vector3(point.x, groundY, point.z);

            // 手提箱本身也要留出空間：它躺在襯布中央，而且是開選單的入口，
            // 被機台壓住的話準心會一直選到機台，選單就打不開了。
            var toCase = new Vector2(MatCenter.x - snapped.x, MatCenter.z - snapped.z);
            if (toCase.magnitude < GameTuning.StallMinDeviceSpacing) return PlacementResult.Overlapping;

            for (int i = 0; i < DeployableDevice.All.Count; i++)
            {
                var d = DeployableDevice.All[i];
                if (d == null || !d.StallOwned) continue;
                if (ReferenceEquals(d, ignore)) continue;

                var p = d.OwnerTransform != null ? d.OwnerTransform.position : Vector3.zero;
                var flat = new Vector2(p.x - snapped.x, p.z - snapped.z);
                if (flat.magnitude < GameTuning.StallMinDeviceSpacing) return PlacementResult.Overlapping;
            }

            return PlacementResult.Ok;
        }

        /// <summary>
        /// 把一台機台放到襯布上。只在 StateAuthority 呼叫。
        /// 工具類（剃毛器／噴槍）直接生成可攜帶物品，其餘生成有 DeployableDevice 的機台。
        /// </summary>
        public bool TryPlaceDevice(LevelElementType type, Vector3 point, float yaw, out PlacementResult reason)
        {
            reason = ValidatePlacement(point, null, out var snapped);
            if (!HasStateAuthority) return false;
            if (reason != PlacementResult.Ok)
            {
                GameAudio.PlayAt(SfxId.PlaceRejected, point);
                return false;
            }

            if (StallCatalog.IsToolDevice(type))
            {
                var kind = StallCatalog.ToolKind(type);
                var tool = ItemFactory.Spawn(Runner, kind, default,
                                             snapped + Vector3.up * 0.35f, Quaternion.Euler(0f, yaw, 0f));
                if (tool == null)
                {
                    reason = PlacementResult.NothingPending;
                    return false;
                }
                GameAudio.PlayAt(SfxId.DevicePlace, snapped);
                return true;
            }

            var prefab = StallCatalog.DevicePrefab(type);
            if (prefab == null)
            {
                Debug.LogError($"[擺攤] GameCatalog 裡沒有 {type} 的 prefab，或它上面沒有 NetworkObject。");
                reason = PlacementResult.NothingPending;
                return false;
            }

            var obj = Runner.Spawn(prefab, snapped, Quaternion.Euler(0f, yaw, 0f), null, (r, o) =>
            {
                var dev = o.GetComponentInChildren<DeployableDevice>(true);
                if (dev != null) dev.MarkDeployed(type);
            });

            if (obj == null)
            {
                Debug.LogError($"[擺攤] Runner.Spawn({type}) 回傳 null。" +
                               "多半是 prefab 沒有被登錄在 Fusion 的 prefab 表裡 —— " +
                               "請先執行「羊駝很忙 / 1. 建置佔位資產」，再跑選單 " +
                               "Tools > Fusion > Rebuild Prefab Table。");
                reason = PlacementResult.NothingPending;
                return false;
            }

            GameAudio.PlayAt(SfxId.DevicePlace, snapped);
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
            if (CountDeployed(LevelElementType.DeliveryCounter) == 0)
            {
                reason = "還沒放交貨窗口，客人沒地方付錢";
                return false;
            }
            if (CountDeployed(LevelElementType.SewingMachine) == 0)
            {
                reason = "還沒放縫紉機，做不出衣服";
                return false;
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

        /// <summary>房主按下開張。任何人都能送，但只有房主送的才會被接受。</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestOpenForBusiness(RpcInfo info = default)
        {
            // 狀態權威所在的機器就是房主，所以「來源 == 本機玩家」等於「是房主送的」
            if (info.Source != Runner.LocalPlayer) return;
            OpenForBusiness();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestDismissSettlement()
        {
            DismissSettlement();
        }

        /// <summary>
        /// 收攤。requesterId 是按下收攤那個玩家的 NetworkId —— 箱子要回到他手上。
        /// 用 NetworkId 而不是 PlayerRef，是因為專案沒有呼叫 Runner.SetPlayerObject，
        /// 從 PlayerRef 反查不到玩家物件。
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_RequestCollectStall(NetworkId requesterId)
        {
            if (State == StallState.Open) return;   // 營業中不准收攤

            PlayerController requester = null;
            if (requesterId.IsValid && Runner.TryFindObject(requesterId, out var obj))
                requester = obj.GetComponent<PlayerController>();

            CollectStall(requester);
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

        /// <summary>給本機呼叫的提示（不需要走網路的那種，例如自己按錯鍵）。</summary>
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
                _matVisual.Apply(MatCenter, MatYaw, GameTuning.StallMatSize, IsArrangeMode);
            }
            else if (_matVisual != null)
            {
                _matVisual.Hide();
            }
        }
    }
}
