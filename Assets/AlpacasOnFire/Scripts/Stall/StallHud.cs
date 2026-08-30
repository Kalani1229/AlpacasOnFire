using AlpacasOnFire.Core;
using AlpacasOnFire.Orders;
using AlpacasOnFire.Player;
using AlpacasOnFire.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 擺攤專用的 HUD 補充資訊：目前模式、資本額、已擺放數量、提示訊息。
    /// 既有的 GameHud（訂單卡、計時、金額、互動提示）完全沒有動，這裡只加不重疊的東西。
    /// </summary>
    public class StallHud : MonoBehaviour
    {
        private Text _modeLabel;
        private Text _capitalLabel;
        private Text _noticeLabel;
        private Image _modeChip;

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
        }

        private void HandleStateChanged(StallState state)
        {
            switch (state)
            {
                case StallState.Deploying: ShowNotice("攤位擺開了 — 對著手提箱按 Space 拿裝備"); break;
                case StallState.Open:      ShowNotice("開張！Space 恢復成操作機台"); break;
                case StallState.Exploring: break;
                case StallState.Settling:  break;
            }
        }

        private void ShowNotice(string message)
        {
            if (string.IsNullOrEmpty(message) || _noticeLabel == null) return;
            _noticeLabel.text = message;
            _noticeTimer = 2.4f;
        }

        private void Update()
        {
            var stall = StallManager.Instance;

            if (stall == null || stall.Object == null)
            {
                if (_modeChip != null) _modeChip.gameObject.SetActive(false);
                if (_capitalLabel != null) _capitalLabel.text = "";
                return;
            }

            if (!_modeChip.gameObject.activeSelf) _modeChip.gameObject.SetActive(true);

            var (text, color) = Describe(stall);
            _modeLabel.text = text;
            _modeChip.color = color;

            _capitalLabel.text = stall.MatDeployed
                ? $"資本額 ${stall.Capital}　｜　攤位機台 {stall.DeployedCount()} 台"
                : $"資本額 ${stall.Capital}";

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
                return ($"放置 {StallCatalog.DisplayName(agent.PendingType)}",
                        new Color(0.45f, 0.30f, 0.10f, 0.9f));

            return stall.State switch
            {
                StallState.Open      => ("營業中", new Color(0.10f, 0.30f, 0.45f, 0.9f)),
                StallState.Settling  => ("結算中", new Color(0.25f, 0.20f, 0.35f, 0.9f)),
                _ when stall.IsArrangeMode => ("佈置模式", new Color(0.40f, 0.28f, 0.08f, 0.9f)),
                _                    => ("探索中", new Color(0.12f, 0.13f, 0.16f, 0.85f)),
            };
        }

        private static PlayerStallAgent LocalAgent()
        {
            var p = PlayerController.Local;
            return p != null ? p.GetComponent<PlayerStallAgent>() : null;
        }
    }
}
