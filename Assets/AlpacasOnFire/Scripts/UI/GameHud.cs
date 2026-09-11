using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
using AlpacasOnFire.Npc;
using AlpacasOnFire.Orders;
using AlpacasOnFire.Player;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.UI
{
    /// <summary>
    /// 遊戲內 HUD：訂單卡（含 Countdown Border）、剩餘時間、金額、
    /// 目前攜帶物、噴槍剩餘染劑量、互動提示。
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        private class OrderCard
        {
            public GameObject Root;
            public Image Background;
            public Image CountdownBorder;
            public Image Swatch;
            public Text Title;
            public Text Sub;
            public int BoundId = -1;
        }

        private readonly List<OrderCard> _cards = new();
        private RectTransform _orderArea;
        private Image _orderAreaFlash;
        private Text _timerLabel;
        private Text _moneyLabel;
        private Text _promptLabel;
        private Text _carryLabel;
        private Text _toastLabel;
        private Image _sprayBarBg;
        private Image _sprayBarFill;
        private Text _sprayLabel;

        private float _flashTimer;
        private float _toastTimer;
        private float _moneyPulse;

        private void Start()
        {
            BuildUI();
            OrderBoard.OnDeliveryResult += HandleDelivery;
            OrderBoard.OnOrderExpired += HandleExpired;

            // v6：Village 的成交／失敗走顧客系統，訂單板那兩個事件永遠不會觸發。
            // 沒有接這幾條的話，Village 裡交貨與顧客流失完全沒有畫面回饋。
            CustomerQueue.OnCustomerServed += HandleCustomerServed;
            CustomerQueue.OnCustomerLeft += HandleNoMatch;
            CustomerQueue.OnCustomerTimeout += HandleExpired;

            LevelDirector.OnMoneyChanged += HandleMoney;
        }

        private void OnDestroy()
        {
            OrderBoard.OnDeliveryResult -= HandleDelivery;
            OrderBoard.OnOrderExpired -= HandleExpired;
            CustomerQueue.OnCustomerServed -= HandleCustomerServed;
            CustomerQueue.OnCustomerLeft -= HandleNoMatch;
            CustomerQueue.OnCustomerTimeout -= HandleExpired;
            LevelDirector.OnMoneyChanged -= HandleMoney;
        }

        // ---------------- 建立 ----------------

        private void BuildUI()
        {
            var root = transform;

            // 準心
            // 準心：固定在畫面正中央的小白色半透明十字。
            // 底下墊一層深色描邊，這樣在白衣服、淺色地面上也看得見。
            BuildCrosshair(root);

            // 上方：時間與金額
            _timerLabel = UIFactory.Label("Timer", root, "03:00", 40, TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(300f, 50f));
            _moneyLabel = UIFactory.Label("Money", root, "$0", 32, TextAnchor.UpperCenter,
                new Color(0.98f, 0.85f, 0.35f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -68f), new Vector2(300f, 44f));

            // 右上：訂單區
            _orderArea = UIFactory.Rect("OrderArea", root,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-24f, -24f), new Vector2(220f, 620f));

            _orderAreaFlash = UIFactory.Panel("OrderAreaFlash", _orderArea, new Color(1f, 0.15f, 0.15f, 0f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f));
            _orderAreaFlash.raycastTarget = false;

            for (int i = 0; i < OrderBoard.Slots; i++)
                _cards.Add(BuildCard(i));

            // 下方中央：互動提示
            _promptLabel = UIFactory.Label("Prompt", root, "", 26, TextAnchor.LowerCenter, Color.white,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 120f), new Vector2(900f, 40f));

            // 左下：目前攜帶物
            _carryLabel = UIFactory.Label("Carry", root, "", 24, TextAnchor.LowerLeft,
                new Color(0.9f, 0.95f, 1f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(28f, 32f), new Vector2(600f, 36f));

            // 右下：噴槍染劑量
            _sprayBarBg = UIFactory.Panel("SprayBg", root, new Color(0f, 0f, 0f, 0.55f),
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-28f, 32f), new Vector2(260f, 26f));
            _sprayBarFill = UIFactory.Panel("SprayFill", _sprayBarBg.transform, Color.cyan,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(0f, 0f), new Vector2(260f, 0f));
            _sprayBarFill.rectTransform.anchoredPosition = Vector2.zero;
            _sprayLabel = UIFactory.Label("SprayLabel", root, "", 20, TextAnchor.LowerRight, Color.white,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-28f, 62f), new Vector2(300f, 26f));

            // 中央偏下：提示訊息（出貨結果、扣款）
            _toastLabel = UIFactory.Label("Toast", root, "", 30, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -160f), new Vector2(1000f, 40f));
        }

        /// <summary>畫面正中央的十字準星。塗抹就是對著這個點刷，所以要一眼看得到。</summary>
        private void BuildCrosshair(Transform root)
        {
            const float len = 13f;   // 十字的長度
            const float thick = 2f;  // 線寬
            const float outline = 1f;// 描邊厚度

            var mid = new Vector2(0.5f, 0.5f);
            var shadow = new Color(0f, 0f, 0f, 0.45f);
            var white = new Color(1f, 1f, 1f, 0.75f);

            // 先描邊、再白線（後建立的疊在上面）
            Bar("CrosshairOutlineH", shadow, new Vector2(len + outline * 2f, thick + outline * 2f));
            Bar("CrosshairOutlineV", shadow, new Vector2(thick + outline * 2f, len + outline * 2f));
            Bar("CrosshairH", white, new Vector2(len, thick));
            Bar("CrosshairV", white, new Vector2(thick, len));

            void Bar(string name, Color color, Vector2 size)
            {
                var img = UIFactory.Panel(name, root, color, mid, mid, mid, Vector2.zero, size);
                img.raycastTarget = false;
            }
        }

        private OrderCard BuildCard(int index)
        {
            const float h = 140f;
            var card = new OrderCard();

            var bg = UIFactory.Panel($"OrderCard{index}", _orderArea, new Color(0.12f, 0.13f, 0.16f, 0.92f),
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -index * (h + 12f)), new Vector2(0f, h));
            card.Root = bg.gameObject;
            card.Background = bg;

            // Countdown Border：Filled 影像沿著卡片邊緣退掉
            var border = UIFactory.Panel("CountdownBorder", bg.transform, new Color(0.3f, 0.9f, 0.4f, 1f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            border.sprite = UIFactory.WhiteSprite;
            border.type = Image.Type.Filled;
            border.fillMethod = Image.FillMethod.Horizontal;
            border.fillOrigin = (int)Image.OriginHorizontal.Left;
            border.fillAmount = 1f;
            border.raycastTarget = false;
            border.rectTransform.anchorMin = new Vector2(0f, 0f);
            border.rectTransform.anchorMax = new Vector2(1f, 0f);
            border.rectTransform.pivot = new Vector2(0.5f, 0f);
            border.rectTransform.sizeDelta = new Vector2(0f, 8f);
            border.rectTransform.anchoredPosition = Vector2.zero;
            card.CountdownBorder = border;

            card.Swatch = UIFactory.Panel("Swatch", bg.transform, Color.white,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -12f), new Vector2(40f, 40f));

            card.Title = UIFactory.Label("Title", bg.transform, "", 24, TextAnchor.UpperLeft, Color.white,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f),
                new Vector2(62f, -14f), new Vector2(-70f, 34f));

            card.Sub = UIFactory.Label("Sub", bg.transform, "", 20, TextAnchor.LowerLeft,
                new Color(0.8f, 0.85f, 0.9f),
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                new Vector2(12f, 16f), new Vector2(-24f, 46f));

            card.Root.SetActive(false);
            return card;
        }

        // ---------------- 每幀更新 ----------------

        private void Update()
        {
            UpdateOrders();
            UpdateStatus();
            UpdatePlayerInfo();
            UpdateFlashAndToast();
        }

        /// <summary>
        /// 訂單卡有兩個來源，看場上跑的是哪一套需求系統：
        ///   Village -> CustomerQueue（站在窗口外排隊的顧客）
        ///   Stall_Test -> OrderBoard（既有的訂單板）
        ///
        /// 判斷條件跟 DeliveryCounter.RouteDelivery() 與 OrderBoard.CustomersOwnDemand()
        /// **必須是同一個**，否則會出現「卡片上有、交過去卻說沒人要」的落差 ——
        /// 那正是先前白色訂單交不掉的那個 bug。
        /// </summary>
        private void UpdateOrders()
        {
            var queue = CustomerQueue.Instance;
            if (queue != null && queue.Object != null && queue.Object.IsValid)
                UpdateCustomerCards(queue);
            else
                UpdateOrderBoardCards();
        }

        private readonly List<Customer> _customerBuffer = new();

        private void UpdateCustomerCards(CustomerQueue queue)
        {
            queue.CollectActive(_customerBuffer);

            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];

                if (i >= _customerBuffer.Count)
                {
                    card.Root.SetActive(false);
                    card.BoundId = -1;
                    continue;
                }

                var c = _customerBuffer[i];

                // 用 NetworkId 當識別碼：同一位顧客在同一格的期間不重建文字。
                // 顧客是「被徵召的羊」，同一隻羊可以當好幾次顧客，但每次徵召之間
                // 一定會經過 Leave -> Release（Active 變 false），所以卡片會先被收掉、
                // 下一次再綁上來，不會殘留上一單的描述。
                int id = (int)c.Object.Id.Raw;

                card.Root.SetActive(true);
                if (card.BoundId != id)
                {
                    card.BoundId = id;
                    card.Title.text = c.Wanted.Describe();
                    card.Swatch.color = PlaceholderPalette.Dye(c.Wanted.Color);
                }

                SetCountdown(card, c.Patience01);
                card.Sub.text = $"${c.Price}　{c.PatienceRemaining:F0}s";
            }
        }

        private void UpdateOrderBoardCards()
        {
            var board = OrderBoard.Instance;
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (board == null || board.Object == null)
                {
                    card.Root.SetActive(false);
                    continue;
                }

                var e = board.Orders[i];
                if (!e.Active)
                {
                    card.Root.SetActive(false);
                    card.BoundId = -1;
                    continue;
                }

                card.Root.SetActive(true);
                if (card.BoundId != e.Id)
                {
                    card.BoundId = e.Id;
                    card.Title.text = e.Spec.Describe();
                    card.Swatch.color = PlaceholderPalette.Dye(e.Spec.Color);
                }

                SetCountdown(card, e.Remaining01);
                card.Sub.text = $"${e.Reward}　{e.Remaining:F0}s";
            }
        }

        /// <summary>倒數框：綠 -> 黃 -> 紅。兩個來源共用同一套配色，讀起來才一致。</summary>
        private static void SetCountdown(OrderCard card, float t)
        {
            card.CountdownBorder.fillAmount = t;
            card.CountdownBorder.color = t > 0.5f
                ? Color.Lerp(new Color(0.95f, 0.85f, 0.2f), new Color(0.3f, 0.9f, 0.4f), (t - 0.5f) * 2f)
                : Color.Lerp(new Color(0.95f, 0.25f, 0.2f), new Color(0.95f, 0.85f, 0.2f), t * 2f);
        }

        private void UpdateStatus()
        {
            var d = LevelDirector.Instance;
            if (d == null || d.Object == null)
            {
                _timerLabel.text = "--:--";
                return;
            }

            float remain = d.Running ? d.RemainingSeconds : 0f;
            _timerLabel.text = $"{Mathf.FloorToInt(remain / 60f):00}:{Mathf.FloorToInt(remain % 60f):00}";
            _timerLabel.color = remain <= 15f ? new Color(1f, 0.4f, 0.35f) : Color.white;

            _moneyLabel.text = $"${d.Money}";
            if (_moneyPulse > 0f)
            {
                _moneyPulse -= Time.deltaTime;
                _moneyLabel.transform.localScale = Vector3.one * (1f + 0.25f * Mathf.Clamp01(_moneyPulse / 0.3f));
            }
            else
            {
                _moneyLabel.transform.localScale = Vector3.one;
            }
        }

        private void UpdatePlayerInfo()
        {
            var p = PlayerController.Local;
            if (p == null || p.Object == null)
            {
                _promptLabel.text = "";
                _carryLabel.text = "";
                _sprayBarBg.gameObject.SetActive(false);
                _sprayLabel.text = "";
                return;
            }

            var held = p.Carry.Held;
            _carryLabel.text = held != null ? $"手上：{held.DisplayName}" : "手上：空";

            // 提示的判定順序**必須跟 PlayerController.HandlePrimaryPress 完全一致**，
            // 不然會出現「提示說可以丟、按下去卻不丟」的落差。
            //   接住 -> 互動 -> （手上有東西且面前空無一物）丟出 -> 什麼都不做
            string prompt = null;
            if (p.Carry.FindCatchable(GameTuning.CatchManualRadius, requireFacing: false) != null)
            {
                prompt = "[左鍵] 接住！";
            }
            else
            {
                var target = p.Interactor.FindTarget(out var ctx);
                if (target != null) prompt = target.GetPrompt(in ctx);

                // 只有「面前真的空無一物」才顯示丟出。面前有東西但現在不能用
                // （機台放滿了、成品還沒被拿走）要維持顯示該物件自己的拒絕提示，
                // 因為那種情況按左鍵不會丟。
                if (string.IsNullOrEmpty(prompt) && held != null && !p.Interactor.HasAnyTargetInRange())
                    prompt = $"[左鍵] 丟出 {held.DisplayName}";
            }
            _promptLabel.text = prompt ?? "";

            if (held is DyeCanisterTool canister)
            {
                _sprayBarBg.gameObject.SetActive(true);
                var fillRt = _sprayBarFill.rectTransform;
                fillRt.sizeDelta = new Vector2(256f * canister.Charge01, -4f);
                _sprayBarFill.color = canister.HasPaint
                    ? PlaceholderPalette.Dye(canister.Color)
                    : new Color(0.4f, 0.4f, 0.4f);
                _sprayLabel.text = $"顏料 {canister.Charge01 * 100f:F0}%（{PlaceholderPalette.DyeName(canister.Color)}）　[右鍵按住] 刷上去";
            }
            else
            {
                _sprayBarBg.gameObject.SetActive(false);
                _sprayLabel.text = "";
            }
        }

        private void UpdateFlashAndToast()
        {
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float a = Mathf.PingPong(_flashTimer * 6f, 1f) * 0.45f;
                _orderAreaFlash.color = new Color(1f, 0.15f, 0.15f, a);
            }
            else
            {
                _orderAreaFlash.color = new Color(1f, 0.15f, 0.15f, 0f);
            }

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.deltaTime;
                var c = _toastLabel.color;
                c.a = Mathf.Clamp01(_toastTimer / 0.6f);
                _toastLabel.color = c;
            }
            else
            {
                _toastLabel.text = "";
            }
        }

        // ---------------- 事件 ----------------

        private void HandleDelivery(bool success, string desc)
        {
            if (success)
            {
                Toast($"出貨成功：{desc}", new Color(0.4f, 1f, 0.5f));
            }
            else
            {
                Toast($"沒有符合的訂單：{desc}", new Color(1f, 0.4f, 0.35f));
                _flashTimer = 1.2f;   // 訂單區閃紅
            }
        }

        private void HandleExpired(string desc)
        {
            Toast($"訂單超時：{desc}", new Color(1f, 0.55f, 0.3f));
            _flashTimer = 0.8f;
        }

        /// <summary>顧客成交。價格寫進提示，玩家馬上看得到這一單值多少。</summary>
        private void HandleCustomerServed(int price, string desc)
        {
            Toast($"成交：{desc}　+${price}", new Color(0.4f, 1f, 0.5f));
        }

        /// <summary>交了沒人要的衣服。跟訂單板的失敗走同一套措辭與紅閃。</summary>
        private void HandleNoMatch(string desc) => HandleDelivery(false, desc);

        private void HandleMoney(int delta, string reason, bool negative)
        {
            _moneyPulse = 0.3f;
        }

        private void Toast(string message, Color color)
        {
            _toastLabel.text = message;
            _toastLabel.color = color;
            _toastTimer = 2.2f;
        }
    }
}
