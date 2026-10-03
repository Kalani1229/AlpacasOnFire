using System;
using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Stall;
using AlpacasOnFire.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AlpacasOnFire.Casino
{
    /// <summary>
    /// 刮刮樂面板（純本機）。
    ///
    /// **這裡沒有任何經濟責任。** 錢在主機抽完的那一刻就全部結清了（CasinoState），
    /// 這支收到的結果只拿來播演出 —— 關面板、斷線、崩潰都不會吃掉獎金。
    ///
    /// 流程：
    ///   選卡  十張有編號的卡攤開，已被買走的是空位、買不起的灰掉；可多選，顯示總價
    ///   等待  送出「我要買哪幾張」，等主機回結果
    ///   飄走  沒買的往上飄走淡出，買到的疊到右邊
    ///   刮    一張一張移到中間，拖曳滑鼠刮開，刮到 40% 自動揭曉；刮完的收到下面一排
    ///   總結  花了多少、拿回多少
    ///
    /// Esc／關閉：還有沒刮的就**先全部揭曉並顯示總結**（不要讓玩家以為獎金沒了），再按一次才關。
    /// </summary>
    public class ScratchCardPanel : MinigamePanel
    {
        public static ScratchCardPanel Instance { get; private set; }

        /// <summary>
        /// 一張卡揭曉了（卡號, 獎金）。**彩帶的掛載點** —— 之後做彩帶時訂閱這個就好，面板不用改。
        /// </summary>
        public static event Action<int, int> OnCardRevealed;

        private const int Count = CasinoState.ScratchCardCount;

        private const float CardW = 118f, CardH = 168f, Gap = 14f;
        private const float BigScale = 2.6f;
        private const float StripScale = 0.55f;

        private enum Phase { Select, Waiting, Animating, Scratch, Summary }

        // ---------------- 卡片 ----------------

        private sealed class CardView
        {
            public int Index;
            public RectTransform Rt;
            public CanvasGroup Group;
            public Image Bg;
            public Outline Frame;   // 選中時的黃框（用 Outline 效果，子物件的話會蓋住卡面）
            public Text Number;
            public Text Result;
            public RawImage Cover;
            public Text CoverHint;
            public Button Btn;

            public Texture2D Tex;
            public Color32[] Pixels;
            public int Cleared;

            public bool Selected;
            public bool Bought;
            public bool Revealed;
            public int Tier;
        }

        private readonly CardView[] _cards = new CardView[Count];

        // ---------------- 其他 UI ----------------

        private RectTransform _cardsRoot;
        private Text _capitalLabel;
        private Text _totalLabel;
        private Text _hintLabel;
        private Text _summaryLabel;
        private Button _buyButton;
        private Button _autoButton;
        private Button _againButton;
        private Button _closeButton;

        // ---------------- 狀態 ----------------

        private Phase _phase;
        private readonly List<CardView> _queue = new();   // 待刮（疊在右邊）
        private int _revealedCount;
        private CardView _current;
        private bool _auto;
        private float _autoT;
        private float _nextAt = -1f;      // 下一張上場的時間（揭曉之後停一下讓人看結果）
        private Vector2? _lastScratch;
        private int _spent, _won;
        private float _waitingSince;

        // ---------------- 補間（不用 Timeline，Lerp 就夠） ----------------

        private sealed class Tween
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public Vector2 FromPos, ToPos;
            public float FromScale, ToScale, FromAlpha, ToAlpha;
            public float Delay, Duration, T;
        }

        private readonly List<Tween> _tweens = new();

        // =====================================================================

        public static void Open(ScratchCardCounter counter)
        {
            var panel = EnsurePanel<ScratchCardPanel>();
            panel.OpenFor(counter);
        }

        protected override void Awake()
        {
            Instance = this;
            base.Awake();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this) Instance = null;
            foreach (var c in _cards) if (c?.Tex != null) Destroy(c.Tex);
        }

        // ---------------------------------------------------------------- 版面

        protected override void Build(RectTransform frame)
        {
            UIFactory.Label("Title", frame, $"刮刮樂　一張 ${GameTuning.ScratchCardPrice}　頭獎 {TopPrize()}", 36,
                TextAnchor.UpperCenter, new Color(1f, 0.85f, 0.35f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -22f), new Vector2(1000f, 48f));

            _capitalLabel = UIFactory.Label("Capital", frame, "", 22, TextAnchor.UpperLeft,
                new Color(0.95f, 0.85f, 0.55f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(28f, -28f), new Vector2(500f, 30f));

            _cardsRoot = UIFactory.Rect("Cards", frame,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            for (int i = 0; i < Count; i++) _cards[i] = BuildCard(i);

            _totalLabel = UIFactory.Label("Total", frame, "", 26, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 150f), new Vector2(900f, 36f));

            _hintLabel = UIFactory.Label("Hint", frame, "", 20, TextAnchor.MiddleCenter, new Color(0.75f, 0.8f, 0.9f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -76f), new Vector2(1000f, 28f));

            _summaryLabel = UIFactory.Label("Summary", frame, "", 40, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 60f), new Vector2(1100f, 130f));

            _buyButton = UIFactory.TextButton("Buy", frame, "購買", Vector2.zero, new Vector2(240f, 58f), Buy);
            Place(_buyButton, new Vector2(0f, 80f), bottom: true);

            _autoButton = UIFactory.TextButton("Auto", frame, "自動刮開", Vector2.zero, new Vector2(220f, 54f),
                () => { _auto = true; _autoButton.interactable = false; });
            Place(_autoButton, new Vector2(500f, 80f), bottom: true);

            _againButton = UIFactory.TextButton("Again", frame, "再買幾張", Vector2.zero, new Vector2(220f, 54f), BackToSelect);
            Place(_againButton, new Vector2(-130f, 80f), bottom: true);

            _closeButton = UIFactory.TextButton("Close", frame, "離開 (Esc)", Vector2.zero, new Vector2(200f, 50f), OnEscape);
            Place(_closeButton, new Vector2(-28f, -26f), bottom: false, right: true);
        }

        private static void Place(Button b, Vector2 pos, bool bottom, bool right = false)
        {
            var rt = (RectTransform)b.transform;
            var a = new Vector2(right ? 1f : 0.5f, bottom ? 0f : 1f);
            rt.anchorMin = a;
            rt.anchorMax = a;
            rt.pivot = a;
            rt.anchoredPosition = pos;
        }

        private CardView BuildCard(int index)
        {
            var bg = UIFactory.Panel($"Card{index + 1}", _cardsRoot, new Color(0.95f, 0.92f, 0.82f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(CardW, CardH));
            var rt = bg.rectTransform;
            var group = bg.gameObject.AddComponent<CanvasGroup>();

            // 選中時的外框
            var frame = bg.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(1f, 0.85f, 0.2f, 1f);
            frame.effectDistance = new Vector2(6f, -6f);
            frame.enabled = false;

            var number = UIFactory.Label("Number", rt, (index + 1).ToString(), 26, TextAnchor.UpperCenter,
                new Color(0.25f, 0.2f, 0.15f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(CardW, 32f));

            var coverSize = new Vector2(CardW - 14f, CardH - 46f);
            var result = UIFactory.Label("Result", rt, "", 22, TextAnchor.MiddleCenter, Color.black,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -14f), coverSize);

            var coverRt = UIFactory.Rect("Cover", rt,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -14f), coverSize);
            var cover = coverRt.gameObject.AddComponent<RawImage>();
            cover.raycastTarget = false;

            var hint = UIFactory.Label("CoverHint", coverRt, "刮開", 18, TextAnchor.MiddleCenter,
                new Color(0.35f, 0.35f, 0.4f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var btn = bg.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            int captured = index;
            btn.onClick.AddListener(() => ToggleSelect(captured));

            return new CardView
            {
                Index = index, Rt = rt, Group = group, Bg = bg, Frame = frame, Number = number,
                Result = result, Cover = cover, CoverHint = hint, Btn = btn,
            };
        }

        private static Vector2 SpreadPos(int i) => new Vector2((i - (Count - 1) * 0.5f) * (CardW + Gap), 10f);
        private static Vector2 StackPos(int k) => new Vector2(560f + k * 9f, 10f - k * 6f);
        private static readonly Vector2 CenterPos = new Vector2(-40f, 10f);
        private static Vector2 StripPos(int j) => new Vector2(-560f + j * (CardW * StripScale + 10f), -275f);

        private static int TopPrize()
        {
            var m = GameTuning.ScratchPrizeMultipliers;
            return GameTuning.ScratchCardPrice * m[m.Length - 1];
        }

        // ---------------------------------------------------------------- 開關

        protected override void OnOpened() => BackToSelect();

        protected override void OnEscape()
        {
            // 還有沒刮的：先全部揭曉並顯示總結，不直接關（玩家要看到獎金還在）
            if (HasUnrevealed())
            {
                RevealAllInstant();
                ShowSummary();
                return;
            }
            Close();
        }

        protected override void OnClosing()
        {
            // 被迫關閉（天亮、機台不見）時還有沒刮的：直接揭曉，讓大獎廣播照樣送出去
            if (HasUnrevealed()) RevealAllInstant();
            _tweens.Clear();
        }

        private bool HasUnrevealed()
        {
            foreach (var c in _cards) if (c.Bought && !c.Revealed) return true;
            return false;
        }

        // ---------------------------------------------------------------- 選卡

        private void BackToSelect()
        {
            _phase = Phase.Select;
            _tweens.Clear();
            _queue.Clear();
            _current = null;
            _revealedCount = 0;
            _auto = false;
            _nextAt = -1f;
            _spent = _won = 0;

            foreach (var c in _cards)
            {
                c.Selected = false;
                c.Bought = false;
                c.Revealed = false;
                c.Tier = 0;
                c.Rt.anchoredPosition = SpreadPos(c.Index);
                c.Rt.localScale = Vector3.one;
                c.Group.alpha = 1f;
                c.Rt.gameObject.SetActive(true);
                c.Cover.gameObject.SetActive(false);
                c.Result.text = "";
            }

            _summaryLabel.text = "";
            _hintLabel.text = "點選要買的卡（可以多選），再按「購買」";
            SetButtons(buy: true, auto: false, again: false);
            RefreshSelect();
        }

        private void ToggleSelect(int index)
        {
            if (_phase != Phase.Select) return;
            var c = _cards[index];
            if (!c.Btn.interactable) return;
            c.Selected = !c.Selected;
            RefreshSelect();
        }

        /// <summary>每幀重整：牌可能被隊友買走、資本可能變動。買不起的灰掉，選不了。</summary>
        private void RefreshSelect()
        {
            var casino = CasinoState.Instance;
            var stall = StallManager.Instance;
            int capital = stall != null && stall.Object != null && stall.Object.IsValid ? stall.Capital : 0;
            int price = GameTuning.ScratchCardPrice;

            int selected = 0;
            foreach (var c in _cards)
            {
                if (casino != null && casino.IsCardTaken(c.Index)) c.Selected = false;   // 被別人買走了
                if (c.Selected) selected++;
            }

            foreach (var c in _cards)
            {
                bool taken = casino == null || casino.IsCardTaken(c.Index);
                bool affordable = c.Selected || (selected + 1) * price <= capital;

                if (taken)
                {
                    // 空位：淡淡的框，沒有號碼
                    c.Bg.color = new Color(1f, 1f, 1f, 0.06f);
                    c.Number.text = "";
                    c.Btn.interactable = false;
                }
                else
                {
                    c.Number.text = (c.Index + 1).ToString();
                    c.Bg.color = affordable ? new Color(0.95f, 0.92f, 0.82f) : new Color(0.38f, 0.38f, 0.4f);
                    c.Number.color = affordable ? new Color(0.25f, 0.2f, 0.15f) : new Color(0.6f, 0.6f, 0.62f);
                    c.Btn.interactable = affordable;
                }

                c.Frame.enabled = c.Selected;
            }

            int left = casino != null ? casino.CardsLeft : 0;
            _totalLabel.text = left == 0
                ? "今天賣完了"
                : selected > 0 ? $"已選 {selected} 張　共 ${selected * price}" : $"剩 {left} 張";
            _buyButton.interactable = selected > 0;
        }

        private void Buy()
        {
            if (_phase != Phase.Select) return;
            var casino = CasinoState.Instance;
            if (casino == null) return;

            int mask = 0;
            foreach (var c in _cards) if (c.Selected) mask |= 1 << c.Index;
            if (mask == 0) return;

            _phase = Phase.Waiting;
            _waitingSince = Time.unscaledTime;
            _buyButton.interactable = false;
            _hintLabel.text = "購買中…";
            foreach (var c in _cards) c.Btn.interactable = false;

            // 只送「我要買哪幾張」—— 結果由主機抽，錢由主機結清
            casino.RPC_BuyScratchCards(mask);
        }

        // ---------------------------------------------------------------- 主機回覆

        /// <summary>主機抽完、錢也結清了。這裡只負責演出。</summary>
        public void OnPurchaseResult(int mask, int packedTiers, int spent, int won)
        {
            if (!IsOpen || _phase != Phase.Waiting) return;

            _spent = spent;
            _won = won;
            _phase = Phase.Animating;
            _hintLabel.text = "";
            _totalLabel.text = "";
            SetButtons(buy: false, auto: false, again: false);

            int k = 0;
            foreach (var c in _cards)
            {
                c.Btn.interactable = false;
                c.Frame.enabled = false;

                bool bought = (mask & (1 << c.Index)) != 0;
                c.Bought = bought;

                if (!bought)
                {
                    // 沒買的（含空位）往上飄走並淡出
                    AddTween(c, c.Rt.anchoredPosition + new Vector2(0f, 280f), 1f, 0f, 0f, 0.6f);
                    continue;
                }

                c.Tier = CasinoState.UnpackTier(packedTiers, c.Index);
                c.Result.text = ResultText(c.Tier);
                c.Result.color = ResultColor(c.Tier);
                PrepareCover(c);

                // 買到的疊到右邊，依序一張一張過去
                AddTween(c, StackPos(k), 1f, 1f, 0.1f + k * 0.06f, 0.4f);
                c.Rt.SetAsLastSibling();
                _queue.Add(c);
                k++;
            }

            // 疊好之後第一張上場
            _nextAt = Time.unscaledTime + 0.75f + k * 0.06f;
        }

        public void OnPurchaseRejected(string reason)
        {
            if (!IsOpen || _phase != Phase.Waiting) return;
            StallManager.LocalNotice(reason);
            _phase = Phase.Select;
            _hintLabel.text = "點選要買的卡（可以多選），再按「購買」";
            RefreshSelect();
        }

        // ---------------------------------------------------------------- 刮

        private void NextCard()
        {
            _nextAt = -1f;

            if (_queue.Count == 0)
            {
                ShowSummary();
                return;
            }

            // 疊在最上面的是最後放上去的；從最上面拿（看起來就是從右邊那疊拿一張）
            var c = _queue[_queue.Count - 1];
            _queue.RemoveAt(_queue.Count - 1);
            _current = c;
            _autoT = 0f;
            _lastScratch = null;

            c.Rt.SetAsLastSibling();
            AddTween(c, CenterPos, BigScale, 1f, 0f, 0.3f);

            _phase = Phase.Scratch;
            _hintLabel.text = "按住滑鼠左鍵拖曳刮開";
            SetButtons(buy: false, auto: !_auto, again: false);
        }

        private void PrepareCover(CardView c)
        {
            var size = ((RectTransform)c.Cover.transform).rect.size;
            int w = Mathf.Max(8, Mathf.RoundToInt(size.x));
            int h = Mathf.Max(8, Mathf.RoundToInt(size.y));

            if (c.Tex == null || c.Tex.width != w || c.Tex.height != h)
            {
                if (c.Tex != null) Destroy(c.Tex);
                c.Tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                c.Pixels = new Color32[w * h];
            }

            // 銀色刮層，加一點條紋才看得出是「一層東西」
            for (int y = 0; y < h; y++)
            {
                byte shade = (byte)(((y / 4) & 1) == 0 ? 178 : 168);
                for (int x = 0; x < w; x++)
                    c.Pixels[y * w + x] = new Color32(shade, (byte)(shade + 2), (byte)(shade + 10), 255);
            }
            c.Tex.SetPixels32(c.Pixels);
            c.Tex.Apply(false);

            c.Cleared = 0;
            c.Cover.texture = c.Tex;
            c.Cover.color = Color.white;
            c.Cover.gameObject.SetActive(true);
            c.CoverHint.gameObject.SetActive(true);
        }

        /// <summary>從 a 刮到 b（貼圖座標），中間補點，不然滑鼠快的時候會斷成一顆一顆。</summary>
        private void ScratchLine(CardView c, Vector2 a, Vector2 b, float radius)
        {
            float dist = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / (radius * 0.5f)));
            bool changed = false;
            for (int s = 0; s <= steps; s++)
                changed |= ScratchDot(c, Vector2.Lerp(a, b, s / (float)steps), radius);

            if (!changed) return;
            c.Tex.SetPixels32(c.Pixels);
            c.Tex.Apply(false);
            if (c.CoverHint.gameObject.activeSelf) c.CoverHint.gameObject.SetActive(false);
        }

        private bool ScratchDot(CardView c, Vector2 p, float radius)
        {
            int w = c.Tex.width, h = c.Tex.height;
            int r = Mathf.CeilToInt(radius);
            int cx = Mathf.RoundToInt(p.x), cy = Mathf.RoundToInt(p.y);
            float r2 = radius * radius;
            bool changed = false;

            for (int y = Mathf.Max(0, cy - r); y <= Mathf.Min(h - 1, cy + r); y++)
            {
                for (int x = Mathf.Max(0, cx - r); x <= Mathf.Min(w - 1, cx + r); x++)
                {
                    float dx = x - p.x, dy = y - p.y;
                    if (dx * dx + dy * dy > r2) continue;
                    int i = y * w + x;
                    if (c.Pixels[i].a == 0) continue;
                    c.Pixels[i].a = 0;
                    c.Cleared++;
                    changed = true;
                }
            }
            return changed;
        }

        private float ClearedFraction(CardView c)
            => c.Pixels == null || c.Pixels.Length == 0 ? 1f : c.Cleared / (float)c.Pixels.Length;

        private void TickScratch()
        {
            var c = _current;
            if (c == null || c.Revealed) return;
            if (IsTweening(c.Rt)) return;   // 還在飛到中間

            if (_auto)
            {
                // 自動：由上往下一排一排刮，快但看得到過程
                _autoT += Time.unscaledDeltaTime / 0.45f;
                int h = c.Tex.height, w = c.Tex.width;
                float y = h - _autoT * h * 1.2f;
                ScratchLine(c, new Vector2(0f, y), new Vector2(w, y), 11f);
            }
            else
            {
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.isPressed)
                {
                    var screen = mouse.position.ReadValue();
                    var coverRt = (RectTransform)c.Cover.transform;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(coverRt, screen, null, out var local))
                    {
                        var rect = coverRt.rect;
                        var tex = new Vector2((local.x - rect.x) / rect.width * c.Tex.width,
                                              (local.y - rect.y) / rect.height * c.Tex.height);
                        bool inside = local.x >= rect.xMin - 6f && local.x <= rect.xMax + 6f
                                   && local.y >= rect.yMin - 6f && local.y <= rect.yMax + 6f;
                        if (inside)
                        {
                            ScratchLine(c, _lastScratch ?? tex, tex, 8f);
                            _lastScratch = tex;
                        }
                        else _lastScratch = null;
                    }
                }
                else _lastScratch = null;
            }

            if (ClearedFraction(c) >= GameTuning.ScratchRevealFraction) Reveal(c, animateAway: true);
        }

        private void Reveal(CardView c, bool animateAway)
        {
            if (c.Revealed) return;
            c.Revealed = true;
            _revealedCount++;

            c.Cover.gameObject.SetActive(false);   // 到 40% 就全部揭曉

            int amount = GameTuning.ScratchCardPrice * GameTuning.ScratchPrizeMultipliers[c.Tier];

            // 告訴主機「我刮開了這張」——大獎廣播在這時候才發，免得一買就劇透自己
            CasinoState.Instance?.RPC_ScratchRevealed(c.Index);
            OnCardRevealed?.Invoke(c.Index, amount);
            if (amount > 0) GameAudio.Play(SfxId.Pickup);

            if (!animateAway) return;

            // 停一下讓人看結果，再收到下面一排，下一張上場
            float hold = _auto ? 0.45f : 0.8f;
            var target = StripPos(_revealedCount - 1);
            AddTween(c, target, StripScale, 1f, hold, 0.3f, from: CenterPos, fromScale: BigScale);
            _nextAt = Time.unscaledTime + hold + 0.1f;
        }

        private void RevealAllInstant()
        {
            _tweens.Clear();
            var all = new List<CardView>();
            if (_current != null && _current.Bought) all.Add(_current);
            all.AddRange(_queue);
            foreach (var c in _cards) if (c.Bought && !all.Contains(c)) all.Add(c);

            int j = 0;
            foreach (var c in all)
            {
                Reveal(c, animateAway: false);
                c.Rt.anchoredPosition = StripPos(j++);
                c.Rt.localScale = Vector3.one * StripScale;
                c.Group.alpha = 1f;
            }
            foreach (var c in _cards)
            {
                if (c.Bought) continue;
                c.Group.alpha = 0f;   // 沒買的就不要再出現了
            }

            _queue.Clear();
            _current = null;
            _nextAt = -1f;
        }

        private void ShowSummary()
        {
            _phase = Phase.Summary;
            _current = null;

            int net = _won - _spent;
            string netText = net > 0 ? $"<color=#7CF08A>賺 {net}</color>"
                           : net < 0 ? $"<color=#FF8A7A>虧 {-net}</color>"
                           : "打平";
            _summaryLabel.text = $"花了 ${_spent}，拿回 ${_won}\n{netText}";
            _hintLabel.text = "";

            var casino = CasinoState.Instance;
            bool more = casino != null && casino.CardsLeft > 0;
            SetButtons(buy: false, auto: false, again: more);
        }

        // ---------------------------------------------------------------- 每幀

        protected override void Update()
        {
            base.Update();
            if (!IsOpen) return;

            // 資本顯示也凍結（跟 HUD 一樣，不在刮開前劇透）
            _capitalLabel.text = CapitalFrozen ? $"資本 ${FrozenCapital:N0}" : "";

            TickTweens();

            switch (_phase)
            {
                case Phase.Select:
                    RefreshSelect();
                    break;

                case Phase.Waiting:
                    // 主機沒回（極少見）：五秒後放回選卡，錢的事主機那邊自己是一致的
                    if (Time.unscaledTime - _waitingSince > 5f)
                    {
                        StallManager.LocalNotice("沒有回應，再試一次");
                        _phase = Phase.Select;
                        RefreshSelect();
                    }
                    break;

                case Phase.Animating:
                case Phase.Scratch:
                    if (_nextAt > 0f && Time.unscaledTime >= _nextAt) NextCard();
                    else TickScratch();
                    break;
            }
        }

        // ---------------------------------------------------------------- 補間

        private void AddTween(CardView c, Vector2 to, float toScale, float toAlpha, float delay, float duration,
                              Vector2? from = null, float? fromScale = null)
        {
            _tweens.RemoveAll(t => t.Rt == c.Rt);
            _tweens.Add(new Tween
            {
                Rt = c.Rt, Group = c.Group,
                FromPos = from ?? c.Rt.anchoredPosition, ToPos = to,
                FromScale = fromScale ?? c.Rt.localScale.x, ToScale = toScale,
                FromAlpha = c.Group.alpha, ToAlpha = toAlpha,
                Delay = delay, Duration = Mathf.Max(0.01f, duration),
            });
        }

        private bool IsTweening(RectTransform rt)
        {
            foreach (var t in _tweens) if (t.Rt == rt) return true;
            return false;
        }

        private void TickTweens()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = _tweens.Count - 1; i >= 0; i--)
            {
                var t = _tweens[i];
                if (t.Delay > 0f) { t.Delay -= dt; continue; }

                t.T = Mathf.Min(1f, t.T + dt / t.Duration);
                float k = Mathf.SmoothStep(0f, 1f, t.T);
                t.Rt.anchoredPosition = Vector2.Lerp(t.FromPos, t.ToPos, k);
                t.Rt.localScale = Vector3.one * Mathf.Lerp(t.FromScale, t.ToScale, k);
                t.Group.alpha = Mathf.Lerp(t.FromAlpha, t.ToAlpha, k);

                if (t.T >= 1f) _tweens.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- 小工具

        private void SetButtons(bool buy, bool auto, bool again)
        {
            _buyButton.gameObject.SetActive(buy);
            _autoButton.gameObject.SetActive(auto);
            _autoButton.interactable = auto;
            _againButton.gameObject.SetActive(again);
        }

        private static string ResultText(int tier)
        {
            int mult = GameTuning.ScratchPrizeMultipliers[tier];
            int amount = GameTuning.ScratchCardPrice * mult;
            if (mult == 0) return "沒中";
            if (mult == 1) return $"回本\n${amount}";
            if (tier == GameTuning.ScratchPrizeMultipliers.Length - 1) return $"頭獎\n${amount}!!";
            if (mult >= GameTuning.ScratchBroadcastMultiplier) return $"${amount}!";
            return $"${amount}";
        }

        private static Color ResultColor(int tier)
        {
            int mult = GameTuning.ScratchPrizeMultipliers[tier];
            if (mult == 0) return new Color(0.45f, 0.45f, 0.48f);
            if (mult == 1) return new Color(0.2f, 0.2f, 0.25f);
            if (mult >= GameTuning.ScratchBroadcastMultiplier) return new Color(0.85f, 0.45f, 0f);
            return new Color(0.1f, 0.55f, 0.2f);
        }
    }
}
