using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 放置預覽的幽靈模型。純本機視覺，不同步。
    ///
    /// 用簡單的半透明方塊（加一根朝向指示）而不是實際機台 prefab 的複製品：
    /// 佔位美術本來就是方塊，視覺落差極小，卻可以完全避開
    /// 「在本機 Instantiate 一個帶 NetworkObject 的 prefab」會踩到的 Fusion 陷阱。
    /// </summary>
    public class PlacementGhost : MonoBehaviour
    {
        private static readonly Color InvalidColor = new Color(0.95f, 0.20f, 0.18f, 0.55f);

        private Transform _box;
        private Transform _nose;
        private Renderer _boxRenderer;
        private Renderer _noseRenderer;
        private MaterialPropertyBlock _mpb;
        private Material _material;

        public static PlacementGhost Create()
        {
            var go = new GameObject("[PlacementGhost]");
            var ghost = go.AddComponent<PlacementGhost>();
            ghost.Build();
            return ghost;
        }

        private void Build()
        {
            _material = MakeTransparentMaterial();

            _box = MakePiece("Box", transform, _material);
            _nose = MakePiece("Facing", transform, _material);

            _boxRenderer = _box.GetComponent<Renderer>();
            _noseRenderer = _nose.GetComponent<Renderer>();
        }

        private static Transform MakePiece(string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);

            // 幽靈模型絕對不能有碰撞體：它會擋住準心的互動射線與攝影機碰撞
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = mat;
            return go.transform;
        }

        private static Material MakeTransparentMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var m = new Material(shader) { name = "M_PlacementGhost_Runtime" };
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return m;
        }

        /// <summary>更新幽靈模型。不合法時整個轉紅。</summary>
        public void Apply(LevelElementType type, Vector3 position, float yaw, bool valid)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            transform.position = position;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var size = StallCatalog.GhostSize(type);
            _box.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            _box.localScale = size;

            // 朝向指示：往前伸出一小塊，讓玩家看得出旋轉了幾度
            _nose.localPosition = new Vector3(0f, size.y * 0.5f, size.z * 0.5f + 0.18f);
            _nose.localScale = new Vector3(size.x * 0.35f, size.y * 0.35f, 0.34f);

            var baseColor = StallCatalog.GhostColor(type);
            baseColor.a = 0.5f;
            var color = valid ? baseColor : InvalidColor;

            _mpb ??= new MaterialPropertyBlock();
            Tint(_boxRenderer, color);
            Tint(_noseRenderer, valid ? new Color(1f, 1f, 1f, 0.75f) : InvalidColor);
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

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
