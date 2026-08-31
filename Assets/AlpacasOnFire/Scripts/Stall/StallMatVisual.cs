using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 襯布的視覺。**不是網路物件** —— 位置與朝向都從 StallManager 的 [Networked] 狀態推導，
    /// 每個用戶端各自生成一份就好，不需要多一個 NetworkObject 佔頻寬。
    ///
    /// 佈置模式：暖色 + **顯示網格線**（看得出格子在哪）
    /// 營業模式：冷色 + 隱藏網格線（裝備鎖住了，格子沒有意義）
    /// </summary>
    public class StallMatVisual : MonoBehaviour
    {
        private static readonly Color ArrangeColor   = new Color(0.95f, 0.78f, 0.35f, 0.30f);
        private static readonly Color BusinessColor  = new Color(0.35f, 0.72f, 0.95f, 0.22f);
        private static readonly Color BorderArrange  = new Color(0.98f, 0.85f, 0.45f, 0.95f);
        private static readonly Color BorderBusiness = new Color(0.45f, 0.85f, 1.00f, 0.95f);
        private static readonly Color GridLineColor  = new Color(1.00f, 0.95f, 0.80f, 0.45f);

        private const float BorderThickness = 0.12f;

        private Transform _surface;
        private Transform _gridRoot;
        private readonly Transform[] _borders = new Transform[4];
        private Renderer _surfaceRenderer;
        private readonly Renderer[] _borderRenderers = new Renderer[4];
        private Renderer[] _gridRenderers;

        private MaterialPropertyBlock _mpb;
        private bool _built;

        public static StallMatVisual Create(GameObject prefabOverride)
        {
            if (prefabOverride != null)
            {
                var inst = Instantiate(prefabOverride);
                var comp = inst.GetComponent<StallMatVisual>();
                if (comp == null) comp = inst.AddComponent<StallMatVisual>();
                comp.EnsureBuilt();
                return comp;
            }

            var go = new GameObject("[StallMat]");
            var mat = go.AddComponent<StallMatVisual>();
            mat.EnsureBuilt();
            return mat;
        }

        private void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _surface = MakePiece("Surface", transform);
            _surfaceRenderer = _surface.GetComponent<Renderer>();

            for (int i = 0; i < 4; i++)
            {
                _borders[i] = MakePiece($"Border{i}", transform);
                _borderRenderers[i] = _borders[i].GetComponent<Renderer>();
            }

            BuildGridLines();
        }

        /// <summary>
        /// 網格線：每個方向 (格數 - 1) 條內線。外框由 Border 負責，不重複畫。
        /// 6 格 -> 每方向 5 條，共 10 條，全部是靜態的扁方塊，成本可以忽略。
        /// </summary>
        private void BuildGridLines()
        {
            int inner = StallGrid.Cells - 1;
            if (inner <= 0)
            {
                _gridRenderers = new Renderer[0];
                return;
            }

            var gridGo = new GameObject("GridLines");
            gridGo.transform.SetParent(transform, false);
            _gridRoot = gridGo.transform;

            _gridRenderers = new Renderer[inner * 2];

            float size = GameTuning.StallMatSize;
            float cell = GameTuning.StallCellSize;
            float width = GameTuning.StallGridLineWidth;
            float half = size * 0.5f;

            for (int i = 0; i < inner; i++)
            {
                float offset = -half + (i + 1) * cell;

                var vertical = MakePiece($"GridX{i}", _gridRoot);
                vertical.localPosition = new Vector3(offset, 0.015f, 0f);
                vertical.localScale = new Vector3(width, 0.03f, size);
                _gridRenderers[i] = vertical.GetComponent<Renderer>();

                var horizontal = MakePiece($"GridZ{i}", _gridRoot);
                horizontal.localPosition = new Vector3(0f, 0.015f, offset);
                horizontal.localScale = new Vector3(size, 0.03f, width);
                _gridRenderers[inner + i] = horizontal.GetComponent<Renderer>();
            }
        }

        /// <summary>
        /// 用扁平的 Cube 而不是 Quad：Quad 只有單面，從側面看會消失；
        /// 扁 Cube 從任何角度都看得到，佔位階段這樣最省事。
        /// </summary>
        private static Transform MakePiece(string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);

            var col = go.GetComponent<Collider>();
            // 襯布不能擋住互動射線與攝影機碰撞，一定要拿掉碰撞體
            if (col != null) Destroy(col);

            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = TransparentMaterial;
            return go.transform;
        }

        private static Material _transparentMaterial;

        /// <summary>URP Lit 的半透明設定。用共用材質，靠 MaterialPropertyBlock 分別上色。</summary>
        private static Material TransparentMaterial
        {
            get
            {
                if (_transparentMaterial != null) return _transparentMaterial;

                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");

                var m = new Material(shader) { name = "M_StallMat_Runtime" };
                m.SetFloat("_Surface", 1f);          // 1 = Transparent
                m.SetFloat("_Blend", 0f);            // 0 = Alpha
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHATEST_ON");

                _transparentMaterial = m;
                return m;
            }
        }

        public void Apply(Vector3 center, float yaw, bool arrangeMode)
        {
            EnsureBuilt();
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            float size = GameTuning.StallMatSize;

            transform.position = center + Vector3.up * GameTuning.StallMatHeightOffset;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            _surface.localPosition = Vector3.zero;
            _surface.localScale = new Vector3(size, 0.02f, size);

            float h = size * 0.5f;
            SetBorder(0, new Vector3(0f, 0.01f, -h), new Vector3(size, 0.04f, BorderThickness));
            SetBorder(1, new Vector3(0f, 0.01f,  h), new Vector3(size, 0.04f, BorderThickness));
            SetBorder(2, new Vector3(-h, 0.01f, 0f), new Vector3(BorderThickness, 0.04f, size));
            SetBorder(3, new Vector3( h, 0.01f, 0f), new Vector3(BorderThickness, 0.04f, size));

            _mpb ??= new MaterialPropertyBlock();
            Tint(_surfaceRenderer, arrangeMode ? ArrangeColor : BusinessColor);

            var borderColor = arrangeMode ? BorderArrange : BorderBusiness;
            for (int i = 0; i < 4; i++) Tint(_borderRenderers[i], borderColor);

            // 網格線只在佈置模式顯示
            if (_gridRoot != null && _gridRoot.gameObject.activeSelf != arrangeMode)
                _gridRoot.gameObject.SetActive(arrangeMode);

            if (arrangeMode && _gridRenderers != null)
                foreach (var r in _gridRenderers) Tint(r, GridLineColor);
        }

        private void SetBorder(int index, Vector3 localPos, Vector3 localScale)
        {
            _borders[index].localPosition = localPos;
            _borders[index].localScale = localScale;
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
