using AlpacasOnFire.Core;
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
        private Text _noticeLabel;
        private Image _modeChip;

        private Text _openHintLabel;

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

            _noticeLabel = UIFactory.Label("StallNotice", transform, "", 28, TextAnchor.MiddleCenter,
                new Color(1f, 0.55f, 0.4f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -210f), new Vector2(1100f, 40f));

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
                if (_openHintLabel != null) _openHintLabel.text = "";
                return;
            }

            if (!_modeChip.gameObject.activeSelf) _modeChip.gameObject.SetActive(true);

            var (text, color) = Describe(stall);
            _modeLabel.text = text;
            _modeChip.color = color;

            _capitalLabel.text = stall.MatDeployed
                ? $"資本額 ${stall.Capital}　｜　攤位裝備 {stall.DeployedCount()} 台"
                : $"資本額 ${stall.Capital}";

            UpdateOpenHint(stall);
            UpdateNotice();
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
