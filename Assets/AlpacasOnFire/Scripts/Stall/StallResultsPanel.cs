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
        private Text _titleLabel;
        private Text _revenueLabel;
        private Text _dealsLabel;
        private Text _missedLabel;
        private Text _capitalLabel;
        private Text _capitalDeltaLabel;
        private Text _runLineLabel;
        private Text _hintLabel;
        private Button _continueButton;

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

            _titleLabel = UIFactory.Label("Title", panel.transform, "今天的生意", 42, TextAnchor.UpperCenter,
                Color.white,
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

            // run 循環才會用到的一行：達標時寫下一輪目標，沒達標時寫「最貴的一件」
            _runLineLabel = UIFactory.Label("RunLine", panel.transform, "", 24, TextAnchor.UpperCenter,
                new Color(0.85f, 0.9f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -372f), new Vector2(700f, 34f));

            _hintLabel = UIFactory.Label("Hint", panel.transform, "收起來換個地方，或是原地再開一場",
                18, TextAnchor.LowerCenter, new Color(0.65f, 0.7f, 0.78f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 86f), new Vector2(700f, 28f));

            _continueButton = UIFactory.TextButton("Continue", panel.transform, "繼續",
                new Vector2(0f, -200f), new Vector2(240f, 56f), Dismiss);

            _root.SetActive(false);
        }

        /// <summary>
        /// 結算畫面有兩套版面：
        ///
        ///   達標   —— 照舊，多一行「第 N 輪達標，下一輪目標 XXX」
        ///   沒達標 —— 換成「撐過了 N 輪」，**差額要大到刺眼**
        ///
        /// 差 40 塊跟差 400 塊，玩家的反應完全不同 —— 前者會想「再來一次」，
        /// 後者會想「我哪裡做錯了」。那個數字是這一局最後留下的印象，
        /// 所以它用最大的字級，比營業額還大。
        /// </summary>
        public void Show(int revenue, int deals, int missed, int capital, int target, bool passed)
        {
            _shownForThisRound = true;
            _root.SetActive(true);

            var stall = StallManager.Instance;
            bool runMode = stall != null && stall.RunMode;

            _dealsLabel.text = $"成交　{deals} 筆";
            _missedLabel.text = missed > 0 ? $"錯過的訂單　{missed} 筆" : "沒有錯過任何訂單";
            _missedLabel.color = missed > 0 ? new Color(1f, 0.6f, 0.45f) : new Color(0.55f, 0.95f, 0.65f);

            _capitalLabel.text = $"資本額　${capital}";
            _capitalDeltaLabel.text = revenue >= 0
                ? $"（本場 +${revenue}）"
                : $"（本場 -${-revenue}）";

            if (!passed) ShowRunOver(revenue, target, stall);
            else ShowRoundCleared(revenue, target, runMode, stall);

            LocalInputProvider.Instance?.SetCursorLocked(false);
        }

        /// <summary>達標（或根本沒有門檻的 Stall_Test）。</summary>
        private void ShowRoundCleared(int revenue, int target, bool runMode, StallManager stall)
        {
            _titleLabel.text = "今天的生意";
            _titleLabel.color = Color.white;

            _revenueLabel.text = $"本場營業額　${revenue}";
            _revenueLabel.fontSize = 46;
            _revenueLabel.color = new Color(0.98f, 0.85f, 0.35f);

            // 沒有門檻的場次（Stall_Test）什麼都不多說，維持原本的畫面
            if (!runMode || target <= 0)
            {
                _runLineLabel.text = "";
            }
            else
            {
                // CurrentRound 在 Settle() 裡已經 +1 了，所以它現在就是「下一輪」
                int next = stall != null ? stall.CurrentRound : 1;
                _runLineLabel.text = $"第 {Mathf.Max(1, next - 1)} 輪達標　—　下一輪目標 " +
                                     $"{GameTuning.StallTargetFor(next)}";
                _runLineLabel.color = new Color(0.55f, 0.95f, 0.65f);
            }

            _hintLabel.text = "收起來換個地方，或是原地再開一場";
            SetContinueText("繼續");
        }

        /// <summary>沒達標 —— 這一局結束。</summary>
        private void ShowRunOver(int revenue, int target, StallManager stall)
        {
            // 「撐過了幾輪」算的是**成功過關的輪數**，不是打過的輪數。
            // CurrentRound 只在達標時才 +1，所以第 N 輪倒下時它還停在 N，
            // 真正撐過的是 N-1 輪。倒在第 1 輪就是 0 輪 —— 誠實一點比較好笑。
            int cleared = stall != null ? Mathf.Max(0, stall.CurrentRound - 1) : 0;
            int deals = stall != null ? stall.RoundDeliveries : 0;
            int shortfall = Mathf.Max(0, target - revenue);

            _titleLabel.text = $"撐過了 {cleared} 輪";
            _titleLabel.color = new Color(1f, 0.62f, 0.48f);

            // **差額用最大的字**，它是這一局最後留下的印象
            _revenueLabel.text = $"還差 ${shortfall}";
            _revenueLabel.fontSize = 58;
            _revenueLabel.color = new Color(1f, 0.45f, 0.4f);

            _dealsLabel.text = $"本輪目標 ${target}　實際賺到 ${revenue}　｜　成交 {deals} 筆";

            string best = stall != null && stall.BestSalePrice > 0
                ? $"最貴的一件：{stall.BestSale.Describe()}　${stall.BestSalePrice}"
                : "這一局沒有賣出任何東西";
            _runLineLabel.text = best;
            _runLineLabel.color = new Color(0.95f, 0.88f, 0.6f);

            _hintLabel.text = "這一局到此為止 —— 想再來一次請重新開始場景";
            SetContinueText("關閉");
        }

        private void SetContinueText(string text)
        {
            if (_continueButton == null) return;
            var label = _continueButton.GetComponentInChildren<Text>();
            if (label != null) label.text = text;
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
        /// 保險：不依賴 RPC。中途加入或漏收 RPC 的用戶端，只要看到狀態是 Settling／RunOver
        /// 就自己開結算，做法跟既有 ResultsScreen 的保險機制一致。
        ///
        /// **RunOver 也要納入**，不然沒達標的那一局，漏收 RPC 的 client 會什麼都看不到。
        /// </summary>
        private void Update()
        {
            if (_shownForThisRound) return;

            var stall = StallManager.Instance;
            if (stall == null || stall.Object == null) return;

            bool settling = stall.State == StallState.Settling;
            bool runOver = stall.State == StallState.RunOver;
            if (!settling && !runOver) return;

            Show(stall.RoundRevenue, stall.RoundDeliveries, stall.RoundMissed, stall.Capital,
                 stall.RoundTarget, passed: settling);
        }
    }
}
