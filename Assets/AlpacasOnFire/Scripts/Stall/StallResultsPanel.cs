using AlpacasOnFire.Core;
using AlpacasOnFire.Player;
using AlpacasOnFire.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 一場營業結束後的結算：本場營業額、成交筆數、錯過的訂單數，以及累加後的資本額。
    ///
    /// 星級結算（ResultsScreen）在擺攤模式下不會被觸發，但程式碼原封不動留著，
    /// 之後要拿回來用只要把 LevelDirector 的「擺攤模式」勾掉即可。
    /// </summary>
    public class StallResultsPanel : MonoBehaviour
    {
        public static StallResultsPanel Instance { get; private set; }

        private GameObject _root;
        private Text _revenueLabel;
        private Text _dealsLabel;
        private Text _missedLabel;
        private Text _capitalLabel;
        private Text _capitalDeltaLabel;

        private bool _shownForThisRound;

        public bool IsOpen => _root != null && _root.activeSelf;

        private void Awake()
        {
            Instance = this;
            Build();
            StallManager.OnRoundSettled += Show;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            StallManager.OnRoundSettled -= Show;
        }

        private void Build()
        {
            var dim = UIFactory.Panel("StallResultsDim", transform, new Color(0f, 0f, 0f, 0.78f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _root = dim.gameObject;

            var panel = UIFactory.Panel("StallResultsPanel", dim.transform, new Color(0.09f, 0.10f, 0.13f, 0.98f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 520f));

            UIFactory.Label("Title", panel.transform, "今天的生意", 42, TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -26f), new Vector2(700f, 54f));

            _revenueLabel = UIFactory.Label("Revenue", panel.transform, "", 46, TextAnchor.UpperCenter,
                new Color(0.98f, 0.85f, 0.35f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -96f), new Vector2(700f, 58f));

            _dealsLabel = UIFactory.Label("Deals", panel.transform, "", 26, TextAnchor.UpperCenter,
                new Color(0.55f, 0.95f, 0.65f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -170f), new Vector2(700f, 36f));

            _missedLabel = UIFactory.Label("Missed", panel.transform, "", 26, TextAnchor.UpperCenter,
                new Color(1f, 0.6f, 0.45f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -210f), new Vector2(700f, 36f));

            UIFactory.Panel("Divider", panel.transform, new Color(1f, 1f, 1f, 0.12f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -262f), new Vector2(620f, 2f));

            _capitalLabel = UIFactory.Label("Capital", panel.transform, "", 32, TextAnchor.UpperCenter,
                Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -292f), new Vector2(700f, 44f));

            _capitalDeltaLabel = UIFactory.Label("CapitalDelta", panel.transform, "", 20, TextAnchor.UpperCenter,
                new Color(0.72f, 0.78f, 0.86f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -336f), new Vector2(700f, 30f));

            UIFactory.Label("Hint", panel.transform, "收起來換個地方，或是原地再開一場",
                18, TextAnchor.LowerCenter, new Color(0.65f, 0.7f, 0.78f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 86f), new Vector2(700f, 28f));

            UIFactory.TextButton("Continue", panel.transform, "繼續",
                new Vector2(0f, -200f), new Vector2(240f, 56f), Dismiss);

            _root.SetActive(false);
        }

        public void Show(int revenue, int deals, int missed, int capital)
        {
            _shownForThisRound = true;
            _root.SetActive(true);

            _revenueLabel.text = $"本場營業額　${revenue}";
            _dealsLabel.text = $"成交　{deals} 筆";
            _missedLabel.text = missed > 0 ? $"錯過的訂單　{missed} 筆" : "沒有錯過任何訂單";
            _missedLabel.color = missed > 0 ? new Color(1f, 0.6f, 0.45f) : new Color(0.55f, 0.95f, 0.65f);

            _capitalLabel.text = $"資本額　${capital}";
            _capitalDeltaLabel.text = revenue >= 0
                ? $"（本場 +${revenue}）"
                : $"（本場 -${-revenue}）";

            LocalInputProvider.Instance?.SetCursorLocked(false);
        }

        private void Dismiss()
        {
            _root.SetActive(false);
            _shownForThisRound = false;

            StallManager.Instance?.RPC_RequestDismissSettlement();

            if (PauseMenu.Instance == null || !PauseMenu.Instance.IsOpen)
                LocalInputProvider.Instance?.SetCursorLocked(true);
        }

        /// <summary>
        /// 保險：不依賴 RPC。中途加入或漏收 RPC 的用戶端，只要看到狀態是 Settling 就自己開結算，
        /// 做法跟既有 ResultsScreen 的保險機制一致。
        /// </summary>
        private void Update()
        {
            if (_shownForThisRound) return;

            var stall = StallManager.Instance;
            if (stall == null || stall.Object == null) return;
            if (stall.State != StallState.Settling) return;

            Show(stall.RoundRevenue, stall.RoundDeliveries, stall.RoundMissed, stall.Capital);
        }
    }
}
