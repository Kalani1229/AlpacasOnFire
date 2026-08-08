using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.UI
{
    /// <summary>
    /// 用程式建立 uGUI 的小工具。Phase 1 的 UI 全部在執行期組出來，
    /// 好處是不需要手動維護 prefab，之後要換成正式美術時再改成 prefab 即可。
    /// </summary>
    public static class UIFactory
    {
        private static Font _font;

        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                // 先找系統中文字型，找不到再退回 Unity 內建字型
                _font = Font.CreateDynamicFontFromOSFont(
                    new[]
                    {
                        "Microsoft JhengHei UI", "Microsoft JhengHei", "Microsoft YaHei",
                        "PingFang TC", "Heiti TC", "Noto Sans CJK TC", "Arial Unicode MS", "Arial"
                    }, 24);
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static RectTransform Rect(string name, Transform parent,
                                         Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                         Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Panel(string name, Transform parent, Color color,
                                  Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                  Vector2 anchoredPos, Vector2 size)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size,
                                 TextAnchor align, Color color,
                                 Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
                                 Vector2 anchoredPos, Vector2 rectSize)
        {
            var rt = Rect(name, parent, anchorMin, anchorMax, pivot, anchoredPos, rectSize);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.text = text;
            t.alignment = align;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button TextButton(string name, Transform parent, string text,
                                        Vector2 anchoredPos, Vector2 size,
                                        System.Action onClick, Transform parentOverride = null)
        {
            var img = Panel(name, parentOverride != null ? parentOverride : parent,
                            new Color(0.16f, 0.18f, 0.22f, 0.95f),
                            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            anchoredPos, size);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            btn.colors = colors;

            Label(name + "_Label", img.transform, text, 22, TextAnchor.MiddleCenter, Color.white,
                  Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>1x1 白色貼圖做的 Sprite，用來畫純色方塊與 Filled 邊框。</summary>
        public static Sprite WhiteSprite
        {
            get
            {
                if (_white != null) return _white;
                var tex = Texture2D.whiteTexture;
                _white = Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height),
                                       new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                return _white;
            }
        }
        private static Sprite _white;
    }
}
