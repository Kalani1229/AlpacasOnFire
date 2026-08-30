using AlpacasOnFire.Core;
using AlpacasOnFire.Networking;
using AlpacasOnFire.Orders;
using AlpacasOnFire.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AlpacasOnFire.UI
{
    /// <summary>結算畫面：星級 + 顧客照片牆（Phase 1 用佔位方塊）。</summary>
    public class ResultsScreen : MonoBehaviour
    {
        public static ResultsScreen Instance { get; private set; }

        private GameObject _root;
        private Text _moneyLabel;
        private Text _starLabel;
        private Text _thresholdLabel;
        private readonly Image[] _stars = new Image[3];

        public bool IsOpen => _root != null && _root.activeSelf;

        private bool _shown;
        private bool _closedManually;

        private void Awake()
        {
            Instance = this;
            Build();
            LevelDirector.OnLevelFinished += Show;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            LevelDirector.OnLevelFinished -= Show;
        }

        private void Build()
        {
            var dim = UIFactory.Panel("ResultsDim", transform, new Color(0f, 0f, 0f, 0.78f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _root = dim.gameObject;

            var panel = UIFactory.Panel("ResultsPanel", dim.transform, new Color(0.09f, 0.10f, 0.13f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(880f, 620f));

            UIFactory.Label("Title", panel.transform, "關卡結算", 44, TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(700f, 56f));

            // 星星
            for (int i = 0; i < 3; i++)
            {
                _stars[i] = UIFactory.Panel($"Star{i}", panel.transform, new Color(0.3f, 0.3f, 0.3f),
                    new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                    new Vector2((i - 1) * 130f, -110f), new Vector2(96f, 96f));
            }

            _moneyLabel = UIFactory.Label("Money", panel.transform, "$0", 40, TextAnchor.UpperCenter,
                new Color(0.98f, 0.85f, 0.35f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -226f), new Vector2(700f, 52f));

            _starLabel = UIFactory.Label("Stars", panel.transform, "", 28, TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -280f), new Vector2(700f, 40f));

            _thresholdLabel = UIFactory.Label("Thresholds", panel.transform, "", 20, TextAnchor.UpperCenter,
                new Color(0.75f, 0.8f, 0.85f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -318f), new Vector2(700f, 36f));

            // 顧客照片牆（佔位方塊）
            UIFactory.Label("WallTitle", panel.transform, "顧客", 22, TextAnchor.UpperLeft,
                new Color(0.8f, 0.85f, 0.9f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(64f, -360f), new Vector2(300f, 30f));

            for (int i = 0; i < 6; i++)
            {
                var c = PlaceholderPalette.Dye((DyeColorType)(i % 5));
                UIFactory.Panel($"Customer{i}", panel.transform, c * 0.9f,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(64f + i * 128f, -398f), new Vector2(112f, 112f));
            }

            UIFactory.TextButton("Again", panel.transform, "再玩一次",
                new Vector2(-110f, -240f), new Vector2(200f, 54f), Restart);
            UIFactory.TextButton("Close", panel.transform, "關閉",
                new Vector2(110f, -240f), new Vector2(200f, 54f), Close);

            _root.SetActive(false);
        }

        public void Show(int money, int stars)
        {
            // 擺攤模式改用 StallResultsPanel 顯示「本場營業額 / 成交 / 錯過」，
            // 星級結算的程式碼整段保留，只是不觸發（規格書：先關閉但不要刪除）。
            var director = LevelDirector.Instance;
            if (director != null && director.StallMode) return;

            _shown = true;
            _root.SetActive(true);
            _moneyLabel.text = $"總金額 ${money}";
            _starLabel.text = stars > 0 ? $"{stars} 星通關" : "沒有達到 1 星門檻";

            var d = LevelDirector.Instance;
            int s1 = d != null ? d.Star1 : GameTuning.Star1Threshold;
            int s2 = d != null ? d.Star2 : GameTuning.Star2Threshold;
            int s3 = d != null ? d.Star3 : GameTuning.Star3Threshold;
            _thresholdLabel.text = $"星級門檻：★ ${s1}　★★ ${s2}　★★★ ${s3}";

            for (int i = 0; i < _stars.Length; i++)
                _stars[i].color = i < stars ? new Color(1f, 0.85f, 0.25f) : new Color(0.28f, 0.28f, 0.3f);

            LocalInputProvider.Instance?.SetCursorLocked(false);
        }

        public void Close()
        {
            _root.SetActive(false);
            _closedManually = true;
            LocalInputProvider.Instance?.SetCursorLocked(true);
        }

        /// <summary>
        /// 保險：不依賴 RPC。只要 LevelDirector 的 [Networked] 狀態顯示關卡已結束就開結算，
        /// 這樣離線 GameMode.Single 與中途加入的用戶端都不會漏掉結算畫面。
        /// </summary>
        private void Update()
        {
            if (_shown || _closedManually) return;
            var d = LevelDirector.Instance;
            if (d == null || d.Object == null || !d.Finished) return;
            Show(d.Money, d.Stars);
        }

        private void Restart()
        {
            GameLauncher.Instance?.Shutdown();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
