using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Player;
using AlpacasOnFire.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 攤位選單。做法直接抄既有的 PatternSelectPanel（已經驗證可用的先例）：
    /// 只在觸發互動的那個用戶端打開，選完之後用 RPC 回報給狀態權威。
    ///
    /// 這個面板同時是「開張」與「收攤」的入口 —— 規格書要求不新增按鍵，
    /// 而佈置模式的 Space 已經被「拿起／放下機台」用掉了，所以這兩個動作放在選單裡最自然。
    /// </summary>
    public class SuitcasePanel : MonoBehaviour
    {
        public static SuitcasePanel Instance { get; private set; }

        private GameObject _root;
        private SuitcaseItem _suitcase;
        private Text _stateLabel;
        private Text _hintLabel;
        private Button _openButton;
        private Text _openButtonLabel;
        private readonly List<(Button button, Text label, LevelElementType type)> _deviceButtons = new();

        public bool IsOpen => _root != null && _root.activeSelf;

        private void Awake()
        {
            Instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------- 版面 ----------------

        private void Build()
        {
            var panel = UIFactory.Panel("SuitcasePanel", transform, new Color(0.05f, 0.06f, 0.08f, 0.94f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(760f, 460f));
            _root = panel.gameObject;

            UIFactory.Label("Title", panel.transform, "手提箱", 34, TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(700f, 44f));

            _stateLabel = UIFactory.Label("State", panel.transform, "", 20, TextAnchor.UpperCenter,
                new Color(0.75f, 0.82f, 0.9f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -62f), new Vector2(700f, 30f));

            // 裝備清單：一列四個
            const float w = 160f, h = 90f, gapX = 12f, gapY = 12f;
            int perRow = 4;
            for (int i = 0; i < StallCatalog.Devices.Length; i++)
            {
                var type = StallCatalog.Devices[i];
                int row = i / perRow;
                int col = i % perRow;
                int inRow = Mathf.Min(perRow, StallCatalog.Devices.Length - row * perRow);

                float rowWidth = inRow * w + (inRow - 1) * gapX;
                float x = -rowWidth * 0.5f + w * 0.5f + col * (w + gapX);
                float y = 60f - row * (h + gapY);

                var btn = UIFactory.TextButton($"Dev_{type}", panel.transform,
                    StallCatalog.DisplayName(type), new Vector2(x, y), new Vector2(w, h),
                    () => Choose(type));

                var label = btn.GetComponentInChildren<Text>();
                _deviceButtons.Add((btn, label, type));
            }

            _hintLabel = UIFactory.Label("Hint", panel.transform,
                "選一台 → 準心投影在襯布上 → [滾輪] 旋轉 → [Space] 放置 → [Q] 取消",
                18, TextAnchor.MiddleCenter, new Color(0.72f, 0.78f, 0.86f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 128f), new Vector2(720f, 28f));

            _openButton = UIFactory.TextButton("Open", panel.transform, "開張",
                new Vector2(-230f, -170f), new Vector2(200f, 52f), RequestOpen);
            _openButtonLabel = _openButton.GetComponentInChildren<Text>();

            UIFactory.TextButton("Collect", panel.transform, "收攤",
                new Vector2(0f, -170f), new Vector2(200f, 52f), RequestCollect);

            UIFactory.TextButton("Close", panel.transform, "關閉 (Esc)",
                new Vector2(230f, -170f), new Vector2(200f, 52f), Close);

            _root.SetActive(false);
        }

        // ---------------- 開關 ----------------

        public static void Open(SuitcaseItem suitcase)
        {
            var inst = Instance;
            if (inst == null)
            {
                StallUIRoot.EnsureExists();
                inst = Instance;
                if (inst == null) return;
            }

            inst._suitcase = suitcase;
            inst._root.SetActive(true);
            inst.Refresh();
            LocalInputProvider.Instance?.SetCursorLocked(false);
        }

        public void Close()
        {
            _root.SetActive(false);
            _suitcase = null;
            if (PauseMenu.Instance == null || !PauseMenu.Instance.IsOpen)
                LocalInputProvider.Instance?.SetCursorLocked(true);
        }

        // ---------------- 動作 ----------------

        private void Choose(LevelElementType type)
        {
            var agent = LocalAgent();
            if (agent == null) return;

            agent.RPC_RequestPlacement((int)type);
            Close();
        }

        private void RequestOpen()
        {
            var stall = StallManager.Instance;
            if (stall == null) return;

            if (!IsHost(stall))
            {
                StallManager.LocalNotice("只有房主可以按開張");
                return;
            }
            if (!stall.CanOpenForBusiness(out string reason))
            {
                StallManager.LocalNotice(reason);
                return;
            }

            stall.RPC_RequestOpenForBusiness();
            Close();
        }

        private void RequestCollect()
        {
            var stall = StallManager.Instance;
            if (stall == null) return;

            if (stall.State == StallState.Open)
            {
                StallManager.LocalNotice("營業中不能收攤，先等這場結束");
                return;
            }

            var player = PlayerController.Local;
            stall.RPC_RequestCollectStall(player != null && player.Object != null
                                          ? player.Object.Id : default);
            Close();
        }

        private static PlayerStallAgent LocalAgent()
        {
            var p = PlayerController.Local;
            return p != null ? p.GetComponent<PlayerStallAgent>() : null;
        }

        /// <summary>本機是不是房主（狀態權威）。GameMode.Single 也算，方便單機測試。</summary>
        private static bool IsHost(StallManager stall)
            => stall != null && stall.Runner != null && stall.Runner.IsServer;

        // ---------------- 更新 ----------------

        private void Refresh()
        {
            var stall = StallManager.Instance;
            if (stall == null) return;

            bool isHost = IsHost(stall);
            bool business = stall.IsBusinessMode;

            _stateLabel.text = business
                ? $"營業中 — 機台已鎖定　已擺放 {stall.DeployedCount()} 台"
                : $"佈置模式　已擺放 {stall.DeployedCount()} 台　資本額 ${stall.Capital}";

            foreach (var (button, label, type) in _deviceButtons)
            {
                button.interactable = !business;
                if (label == null) continue;
                int count = StallCatalog.IsToolDevice(type) ? 0 : stall.CountDeployed(type);
                label.text = count > 0
                    ? $"{StallCatalog.DisplayName(type)}　×{count}"
                    : StallCatalog.DisplayName(type);
                label.color = business ? new Color(0.55f, 0.55f, 0.6f) : Color.white;
            }

            bool openable = stall.CanOpenForBusiness(out string blockReason);
            bool canOpen = !business && openable;
            _openButton.interactable = isHost && canOpen;
            if (_openButtonLabel != null)
            {
                _openButtonLabel.text = business ? "營業中" : isHost ? "開張" : "開張（房主）";
                _openButtonLabel.color = _openButton.interactable ? new Color(0.5f, 1f, 0.6f)
                                                                 : new Color(0.55f, 0.55f, 0.6f);
            }

            if (_hintLabel != null)
            {
                if (business)
                    _hintLabel.text = "營業中：Space 恢復成操作機台，機台不能移動";
                else if (!openable && !string.IsNullOrEmpty(blockReason))
                    _hintLabel.text = $"還不能開張：{blockReason}";
                else
                    _hintLabel.text = "選一台 → 準心投影在襯布上 → [滾輪] 旋轉 → [Space] 放置 → [Q] 取消";
            }
        }

        private void Update()
        {
            if (!IsOpen) return;

            Refresh();

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();

            // 手提箱被收走（例如別人收攤了）就自動關閉
            if (_suitcase == null || _suitcase.Object == null) Close();
        }
    }
}
