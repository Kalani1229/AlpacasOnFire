using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlpacasOnFire.Core;
using AlpacasOnFire.Player;
using AlpacasOnFire.UI;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlpacasOnFire.Networking
{
    /// <summary>
    /// Fusion 的進入點：建立 NetworkRunner、開場、生成玩家、收集本機輸入。
    ///
    /// 開發期用 GameMode.Single（完全離線）反覆測玩法；
    /// 驗證完再用 Host / Client 做真正的連線測試。
    /// </summary>
    [RequireComponent(typeof(NetworkRunner))]
    public class GameLauncher : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static GameLauncher Instance { get; private set; }

        [Header("Prefabs")]
        [Tooltip("留空的話會自動從 Resources/GameCatalog.asset 取用。")]
        [SerializeField] private NetworkObject _playerPrefab;

        [Header("Session")]
        [SerializeField] private string _sessionName = "AlpacasOnFire";
        [SerializeField] private int _maxPlayers = 4;
        [SerializeField] private bool _autoStartSingle = true;
        [SerializeField] private GameMode _autoStartMode = GameMode.Single;

        private NetworkRunner _runner;
        private readonly Dictionary<PlayerRef, NetworkObject> _spawned = new();
        private int _nextColorIndex;

        public NetworkRunner Runner => _runner;
        public bool IsRunning => _runner != null && _runner.IsRunning;

        private void Awake()
        {
            Instance = this;
            _runner = GetComponent<NetworkRunner>();
            _runner.ProvideInput = true;
        }

        private async void Start()
        {
            GameUIRoot.EnsureExists();

            // 從標題畫面進來的話用它選的模式，直接播關卡場景就用 Inspector 的預設值
            bool fromTitle = PendingLaunch.HasPending;
            var mode = PendingLaunch.Consume(_autoStartMode);
            if ((_autoStartSingle || fromTitle) && !IsRunning)
                await StartGame(mode);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public async Task StartGame(GameMode mode, string sessionName = null)
        {
            if (IsRunning) return;

            // ---- 地圖 B：先有世界，再啟動 Runner ----
            //
            // 場景裡的 NPC、大動物是 Fusion 的場景物件，Runner 一啟動就會 Spawned()。
            // 城市是執行期生成的；如果牠們先活起來、城市後生成，牠們會在空中或建築裡開始走動。
            // 所以在這裡同步生成城市、烤好 NavMesh，然後才 StartGame。
            //
            // 不會被 Runner 啟動時的載入場景洗掉：NetworkSceneManagerDefault 預設
            // IsSceneTakeOverEnabled = true，已經載入的場景會被直接接手、不重新載入。
            //
            // 放在 StartGame 而不是 Start()：從標題畫面進來、自動開場、之後從選單呼叫，
            // 三條路都會經過這裡。沒有 RandomMapBuilder 的場景（Stall_Test）什麼都不做。
            PrepareWorld();

            _runner.ProvideInput = true;
            if (_runner.GetComponent<NetworkSceneManagerDefault>() == null)
                _runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            var sceneRef = SceneRef.FromIndex(SceneManager.GetActiveScene().buildIndex);
            var sceneInfo = new NetworkSceneInfo();
            if (sceneRef.IsValid)
                sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Additive);

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode     = mode,
                SessionName  = string.IsNullOrEmpty(sessionName) ? _sessionName : sessionName,
                PlayerCount  = _maxPlayers,
                Scene        = sceneInfo,
                SceneManager = _runner.GetComponent<NetworkSceneManagerDefault>(),
            });

            if (!result.Ok)
                Debug.LogError($"[GameLauncher] 啟動失敗: {result.ShutdownReason}");
            else
                Debug.Log($"[GameLauncher] 已啟動，模式 = {mode}");
        }

        /// <summary>
        /// 生成城市、烤 NavMesh，然後把玩家出生點搬到第一個廣場。
        ///
        /// 出生點用覆寫的方式給 SpawnPointRegistry，玩家生出來就在廣場上，
        /// 不需要事後 Teleport。NPC 與大動物的落位在牠們自己的 Spawned() 裡做
        /// （那時候牠們才有 NetworkCharacterController 可以 Teleport）。
        /// </summary>
        private static void PrepareWorld()
        {
            // 覆寫是靜態的，先清掉 —— 不然從羊駝村回到 Stall_Test 會沿用上一張地圖的廣場
            SpawnPointRegistry.SetOverride(null);

            var map = FindAnyObjectByType<Map.RandomMapBuilder>();
            if (map == null) return;

            map.EnsureGenerated();

            if (map.PlazaCenters.Count == 0)
            {
                Debug.LogWarning("[GameLauncher] 地圖沒有任何廣場，玩家出生點維持場景原本的位置。");
                return;
            }

            // 四個玩家在廣場中心附近散開一點，不要疊在一起
            var center = map.PlazaCenters[0];
            var points = new List<Vector3>();
            for (int i = 0; i < 4; i++)
            {
                var wish = center + Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward * 2.5f;
                if (!Map.NavUtil.SnapToNavMesh(wish, 3f, out var p)) p = wish;
                points.Add(p);
            }
            SpawnPointRegistry.SetOverride(points);
        }

        public async void Shutdown()
        {
            if (_runner != null && _runner.IsRunning)
                await _runner.Shutdown();
        }

        // ---------------- 玩家生成 ----------------

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer) return;

            var prefab = _playerPrefab != null ? _playerPrefab
                       : (GameCatalog.Instance != null ? GameCatalog.Instance.playerPrefab : null);
            if (prefab == null)
            {
                Debug.LogError("[GameLauncher] 找不到玩家 prefab。請執行選單「羊駝很忙 / 建置佔位資產」。");
                return;
            }

            var (pos, rot) = SpawnPointRegistry.Next();
            var obj = runner.Spawn(prefab, pos, rot, player, (r, o) =>
            {
                var pc = o.GetComponent<PlayerController>();
                if (pc != null) pc.ColorIndex = _nextColorIndex;
            });
            _nextColorIndex = (_nextColorIndex + 1) % PlaceholderPalette.PlayerColors.Length;
            _spawned[player] = obj;
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer) return;
            if (_spawned.TryGetValue(player, out var obj) && obj != null)
                runner.Despawn(obj);
            _spawned.Remove(player);
        }

        // ---------------- 輸入 ----------------

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            var provider = LocalInputProvider.Instance;
            input.Set(provider != null ? provider.Poll() : default);
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

        // ---------------- 其餘 callback（Phase 1 不需要處理） ----------------

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
            => Debug.Log($"[GameLauncher] Shutdown: {shutdownReason}");
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
