using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 襯布的視覺。**不是網路物件** —— 位置與朝向都從 StallManager 的 [Networked] 狀態推導，
    /// 每個用戶端各自生成一份就好，不需要多一個 NetworkObject 佔頻寬。
    ///
    /// 佈置模式是暖色（可以擺東西），營業模式轉成冷色（機台鎖住了），一眼看得出模式。
    /// </summary>
    public class StallMatVisual : MonoBehaviour
    {
        private static readonly Color ArrangeColor  = new Color(0.95f, 0.78f, 0.35f, 0.30f);
        private static readonly Color BusinessColor = new Color(0.35f, 0.72f, 0.95f, 0.22f);
        private static readonly Color BorderArrange = new Color(0.98f, 0.85f, 0.45f, 0.95f);
        private static readonly Color BorderBusiness= new Color(0.45f, 0.85f, 1.00f, 0.95f);

        private const float BorderThickness = 0.12f;

        private Transform _surface;
        private readonly Transform[] _borders = new Transform[4];
        private MaterialPropertyBlock _mpb;
        private Renderer _surfaceRenderer;
        private readonly Renderer[] _borderRenderers = new Renderer[4];

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
            if (_surface != null) return;

            _surface = MakeQuadPiece("Surface", transform);
            for (int i = 0; i < 4; i++)
                _borders[i] = MakeQuadPiece($"Border{i}", transform);

            _surfaceRenderer = _surface.GetComponent<Renderer>();
            for (int i = 0; i < 4; i++) _borderRenderers[i] = _borders[i].GetComponent<Renderer>();
        }

        /// <summary>
        /// 用扁平的 Cube 而不是 Quad：Quad 只有單面，從側面看會消失；
        /// 扁 Cube 從任何角度都看得到，佔位階段這樣最省事。
        /// </summary>
        private static Transform MakeQuadPiece(string name, Transform parent)
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
                // URP 的透明模式開關
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

        public void Apply(Vector3 center, float yaw, float size, bool arrangeMode)
        {
            EnsureBuilt();
            if (!gameObject.activeSelf) gameObject.SetActive(true);

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
