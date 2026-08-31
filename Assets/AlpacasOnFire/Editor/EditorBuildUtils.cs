using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>建置佔位資產時共用的小工具。</summary>
    public static class EditorBuildUtils
    {
        public const string Root         = "Assets/AlpacasOnFire";
        public const string MaterialsDir = Root + "/Materials";
        public const string PrefabsDir   = Root + "/Prefabs";
        public const string LevelsDir    = Root + "/Levels";
        public const string ScenesDir    = Root + "/Scenes";
        public const string ResourcesDir = Root + "/Resources";

        public static void EnsureFolders()
        {
            EnsureFolder(Root);
            EnsureFolder(MaterialsDir);
            EnsureFolder(PrefabsDir);
            EnsureFolder(LevelsDir);
            EnsureFolder(ScenesDir);
            EnsureFolder(ResourcesDir);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        public static Shader LitShader
        {
            get
            {
                var s = Shader.Find("Universal Render Pipeline/Lit");
                if (s == null) s = Shader.Find("Standard");
                return s;
            }
        }

        private static readonly Dictionary<string, Material> MatCache = new();

        public static void ResetCaches() => MatCache.Clear();

        public static Material Mat(string name, Color color, float smoothness = 0.2f)
        {
            if (MatCache.TryGetValue(name, out var cached) && cached != null) return cached;

            string path = $"{MaterialsDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(LitShader);
                AssetDatabase.CreateAsset(mat, path);
            }
            MatCache[name] = mat;
            mat.shader = LitShader;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// URP Lit 的半透明材質。URP 的透明是靠一組屬性 + keyword + renderQueue 決定的，
        /// 不是改個 alpha 就好，所以集中在這裡設定一次。
        /// </summary>
        public static Material TransparentMat(string name, Color color, float alpha)
        {
            var mat = Mat(name, color);
            var c = color; c.a = alpha;

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);   // 1 = Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);       // Alpha blend
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static GameObject Empty(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }

        public static GameObject Prim(PrimitiveType type, string name, Transform parent,
                                      Vector3 localPos, Vector3 localScale, Material mat,
                                      bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            var col = go.GetComponent<Collider>();
            if (!keepCollider && col != null) Object.DestroyImmediate(col);

            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
            return go;
        }

        /// <summary>把私有 [SerializeField] 欄位接起來。</summary>
        public static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[Builder] {target.GetType().Name} 找不到欄位 {field}"); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetRefArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[Builder] {target.GetType().Name} 找不到欄位 {field}"); return; }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetEnum(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[Builder] {target.GetType().Name} 找不到欄位 {field}"); return; }
            p.enumValueIndex = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetInt(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static GameObject SavePrefab(GameObject go, string fileName)
        {
            string path = $"{PrefabsDir}/{fileName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out bool success);
            Object.DestroyImmediate(go);

            if (!success || prefab == null)
                throw new System.Exception($"儲存 prefab 失敗：{path}");

            return prefab;
        }

        /// <summary>建立專案 Layer（攝影機碰撞需要排除玩家與手上物品）。</summary>
        public static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return -1;

            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                var sp = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(sp.stringValue)) continue;
                sp.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return i;
            }
            Debug.LogWarning($"[Builder] 沒有空的 Layer 槽可以建立 {layerName}。");
            return -1;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0) return;
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }
    }
}
