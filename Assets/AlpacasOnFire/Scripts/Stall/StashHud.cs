using AlpacasOnFire.Core;
using AlpacasOnFire.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 共用背包的 HUD：畫面左下角一排色塊 + 數字。
    ///
    /// 用既有的 UIFactory 做，沒有引進新的 UI 套件。
    /// 位置刻意排在 GameHud 的「手上：XXX」上方，不會疊到。
    ///
    /// 剃到毛的瞬間對應色塊會放大一下再縮回去 —— 玩家要能立刻確認「進帳了」，
    /// 不然剃毛的回饋只剩下 NPC 跑掉，會覺得沒拿到東西。
    /// </summary>
    public class StashHud : MonoBehaviour
    {
        /// <summary>
        /// 顯示順序。黃在最左邊（最便宜），紅在最右邊（最貴）。
        ///
        /// **沒有白色。** 白毛不進共同背包 —— 它是互相剃毛的現場產物，
        /// 剃下來就在地上。掛一個永遠是 0 的白色格子只會讓人以為壞了。
        /// </summary>
        private static readonly DyeColorType[] Order =
        {
            DyeColorType.Yellow,
            DyeColorType.Green,
            DyeColorType.Blue,
            DyeColorType.Red,
        };

        private class Slot
        {
            public Image Swatch;
            public Text Count;
            public RectTransform Root;
            public float Pulse;
        }

        private readonly Slot[] _slots = new Slot[Order.Length];
        private Text _title;

        private void Start()
        {
            Build();
            TeamStash.OnStashChanged += HandleChanged;
        }

        private void OnDestroy()
        {
            TeamStash.OnStashChanged -= HandleChanged;
        }

        private void Build()
        {
            const float slotW = 64f, slotH = 58f, gap = 6f;

            _title = UIFactory.Label("StashTitle", transform, "背包", 18, TextAnchor.LowerLeft,
                new Color(0.8f, 0.85f, 0.92f),
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(28f, 138f), new Vector2(200f, 24f));

            for (int i = 0; i < Order.Length; i++)
            {
                float x = 28f + i * (slotW + gap);

                var swatch = UIFactory.Panel($"Stash_{Order[i]}", transform,
                    PlaceholderPalette.Dye(Order[i]),
                    new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(x, 76f), new Vector2(slotW, slotH));
                swatch.raycastTarget = false;

                var count = UIFactory.Label($"StashCount_{Order[i]}", swatch.transform, "0",
                    24, TextAnchor.MiddleCenter, Color.black,
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

                _slots[i] = new Slot
                {
                    Swatch = swatch,
                    Count = count,
                    Root = swatch.rectTransform,
                };
            }
        }

        private void HandleChanged(DyeColorType color, int delta)
        {
            for (int i = 0; i < Order.Length; i++)
            {
                if (Order[i] != color) continue;
                _slots[i].Pulse = 0.35f;
                return;
            }
        }

        private void Update()
        {
            var stash = TeamStash.Instance;
            bool show = stash != null && stash.Object != null;

            if (_title != null) _title.enabled = show;

            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot?.Swatch == null) continue;

                if (slot.Swatch.gameObject.activeSelf != show)
                    slot.Swatch.gameObject.SetActive(show);
                if (!show) continue;

                int n = stash.Count(Order[i]);
                slot.Count.text = n.ToString();

                // 沒有存量的顏色壓暗，一眼看得出哪些做得出東西
                var c = PlaceholderPalette.Dye(Order[i]);
                slot.Swatch.color = n > 0 ? c : c * 0.35f;
                slot.Count.color = n > 0 ? Color.black : new Color(1f, 1f, 1f, 0.5f);

                if (slot.Pulse > 0f)
                {
                    slot.Pulse -= Time.deltaTime;
                    float t = Mathf.Clamp01(slot.Pulse / 0.35f);
                    slot.Root.localScale = Vector3.one * (1f + 0.3f * t);
                }
                else
                {
                    slot.Root.localScale = Vector3.one;
                }
            }
        }
    }
}
