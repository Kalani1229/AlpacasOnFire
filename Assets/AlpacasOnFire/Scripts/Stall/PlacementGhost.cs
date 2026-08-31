using AlpacasOnFire.Core;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 放置預覽的幽靈模型 + 目標格高亮。純本機視覺，不同步。
    ///
    /// 三個部件：
    ///  - CellHighlight：貼在格子上的方塊，佔地多大就多大（合法綠 / 不合法紅）
    ///  - Box：裝備本體的外框
    ///  - Facing：朝正面（+Z）伸出去的一小塊，讓玩家看得出朝向轉到哪
    ///
    /// 用簡單方塊而不是實際機台 prefab 的複製品：佔位美術本來就是方塊，
    /// 視覺落差極小，卻可以完全避開「在本機 Instantiate 帶 NetworkObject 的 prefab」的陷阱。
    /// </summary>
    public class PlacementGhost : MonoBehaviour
    {
        private static readonly Color InvalidColor   = new Color(0.95f, 0.20f, 0.18f, 0.55f);
        private static readonly Color ValidCellColor = new Color(0.35f, 0.95f, 0.45f, 0.40f);
        private static readonly Color InvalidCell    = new Color(0.95f, 0.20f, 0.18f, 0.40f);

        private Transform _cell;
        private Transform _box;
        private Transform _nose;
        private Renderer _cellRenderer;
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

            _cell = MakePiece("CellHighlight", transform, _material);
            _box  = MakePiece("Box", transform, _material);
            _nose = MakePiece("Facing", transform, _material);

            _cellRenderer = _cell.GetComponent<Renderer>();
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

        /// <summary>
        /// 更新幽靈模型。
        /// </summary>
        /// <param name="cellCenter">目標格中心的世界座標（已經吸附到格子）。</param>
        /// <param name="rotation">依朝向索引算出來的旋轉。</param>
        /// <param name="cellsW">旋轉後佔幾格寬。</param>
        /// <param name="cellsD">旋轉後佔幾格深。</param>
        /// <param name="valid">合不合法。不合法一律轉紅。</param>
        public void Apply(LevelElementType type, Vector3 cellCenter, Quaternion rotation,
                          int cellsW, int cellsD, bool valid)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            transform.position = cellCenter;
            transform.rotation = rotation;

            float cell = GameTuning.StallCellSize;

            // 目標格高亮：整塊佔地都要亮起來，一眼看得出會吃掉哪幾格
            _cell.localPosition = new Vector3(0f, 0.03f, 0f);
            _cell.localScale = new Vector3(cell * cellsW * 0.96f, 0.03f, cell * cellsD * 0.96f);

            var size = StallCatalog.GhostSize(type);
            _box.localPosition = new Vector3(0f, size.y * 0.5f, 0f);
            _box.localScale = size;

            // 朝向指示：往正面（+Z）伸出一小塊
            _nose.localPosition = new Vector3(0f, size.y * 0.5f, size.z * 0.5f + 0.18f);
            _nose.localScale = new Vector3(size.x * 0.35f, size.y * 0.35f, 0.34f);

            var baseColor = StallCatalog.GhostColor(type);
            baseColor.a = 0.5f;

            _mpb ??= new MaterialPropertyBlock();
            Tint(_cellRenderer, valid ? ValidCellColor : InvalidCell);
            Tint(_boxRenderer, valid ? baseColor : InvalidColor);
            Tint(_noseRenderer, valid ? new Color(1f, 1f, 1f, 0.8f) : InvalidColor);
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
