using AlpacasOnFire.Core;
using AlpacasOnFire.Orders;
using AlpacasOnFire.Player;
using AlpacasOnFire.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 擺攤專用的 HUD：目前模式、資本額、已擺放數量、提示訊息、開張條件。
    ///
    /// **這裡沒有任何可點的按鈕。** 這個專案的游標從頭到尾是鎖住的
    /// （`LocalInputProvider` 把 `LookEnabled` 直接綁在游標鎖上），常駐的 HUD 按鈕點不到。
    /// 所以開張改成走過去敲**開張鈴**（世界物件，Space 互動），
    /// 收攤改成對著手提箱按 Space —— HUD 只負責告訴你「還缺什麼才能開張」。
    ///
    /// 既有的 GameHud（訂單卡、計時、金額、互動提示）完全沒有動。
    /// </summary>
    public class StallHud : MonoBehaviour
    {
        private Text _modeLabel;
        private Text _capitalLabel;
        private Text _targetLabel;
        private Text _noticeLabel;
        private Image _modeChip;

        private Text _openHintLabel;
        private Text _dayWarnLabel;
        private Text _upcomingLabel;

        /// <summary>
        /// 大額日的顏色。**這個顏色就是訊號** —— 不寫「今天是大額日」，
        /// 主行的「目標」與未來三天那一排都用它標出來。
        /// </summary>
        private const string BalloonHex = "#FF6FD8";

        private float _noticeTimer;

        private void Start()
        {
            Build();
            StallManager.OnStallNotice += ShowNotice;
            StallManager.OnStateChanged += HandleStateChanged;
        }

        private void OnDestroy()
        {
            StallManager.OnStallNotice -= ShowNotice;
            StallManager.OnStateChanged -= HandleStateChanged;
        }

        private void Build()
        {
            // 左上角的模式標籤（GameHud 的計時與金額在正上方中央，不會打架）
            _modeChip = UIFactory.Panel("ModeChip", transform, new Color(0.1f, 0.11f, 0.14f, 0.85f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -24f), new Vector2(300f, 46f));
            _modeChip.raycastTarget = false;

            _modeLabel = UIFactory.Label("Mode", _modeChip.transform, "探索中", 24, TextAnchor.MiddleCenter,
                Color.white, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            _capitalLabel = UIFactory.Label("Capital", transform, "", 22, TextAnchor.UpperLeft,
                new Color(0.98f, 0.85f, 0.35f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(26f, -76f), new Vector2(420f, 30f));

            // run 循環的目標進度。**這是整個機制唯一的壓力來源，看不到就等於不存在**，
            // 所以放在資本額正下方、營業中一直亮著。
            _targetLabel = UIFactory.Label("RoundTarget", transform, "", 24, TextAnchor.UpperLeft,
                new Color(0.95f, 0.85f, 0.55f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(26f, -108f), new Vector2(760f, 32f));

            // 未來三天的目標（小字）。大哥平常不出現，**這是玩家唯一的預警系統** ——
            // 沒有它，第 3 天的大額就是驚喜死亡。
            _upcomingLabel = UIFactory.Label("UpcomingTargets", transform, "", 18, TextAnchor.UpperLeft,
                new Color(0.78f, 0.8f, 0.86f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -142f), new Vector2(760f, 26f));

            _noticeLabel = UIFactory.Label("StallNotice", transform, "", 28, TextAnchor.MiddleCenter,
                new Color(1f, 0.55f, 0.4f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -210f), new Vector2(1100f, 40f));

            // run 模式：天快黑了還沒開張的警告。畫面上方偏中，比一般提示大、會閃
            _dayWarnLabel = UIFactory.Label("DayNotOpenWarn", transform, "", 34, TextAnchor.MiddleCenter,
                new Color(1f, 0.3f, 0.25f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -120f), new Vector2(900f, 48f));

            // 開張條件提示：只是文字，沒有按鈕（游標鎖住，按鈕點不到）
            _openHintLabel = UIFactory.Label("OpenHint", transform, "", 20, TextAnchor.LowerRight,
                new Color(0.85f, 0.88f, 0.94f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-28f, 110f), new Vector2(620f, 28f));
        }

        private void HandleStateChanged(StallState state)
        {
            switch (state)
            {
                case StallState.Deploying:
                    ShowNotice("裝備都擺出來了 — 面向裝備按 Space 可以搬動");
                    break;
                case StallState.Open:
                    ShowNotice("開張！Space 恢復成操作機台");
                    break;
                case StallState.Night:
                    ShowNotice("天黑了 —— 今天的生意結束");
                    break;
            }
        }

        private void ShowNotice(string message)
        {
            if (string.IsNullOrEmpty(message) || _noticeLabel == null) return;
            _noticeLabel.text = message;
            _noticeTimer = 2.4f;
        }

        /// <summary>本機是不是房主（狀態權威）。GameMode.Single 也算，方便單機測試。</summary>
        private static bool IsHost(StallManager stall)
            => stall != null && stall.Runner != null && stall.Runner.IsServer;

        // ---------------- 每幀更新 ----------------

        private void Update()
        {
            var stall = StallManager.Instance;

            if (stall == null || stall.Object == null)
            {
                if (_modeChip != null) _modeChip.gameObject.SetActive(false);
                if (_capitalLabel != null) _capitalLabel.text = "";
                if (_targetLabel != null) _targetLabel.text = "";
                if (_openHintLabel != null) _openHintLabel.text = "";
                if (_dayWarnLabel != null) _dayWarnLabel.text = "";
                if (_upcomingLabel != null) _upcomingLabel.text = "";
                return;
            }

            if (!_modeChip.gameObject.activeSelf) _modeChip.gameObject.SetActive(true);

            var (text, color) = Describe(stall);
            _modeLabel.text = text;
            _modeChip.color = color;

            // run 模式顯示「含今天收入」的資本，跟下面那一行的數字一致
            int capital = stall.RunMode ? stall.ProjectedCapital : stall.Capital;
            _capitalLabel.text = stall.MatDeployed
                ? $"資本額 ${Fmt(capital)}　｜　攤位裝備 {stall.DeployedCount()} 台"
                : $"資本額 ${Fmt(capital)}";

            UpdateRoundTarget(stall);
            UpdateOpenHint(stall);
            UpdateNotice();
        }

        /// <summary>
        /// run 模式的一天：第幾天、剩多少時間、目標多少、已經賺了多少、還差多少。
        ///
        /// **從早上探索就開始顯示**，不是只有營業中 —— 玩家採集時就要知道今天要賺多少、
        /// 還剩多少時間，「什麼時候收手去開張」這個決策才做得出來。
        ///
        /// **達標的瞬間變綠並改寫成「已達標！」。** 這個回饋很重要 ——
        /// 它把後半天從「還在焦慮」翻成「多賺多賺」，是同一段時間裡兩種完全不同的心情。
        ///
        /// 只在 run 模式顯示：Stall_Test 沒有門檻也沒有全天時鐘，多一行數字只會干擾。
        /// 顯示「第 N 天」；欄位還是叫 CurrentRound（改名牽動太多地方）。
        /// </summary>
        private void UpdateRoundTarget(StallManager stall)
        {
            if (_targetLabel == null) return;

            if (stall.RunMode && stall.IsNight)
            {
                UpdateNight(stall);
                UpdateUpcoming(stall, true);
                return;
            }

            bool dayRunning = stall.RunMode && stall.DayActive && stall.RoundTarget > 0
                              && stall.State != StallState.Settling && stall.State != StallState.RunOver;

            if (!dayRunning)
            {
                _targetLabel.text = "";
                if (_dayWarnLabel != null) _dayWarnLabel.text = "";
                UpdateUpcoming(stall, false);
                return;
            }

            var director = LevelDirector.Instance;
            float remain = director != null && director.Running ? director.RemainingSeconds : 0f;
            int secs = Mathf.CeilToInt(remain);
            string clock = $"{secs / 60}:{secs % 60:00}";

            // 主要比較的是**累積資本**，不是今天賺的。括號裡是今天的貢獻
            int capital = stall.ProjectedCapital;
            int today = stall.CurrentRevenue;
            int target = stall.RoundTarget;
            string head = $"第 {stall.CurrentRound} 天　剩 {clock}　{TargetText(target, stall.TodayIsBalloon)}" +
                          $"　資本 {Fmt(capital)}（今天 {Signed(today)}）";

            if (capital >= target)
            {
                _targetLabel.text = $"{head}　已達標！";
                _targetLabel.color = new Color(0.45f, 0.95f, 0.5f);
            }
            else
            {
                _targetLabel.text = $"{head}　還差 {Fmt(target - capital)}";

                // 進度過 75% 轉暖色 —— 快要來不及的時候要看得出來
                float progress = capital / (float)target;
                _targetLabel.color = progress >= 0.75f
                    ? new Color(0.98f, 0.9f, 0.45f)
                    : new Color(0.95f, 0.85f, 0.55f);
            }

            UpdateUpcoming(stall, true);
            UpdateNotOpenWarning(stall, remain);
        }

        /// <summary>
        /// 夜晚：白天那一行換成「夜晚」+ 資本與目標。
        /// 能走到夜晚就代表天黑查帳已經過了（沒過會直接結束），但數字照樣寫出來，
        /// 讓玩家看到自己離目標多遠、明天要面對什麼。
        /// </summary>
        private void UpdateNight(StallManager stall)
        {
            int capital = stall.ProjectedCapital;   // 夜裡 = Capital（今天的收入天黑時已入帳）
            int target = stall.RoundTarget;
            bool passed = target <= 0 || capital >= target;

            string head = $"第 {stall.CurrentRound} 天　夜晚　資本 {Fmt(capital)}　{TargetText(target, stall.TodayIsBalloon)}";
            _targetLabel.text = passed ? $"{head}　已達標！" : $"{head}　還差 {Fmt(target - capital)}";
            _targetLabel.color = passed ? new Color(0.45f, 0.95f, 0.5f) : new Color(1f, 0.55f, 0.45f);

            if (_dayWarnLabel != null)
            {
                // 夜晚的指引是平靜的提示，不閃 —— 這不是警告，是「接下來該做什麼」
                _dayWarnLabel.text = "動物都睡了。去找發光的柱子收工。";
                _dayWarnLabel.color = new Color(0.78f, 0.85f, 1f, 0.95f);
            }
        }

        /// <summary>未來三天的目標，大額日用大額色。只在 run 模式的白天與夜晚顯示。</summary>
        private void UpdateUpcoming(StallManager stall, bool show)
        {
            if (_upcomingLabel == null) return;
            if (!show || !stall.RunMode)
            {
                _upcomingLabel.text = "";
                return;
            }

            var sb = new System.Text.StringBuilder();
            for (int i = 1; i <= 3; i++)
            {
                int day = stall.CurrentRound + i;
                string entry = $"第 {day} 天 {Fmt(Orders.CreditorSchedule.TargetFor(day))}";
                if (Orders.CreditorSchedule.IsBalloonDay(day)) entry = $"<color={BalloonHex}>{entry}</color>";
                if (i > 1) sb.Append("　");
                sb.Append(entry);
            }
            _upcomingLabel.text = sb.ToString();
        }

        /// <summary>「目標 XXX」。大額日整段換成大額色（rich text），不另外寫字。</summary>
        private static string TargetText(int target, bool balloon)
        {
            string t = $"目標 {Fmt(target)}";
            return balloon ? $"<color={BalloonHex}>{t}</color>" : t;
        }

        private static string Fmt(int n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        private static string Signed(int n) => n >= 0 ? $"+{Fmt(n)}" : $"-{Fmt(-n)}";

        /// <summary>
        /// 天快黑了還沒開張：紅字閃爍。**這是新玩家最容易犯的錯** ——
        /// 採集採到忘記時間，時間到才發現一件都沒賣。
        /// 讀的全是同步狀態（State、LevelDirector 的計時），所以每個 client 都看得到。
        /// </summary>
        private void UpdateNotOpenWarning(StallManager stall, float remain)
        {
            if (_dayWarnLabel == null) return;

            bool warn = stall.State != StallState.Open && remain < GameTuning.DayNotOpenWarnSeconds;
            if (!warn)
            {
                _dayWarnLabel.text = "";
                return;
            }

            int secs = Mathf.CeilToInt(remain);
            _dayWarnLabel.text = $"還沒開張！天黑前 {secs} 秒 —— 快去擺攤敲鈴";

            // 一秒閃兩次；Time.unscaledTime 讓暫停選單開著時也不會卡在某一個亮度
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f);
            var c = new Color(1f, 0.3f, 0.25f, Mathf.Lerp(0.35f, 1f, pulse));
            _dayWarnLabel.color = c;
        }

        /// <summary>
        /// 只顯示狀態，不提供操作 —— 開張要走過去敲襯布邊上的鈴鐺。
        /// </summary>
        private void UpdateOpenHint(StallManager stall)
        {
            if (!stall.IsArrangeMode)
            {
                _openHintLabel.text = "";
                return;
            }

            bool openable = stall.CanOpenForBusiness(out string reason);

            if (!openable && !string.IsNullOrEmpty(reason))
            {
                _openHintLabel.text = $"還不能開張：{reason}";
                _openHintLabel.color = new Color(1f, 0.7f, 0.5f);
                return;
            }

            _openHintLabel.text = IsHost(stall)
                ? "準備好了 — 去敲襯布旁邊的鈴鐺開張"
                : "準備好了 — 等房主敲鈴開張";
            _openHintLabel.color = new Color(0.6f, 1f, 0.7f);
        }

        private void UpdateNotice()
        {
            if (_noticeTimer > 0f)
            {
                _noticeTimer -= Time.deltaTime;
                var c = _noticeLabel.color;
                c.a = Mathf.Clamp01(_noticeTimer / 0.6f);
                _noticeLabel.color = c;
            }
            else if (_noticeLabel.text.Length > 0)
            {
                _noticeLabel.text = "";
            }
        }

        private static (string, Color) Describe(StallManager stall)
        {
            var agent = LocalAgent();
            if (agent != null && agent.HasPending)
                return ($"搬動 {StallCatalog.DisplayName(agent.PendingType)}",
                        new Color(0.45f, 0.30f, 0.10f, 0.9f));

            return stall.State switch
            {
                StallState.Open     => ("營業中", new Color(0.10f, 0.30f, 0.45f, 0.9f)),
                StallState.Settling => ("結算中", new Color(0.25f, 0.20f, 0.35f, 0.9f)),
                StallState.RunOver  => ("這一局結束", new Color(0.42f, 0.14f, 0.16f, 0.92f)),
                StallState.Night    => ("夜晚", new Color(0.10f, 0.12f, 0.30f, 0.92f)),
                _ when stall.IsArrangeMode => ("佈置模式", new Color(0.40f, 0.28f, 0.08f, 0.9f)),
                _                   => ("探索中", new Color(0.12f, 0.13f, 0.16f, 0.85f)),
            };
        }

        private static PlayerStallAgent LocalAgent()
        {
            var p = PlayerController.Local;
            return p != null ? p.GetComponent<PlayerStallAgent>() : null;
        }
    }
}
