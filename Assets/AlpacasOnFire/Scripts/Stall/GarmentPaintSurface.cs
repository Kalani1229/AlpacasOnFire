using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 掛在人偶上的「可塗抹表面」。
    ///
    /// 玩家拿著染劑罐按住滑鼠右鍵、對著畫面中央的準心刷，刷到哪裡哪裡就變成顏料的顏色；
    /// 塗滿一定比例之後，整件衣服就算是那個顏色（訂單比對用的是這個結果）。
    ///
    /// 同步方式：**同步一張低解析度的遮罩，不同步貼圖。**
    /// 貼圖沒辦法塞進網路狀態；只傳筆觸的話，中途加入的玩家會看到一件沒塗過的衣服。
    /// 所以這裡把 32x32 的遮罩壓成 32 個 int 同步，各端再從遮罩即時生成 Texture2D。
    /// 玩家體感仍然是「刷到哪裡變哪裡」，只是邊緣是像素塊。
    ///
    /// v1 一次只塗一種顏色（換色就洗掉重來）。之後要做雙色衣服，
    /// 把位元遮罩換成「每格一個顏色索引」即可，對外的 API 不用動。
    /// </summary>
    public class GarmentPaintSurface : NetworkBehaviour
    {
        /// <summary>遮罩解析度。覺得太粗就往上調，記得 MaskInts 會跟著變。</summary>
        public const int Resolution = 32;
        public const int CellCount = Resolution * Resolution;
        public const int MaskInts = CellCount / 32;   // 一個 int 裝 32 格

        [Header("Paint Surface")]
        [Tooltip("被塗抹的那個 Renderer（人偶身上的衣服）。")]
        [SerializeField] private Renderer _garmentRenderer;
        [Tooltip("塗抹判定用的碰撞體。留空會自動抓同物件上的。")]
        [SerializeField] private Collider _paintCollider;

        [Header("UV 對應")]
        [Tooltip("貼圖的水平方向跟本地 +X 相反時打勾。")]
        [SerializeField] private bool _flipU = true;
        [Tooltip("貼圖的垂直方向跟本地 +Y 相反時打勾。")]
        [SerializeField] private bool _flipV = true;

        [Networked, Capacity(MaskInts)] public NetworkArray<int> Mask { get; }
        [Networked] public int PaintColorRaw { get; set; }
        [Networked] public int PaintedCells { get; set; }
        /// <summary>每塗一筆就 +1，用戶端靠它判斷要不要重建貼圖。</summary>
        [Networked] public int MaskVersion { get; set; }

        private Texture2D _texture;
        private Color32[] _pixels;
        private MaterialPropertyBlock _mpb;
        private int _renderedVersion = -1;
        private int _renderedBaseColor = -1;

        public Renderer GarmentRenderer => _garmentRenderer;
        public Collider PaintCollider => _paintCollider;
        public DyeColorType PaintColor => (DyeColorType)PaintColorRaw;
        public float Coverage01 => PaintedCells / (float)CellCount;

        /// <summary>目前有沒有東西可以塗（人偶身上有衣服）。</summary>
        public bool Active => _garmentRenderer != null && _garmentRenderer.enabled;

        private void Awake()
        {
            if (_paintCollider == null) _paintCollider = GetComponent<Collider>();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_texture != null) Destroy(_texture);
            _texture = null;
        }

        // ---------------- 塗抹 ----------------

        /// <summary>
        /// 把世界座標的一個命中點換算成這個表面的 UV。
        ///
        /// 衣服是一片「寬 x 薄」的長方體，所以用**平面投影**（寬 x 高），不用圓柱投影。
        /// 這樣有個剛好想要的性質：正面和背面的同一個位置會得到**同一組 UV**，
        /// 也就是圖案會「透過去」—— 從背面看就是正面圖案的鏡像，跟一片薄布一樣。
        ///
        /// 最後的 _flipU / _flipV 是在對齊**網格自己的 UV 佈局**：
        /// 這裡算出來的是「以玩家視角看，螢幕右 = 本地 +X、螢幕上 = 本地 +Y」，
        /// 但貼圖真正貼上去的方向是網格 UV 決定的 —— Unity 內建 Cube 在我們塗的那一面
        /// 剛好整個轉了 180 度，不補正的話就會變成「指左邊、塗右邊」。
        /// 換成正式的衣服模型時，這兩個開關大概要重調。
        /// </summary>
        public bool TryWorldToUv(Vector3 worldPoint, out Vector2 uv)
        {
            uv = default;
            if (_garmentRenderer == null) return false;

            var local = _garmentRenderer.transform.InverseTransformPoint(worldPoint);
            var bounds = _garmentRenderer.localBounds;

            float width = Mathf.Max(0.0001f, bounds.size.x);
            float height = Mathf.Max(0.0001f, bounds.size.y);

            float u = Mathf.Clamp01((local.x - bounds.min.x) / width);
            float v = Mathf.Clamp01((local.y - bounds.min.y) / height);

            if (_flipU) u = 1f - u;
            if (_flipV) v = 1f - v;

            uv = new Vector2(u, v);
            return true;
        }

        /// <summary>塗一筆。只在 StateAuthority 呼叫。回傳有沒有真的塗到新的格子。</summary>
        public bool PaintAt(Vector2 uv, DyeColorType color, float radiusUv)
        {
            if (!HasStateAuthority || !Active) return false;

            // v1 一次只能塗一種顏色，換色就洗掉重來
            if (PaintedCells > 0 && PaintColorRaw != (int)color) ClearMask();
            PaintColorRaw = (int)color;

            int rCells = Mathf.Max(1, Mathf.RoundToInt(radiusUv * Resolution));
            int cx = Mathf.RoundToInt(uv.x * (Resolution - 1));
            int cy = Mathf.RoundToInt(uv.y * (Resolution - 1));

            int added = 0;
            for (int dy = -rCells; dy <= rCells; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= Resolution) continue;                 // v 不繞回去

                for (int dx = -rCells; dx <= rCells; dx++)
                {
                    if (dx * dx + dy * dy > rCells * rCells) continue;  // 圓形筆刷
                    int x = cx + dx;
                    if (x < 0 || x >= Resolution) continue;            // 平面投影，u 不繞回去

                    if (SetCell(x, y)) added++;
                }
            }

            if (added <= 0) return false;

            PaintedCells += added;
            MaskVersion++;
            return true;
        }

        public void ClearMask()
        {
            if (!HasStateAuthority) return;
            for (int i = 0; i < MaskInts; i++) Mask.Set(i, 0);
            PaintedCells = 0;
            MaskVersion++;
        }

        private bool SetCell(int x, int y)
        {
            int index = y * Resolution + x;
            int word = index >> 5;
            int bit = 1 << (index & 31);

            int value = Mask[word];
            if ((value & bit) != 0) return false;

            Mask.Set(word, value | bit);
            return true;
        }

        private bool GetCell(int index)
        {
            int word = index >> 5;
            int bit = 1 << (index & 31);
            return (Mask[word] & bit) != 0;
        }

        // ---------------- 貼圖 ----------------

        public override void Render()
        {
            if (_garmentRenderer == null) return;

            if (!Active)
            {
                _renderedVersion = -1;
                return;
            }

            int baseKey = BaseColorKey();
            if (_renderedVersion == MaskVersion && _renderedBaseColor == baseKey) return;

            _renderedVersion = MaskVersion;
            _renderedBaseColor = baseKey;
            RebuildTexture();
        }

        /// <summary>衣服本身的顏色由外面（人偶）決定，這裡只負責把它跟塗抹結果合成。</summary>
        public Color BaseColor { get; set; } = Color.white;

        private int BaseColorKey()
            => ((int)(BaseColor.r * 255) << 16) | ((int)(BaseColor.g * 255) << 8) | (int)(BaseColor.b * 255);

        private void RebuildTexture()
        {
            if (_texture == null)
            {
                _texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapModeU = TextureWrapMode.Clamp,
                    wrapModeV = TextureWrapMode.Clamp,
                };
                _pixels = new Color32[CellCount];
            }

            Color32 base32 = BaseColor;
            Color32 paint32 = PlaceholderPalette.Dye(PaintColor);

            for (int i = 0; i < CellCount; i++)
                _pixels[i] = GetCell(i) ? paint32 : base32;

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);

            _mpb ??= new MaterialPropertyBlock();
            _garmentRenderer.GetPropertyBlock(_mpb);
            _mpb.SetTexture("_BaseMap", _texture);
            _mpb.SetTexture("_MainTex", _texture);
            _mpb.SetColor("_BaseColor", Color.white);   // 顏色全部交給貼圖，避免兩套系統打架
            _mpb.SetColor("_Color", Color.white);
            _garmentRenderer.SetPropertyBlock(_mpb);
        }
    }
}
