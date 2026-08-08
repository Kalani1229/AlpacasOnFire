using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Machines;
using UnityEngine;

namespace AlpacasOnFire.Level
{
    /// <summary>
    /// 把 LevelDefinition 變成場景物件。
    /// Phase 1 由 Editor 工具在編輯期呼叫（機台是 Fusion 的場景物件，必須在執行前就存在）；
    /// Phase 3 的多關卡系統可以沿用同一份程式碼改成執行期生成。
    /// </summary>
    public static class LevelBuilder
    {
        public const string RootName = "[Level]";

        /// <summary>
        /// 依 record 生成一個元件（不做 Undo 記錄，Editor 端自行包）。
        ///
        /// catalogOverride：編輯期請一定要傳。編輯期用 Resources.Load 取 Catalog 並不可靠
        /// （資產剛建立、尚未被匯入時會回 null），Editor 端改成用 AssetDatabase 直接載入後傳進來。
        /// </summary>
        public static GameObject Instantiate(LevelElementRecord rec, Transform parent,
                                             System.Func<GameObject, GameObject> instantiateFunc = null,
                                             GameCatalog catalogOverride = null)
        {
            var catalog = catalogOverride != null ? catalogOverride : GameCatalog.Instance;
            if (catalog == null)
            {
                Debug.LogError("[LevelBuilder] 取不到 GameCatalog，無法生成任何元件。");
                return null;
            }

            var prefab = catalog.GetElement(rec.type);
            if (prefab == null)
            {
                Debug.LogWarning($"[LevelBuilder] Catalog 裡沒有 {rec.type} 的 prefab，略過。");
                return null;
            }

            var go = instantiateFunc != null ? instantiateFunc(prefab) : Object.Instantiate(prefab);
            if (go == null)
            {
                Debug.LogError($"[LevelBuilder] 生成 {rec.type} 失敗（prefab = {prefab.name}）。");
                return null;
            }

            go.name = $"{rec.type}_{rec.id}";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = rec.position;
            go.transform.localRotation = Quaternion.Euler(rec.rotationEuler);
            go.transform.localScale = rec.scale == Vector3.zero ? Vector3.one : rec.scale;

            var tag = go.GetComponent<LevelElementTag>();
            if (tag == null) tag = go.AddComponent<LevelElementTag>();
            tag.recordId = rec.recordIdSafe();
            tag.type = rec.type;
            tag.variant = rec.variant;
            tag.intParam = rec.intParam;

            ApplyVariant(go, rec);
            return go;
        }

        private static void ApplyVariant(GameObject go, LevelElementRecord rec)
        {
            // 染料點顏色：variant 沒填的話，從元件類型推導
            var dye = go.GetComponent<DyeSourceNode>();
            if (dye != null)
            {
                var color = rec.type switch
                {
                    LevelElementType.DyeSourceBlue   => DyeColorType.Blue,
                    LevelElementType.DyeSourceGreen  => DyeColorType.Green,
                    LevelElementType.DyeSourceYellow => DyeColorType.Yellow,
                    _                                => DyeColorType.Red,
                };
                if (!string.IsNullOrEmpty(rec.variant))
                    System.Enum.TryParse(rec.variant, out color);

                new SerializedFieldSetter(dye).SetEnum("_color", (int)color);
            }

            // 飾品取得點種類
            var acc = go.GetComponent<AccessoryDispenser>();
            if (acc != null && !string.IsNullOrEmpty(rec.variant)
                && System.Enum.TryParse<AccessoryType>(rec.variant, out var a))
            {
                new SerializedFieldSetter(acc).SetEnum("_accessory", (int)a);
            }
        }

        /// <summary>用反射設定私有序列化欄位（只在編輯期與建置流程使用）。</summary>
        private readonly struct SerializedFieldSetter
        {
            private readonly object _target;
            public SerializedFieldSetter(object target) => _target = target;

            public void SetEnum(string fieldName, int value)
            {
                var f = _target.GetType().GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (f == null) return;
                f.SetValue(_target, System.Enum.ToObject(f.FieldType, value));
            }
        }

        public static List<LevelElementTag> FindPlaced(Transform root)
        {
            var list = new List<LevelElementTag>();
            if (root == null) return list;
            root.GetComponentsInChildren(true, list);
            return list;
        }
    }

    internal static class LevelRecordExtensions
    {
        public static string recordIdSafe(this LevelElementRecord rec)
            => string.IsNullOrEmpty(rec.id) ? rec.id = System.Guid.NewGuid().ToString("N")[..8] : rec.id;
    }
}
