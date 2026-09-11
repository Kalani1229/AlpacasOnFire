using UnityEngine;

namespace AlpacasOnFire.Core
{
    /// <summary>
    /// 讓一條會伸縮的量條**從固定的一端長出來**。
    ///
    /// 為什麼要有這個東西：專案裡的量條都是 Cube 縮放出來的，而 Cube 的軸心在中心，
    /// 所以 `localScale.x = t` 是從中間往兩邊撐開、也從兩邊往中間縮。進度條看起來
    /// 像在「收合」而不是「前進」，剩餘量條則像浮在半空。
    ///
    /// 另外一個更隱蔽的問題：直接把 localScale 設成比例（0~1）等於**丟掉 prefab 上的
    /// 設計尺寸**。設計寬 0.78 的條子，滿的時候反而變成 1.0、突出容器外。
    ///
    /// 這支兩件事一起解決：
    ///   - 第一次使用時記下 prefab 的原始大小與位置，之後一律以它為基準乘比例
    ///   - 縮放後把位置補回去，讓固定的那一端不動
    ///
    /// 用法：宣告成一個欄位（每條量條一個），每幀呼叫 Apply()。
    /// <code>
    /// private readonly BarAnchor _bar = new(BarAnchor.Axis.X);
    /// ...
    /// _bar.Apply(_progressBar, Progress01);
    /// </code>
    /// </summary>
    public sealed class BarAnchor
    {
        /// <summary>沿哪個軸伸縮。X 從左端長到右端，Y 從底部長上來。</summary>
        public enum Axis { X, Y }

        private readonly Axis _axis;

        private Vector3 _baseScale;
        private Vector3 _basePos;
        private bool _cached;

        public BarAnchor(Axis axis) => _axis = axis;

        /// <summary>
        /// 把量條畫到比例 t（0~1）。
        ///
        /// min 是最短長度：完全歸零的話 Unity 會把它縮成一個看不見的點，
        /// 留一點點才看得出「這裡有一條條子，只是空了」。
        /// </summary>
        public void Apply(Transform bar, float t, float min = 0.02f)
        {
            if (bar == null) return;

            // 基準一定要在第一次縮放**之前**抓，而且只抓一次 ——
            // 讀到被自己改過的值就等於基準跑掉，條子會一次比一次短。
            if (!_cached)
            {
                _baseScale = bar.localScale;
                _basePos = bar.localPosition;
                _cached = true;
            }

            var scale = _baseScale;
            var pos = _basePos;
            t = Mathf.Clamp01(t);

            if (_axis == Axis.X)
            {
                float w = Mathf.Max(min, _baseScale.x * t);
                scale.x = w;
                pos.x = _basePos.x - _baseScale.x * 0.5f + w * 0.5f;   // 左端固定
            }
            else
            {
                float h = Mathf.Max(min, _baseScale.y * t);
                scale.y = h;
                pos.y = _basePos.y - _baseScale.y * 0.5f + h * 0.5f;   // 底部固定
            }

            bar.localScale = scale;
            bar.localPosition = pos;
        }
    }
}
