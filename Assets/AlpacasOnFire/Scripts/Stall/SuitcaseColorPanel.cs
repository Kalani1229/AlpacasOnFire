using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Player;
using AlpacasOnFire.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 選色面板：這一場要帶哪三種毛。佈置模式對手提箱按右鍵打開。
    ///
    /// **為什麼這裡用面板而不是循環式互動**：選三色需要同時看到五色的背包存量
    /// 才決定得了（我紅毛夠嗎？綠毛只剩 2 份還要選嗎？），循環式的介面看不到全貌。
    /// 之前拿掉的是「裝備選單」—— 那是因為裝備開箱就全在場上、沒有東西要選；
    /// 這是不同的情況。
    ///
    /// 做法照抄 PatternSelectPanel 已驗證的模式：只在觸發互動的那個用戶端打開、
    /// 開啟時解鎖游標、選完走 RPC 回報給狀態權威。
    /// </summary>
    public class SuitcaseColorPanel : MonoBehaviour
    {
        public static SuitcaseColorPanel Instance { get; private set; }

        /// <summary>
        /// 顯示順序：便宜的在左邊。
        ///
        /// **沒有白色。** 白毛（羊駝毛）是玩家互相剃出來的現場產物，
        /// 剃下來就掉在地上、直接送織布機，不經過背包也不需要素材箱。
        /// 給它一格反而會壞掉：素材箱是開張時鎖定的，而互剃是營業中一直在做的事。
        ///
        /// 副作用是三格要從四色裡挑，取捨比從五色裡挑更緊，這是好事。
        /// </summary>
        private static readonly DyeColorType[] Colors =
        {
            DyeColorType.Yellow,
            DyeColorType.Green,
            DyeColorType.Blue,
            DyeColorType.Red,
        };

        private class Swatch
        {
            public Button Button;
            public Image Background;
            public Image Frame;
            public Text Label;
            public DyeColorType Color;
        }

        private readonly List<Swatch> _swatches = new();
        private GameObject _root;
        private SuitcaseItem _suitcase;
        private Text _countLabel;
        private Text _hintLabel;

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
            var panel = UIFactory.Panel("SuitcaseColorPanel", transform,
                new Color(0.05f, 0.06f, 0.08f, 0.94f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(760f, 420f));
            _root = panel.gameObject;

            UIFactory.Label("Title", panel.transform, "這一場要帶哪三種毛", 32,
                TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -22f), new Vector2(700f, 44f));

            _countLabel = UIFactory.Label("Count", panel.transform, "", 20,
                TextAnchor.UpperCenter, new Color(0.75f, 0.82f, 0.9f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -62f), new Vector2(700f, 28f));

            // 五個色塊排一列
            const float w = 128f, h = 150f, gap = 12f;
            float rowWidth = Colors.Length * w + (Colors.Length - 1) * gap;

            for (int i = 0; i < Colors.Length; i++)
            {
                var colour = Colors[i];
                float x = -rowWidth * 0.5f + w * 0.5f + i * (w + gap);

                // 外框（選中時亮起來）
                var frame = UIFactory.Panel($"Frame_{colour}", panel.transform,
                    new Color(1f, 1f, 1f, 0f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(x, 10f), new Vector2(w + 10f, h + 10f));
                frame.raycastTarget = false;

                var btn = UIFactory.TextButton($"Color_{colour}", panel.transform, "",
                    new Vector2(x, 10f), new Vector2(w, h), () => Toggle(colour));

                var bg = btn.GetComponent<Image>();
                bg.color = PlaceholderPalette.Dye(colour);

                var label = btn.GetComponentInChildren<Text>();
                label.color = Color.black;
                label.fontSize = 22;

                _swatches.Add(new Swatch
                {
                    Button = btn,
                    Background = bg,
                    Frame = frame,
                    Label = label,
                    Color = colour,
                });
            }

            _hintLabel = UIFactory.Label("Hint", panel.transform,
                "開張時會把選中顏色的**全部**存量帶走；收攤時沒用完的退回背包",
                18, TextAnchor.LowerCenter, new Color(0.72f, 0.78f, 0.86f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 84f), new Vector2(720f, 26f));

            UIFactory.TextButton("Close", panel.transform, "關閉 (Esc)",
                new Vector2(0f, -160f), new Vector2(220f, 50f), Close);

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

        private void Toggle(DyeColorType colour)
        {
            if (_suitcase == null || _suitcase.Object == null) return;
            _suitcase.RPC_ToggleColor((int)colour);
            // 不在這裡改本機狀態 —— 等 [Networked] 同步回來再更新畫面，
            // 免得預測跟權威不一致時畫面閃爍
        }

        // ---------------- 更新 ----------------

        private void Refresh()
        {
            var stash = TeamStash.Instance;
            var suitcase = _suitcase;
            if (suitcase == null) return;

            int selected = suitcase.SelectedCount;
            bool full = selected >= SuitcaseItem.ColorSlots;

            _countLabel.text = $"已選 {selected} / {SuitcaseItem.ColorSlots}";

            foreach (var s in _swatches)
            {
                int stock = stash != null ? stash.Count(s.Color) : 0;
                bool isSelected = suitcase.IsSelected(s.Color);
                bool selectable = isSelected || (!full && stock > 0);

                s.Label.text = stock > 0
                    ? $"{PlaceholderPalette.DyeName(s.Color)}\n{stock} 份"
                    : $"{PlaceholderPalette.DyeName(s.Color)}\n沒有";

                var baseColour = PlaceholderPalette.Dye(s.Color);
                s.Background.color = stock > 0 ? baseColour : baseColour * 0.3f;
                s.Label.color = stock > 0 ? Color.black : new Color(1f, 1f, 1f, 0.5f);

                // 選中的加一圈白框
                s.Frame.color = isSelected ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0f);
                s.Button.interactable = selectable && !suitcase.Loaded;
            }

            if (_hintLabel != null)
            {
                _hintLabel.text = suitcase.Loaded
                    ? "已經開張，材料鎖定了"
                    : selected == 0
                        ? "至少要選一種顏色才能開張"
                        : "開張時會把選中顏色的全部存量帶走；收攤時沒用完的退回背包";
            }
        }

        private void Update()
        {
            if (!IsOpen) return;

            // 背包存量與選擇狀態都可能被隊友改動，每幀重整最單純
            Refresh();

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();

            // 攤位被收掉、或箱子沒了就自動關閉
            if (_suitcase == null || _suitcase.Object == null) { Close(); return; }

            var stall = StallManager.Instance;
            if (stall == null || !stall.IsArrangeMode) Close();
        }
    }
}
