using System.Collections.Generic;
using System.IO;
using System.Linq;
using AlpacasOnFire.Core;
using AlpacasOnFire.Level;
using UnityEditor;
using UnityEngine;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 關卡編輯器（Unity Editor 內的開發者工具）。
    ///
    /// 可以做：設定關卡時間與通關分數、新增／刪除元件與 NPC、編輯位置與旋轉、
    /// 編輯出生點、存檔／讀檔（ScriptableObject 或 JSON）。
    ///
    /// 存檔格式是 LevelDefinition + List&lt;LevelElementRecord&gt;，
    /// Phase 3 的多關卡資料驅動系統會直接沿用。
    /// </summary>
    public class LevelEditorWindow : EditorWindow
    {
        private LevelDefinition _def;
        private Vector2 _scroll;
        private LevelElementType _newType = LevelElementType.SewingMachine;
        private bool _showSettings = true;
        private bool _showElements = true;
        private string _filter = "";

        [MenuItem("羊駝很忙/關卡編輯器 %#L", priority = 20)]
        public static void Open()
        {
            var w = GetWindow<LevelEditorWindow>("關卡編輯器");
            w.minSize = new Vector2(420f, 520f);
            w.Show();
        }

        private void OnGUI()
        {
            DrawHeader();
            if (_def == null)
            {
                EditorGUILayout.HelpBox("請選一份 LevelDefinition，或按下方按鈕載入內建的兩個關卡。", MessageType.Info);
                DrawQuickLoad();
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSettings();
            EditorGUILayout.Space(6);
            DrawElements();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            DrawSceneActions();
            DrawFileActions();
        }

        // ---------------------------------------------------------------- 版面

        private void DrawHeader()
        {
            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            _def = (LevelDefinition)EditorGUILayout.ObjectField("關卡資料", _def, typeof(LevelDefinition), false);
            if (EditorGUI.EndChangeCheck()) Repaint();
        }

        private void DrawQuickLoad()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("載入 測試關卡"))
                _def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelSceneBuilder.TestLevelAsset);
            if (GUILayout.Button("載入 第一關"))
                _def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelSceneBuilder.Level01Asset);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("建立內建關卡資料（測試關 + 第一關）"))
            {
                LevelSceneBuilder.CreateDefinitions();
                _def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(LevelSceneBuilder.Level01Asset);
            }

            if (GUILayout.Button("新建空白關卡…"))
            {
                string path = EditorUtility.SaveFilePanelInProject("新建關卡", "Level_New", "asset", "",
                                                                   EditorBuildUtils.LevelsDir);
                if (!string.IsNullOrEmpty(path))
                {
                    var def = CreateInstance<LevelDefinition>();
                    AssetDatabase.CreateAsset(def, path);
                    AssetDatabase.SaveAssets();
                    _def = def;
                }
            }
        }

        private void DrawSettings()
        {
            _showSettings = EditorGUILayout.Foldout(_showSettings, "關卡設定", true, EditorStyles.foldoutHeader);
            if (!_showSettings) return;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _def.levelName = EditorGUILayout.TextField("關卡名稱", _def.levelName);
            _def.sceneName = EditorGUILayout.TextField("場景名稱", _def.sceneName);
            _def.durationSeconds = EditorGUILayout.FloatField("關卡時間（秒）", _def.durationSeconds);

            EditorGUILayout.LabelField("通關分數（星級門檻）", EditorStyles.boldLabel);
            _def.star1 = EditorGUILayout.IntField("★", _def.star1);
            _def.star2 = EditorGUILayout.IntField("★★", _def.star2);
            _def.star3 = EditorGUILayout.IntField("★★★", _def.star3);

            EditorGUILayout.LabelField("訂單內容池", EditorStyles.boldLabel);
            var so = new SerializedObject(_def);
            EditorGUILayout.PropertyField(so.FindProperty("orderPatterns"), new GUIContent("版型"), true);
            EditorGUILayout.PropertyField(so.FindProperty("orderColors"), new GUIContent("顏色"), true);
            EditorGUILayout.PropertyField(so.FindProperty("orderAccessories"), new GUIContent("飾品"), true);
            so.ApplyModifiedProperties();
            _def.accessoryChance = EditorGUILayout.Slider("含飾品機率", _def.accessoryChance, 0f, 1f);

            EditorGUILayout.EndVertical();
            if (EditorGUI.EndChangeCheck()) MarkDirty();
        }

        private void DrawElements()
        {
            _showElements = EditorGUILayout.Foldout(_showElements,
                $"元件（{_def.elements.Count}）", true, EditorStyles.foldoutHeader);
            if (!_showElements) return;

            EditorGUILayout.BeginHorizontal();
            _newType = (LevelElementType)EditorGUILayout.EnumPopup(_newType);
            if (GUILayout.Button("＋ 新增", GUILayout.Width(80f)))
            {
                Undo.RecordObject(_def, "新增元件");
                var pos = SceneViewFocusPoint();
                _def.Add(_newType, pos);
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            _filter = EditorGUILayout.TextField("篩選", _filter);

            LevelElementRecord toDelete = null;
            LevelElementRecord toDuplicate = null;

            for (int i = 0; i < _def.elements.Count; i++)
            {
                var e = _def.elements[i];
                if (!string.IsNullOrEmpty(_filter) &&
                    !e.type.ToString().ToLower().Contains(_filter.ToLower())) continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{i}. {e.type}", EditorStyles.boldLabel);
                if (GUILayout.Button("選取", GUILayout.Width(48f))) SelectInScene(e);
                if (GUILayout.Button("複製", GUILayout.Width(48f))) toDuplicate = e;
                if (GUILayout.Button("刪除", GUILayout.Width(48f))) toDelete = e;
                EditorGUILayout.EndHorizontal();

                e.type = (LevelElementType)EditorGUILayout.EnumPopup("類型", e.type);
                e.position = EditorGUILayout.Vector3Field("位置", e.position);
                e.rotationEuler = EditorGUILayout.Vector3Field("旋轉", e.rotationEuler);
                e.scale = EditorGUILayout.Vector3Field("縮放", e.scale);
                e.variant = EditorGUILayout.TextField(new GUIContent("Variant",
                    "染料點填顏色（Red/Blue/Green/Yellow）、飾品點填種類（Button/Ribbon/Badge）、NPC 填種類（Phase 2 用）"),
                    e.variant);

                if (EditorGUI.EndChangeCheck()) MarkDirty();
                EditorGUILayout.EndVertical();
            }

            if (toDelete != null)
            {
                Undo.RecordObject(_def, "刪除元件");
                _def.elements.Remove(toDelete);
                MarkDirty();
            }
            if (toDuplicate != null)
            {
                Undo.RecordObject(_def, "複製元件");
                var clone = toDuplicate.Clone();
                clone.position += new Vector3(2f, 0f, 0f);
                _def.elements.Add(clone);
                MarkDirty();
            }
        }

        private void DrawSceneActions()
        {
            EditorGUILayout.LabelField("場景", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("在目前場景生成"))
            {
                var root = LevelSceneBuilder.EnsureLevelRoot();
                LevelSceneBuilder.PopulateLevelRoot(_def, root);
                EditorUtility.SetDirty(root.gameObject);
            }
            if (GUILayout.Button("從場景回寫"))
                PullFromScene();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("建立獨立場景（含 Fusion / UI / 系統）"))
            {
                if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    string sceneName = string.IsNullOrEmpty(_def.sceneName) ? _def.name : _def.sceneName;
                    // 傳資產路徑而不是物件：換場景時 ScriptableObject 參考會被卸載
                    LevelSceneBuilder.BuildScene(AssetDatabase.GetAssetPath(_def), sceneName);
                }
            }
        }

        private void DrawFileActions()
        {
            EditorGUILayout.LabelField("存檔 / 讀檔", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("存成 JSON…"))
            {
                string path = EditorUtility.SaveFilePanel("儲存關卡 JSON",
                    Application.dataPath, _def.name, "json");
                if (!string.IsNullOrEmpty(path))
                {
                    File.WriteAllText(path, _def.ToJson());
                    Debug.Log($"[關卡編輯器] 已存檔：{path}");
                }
            }
            if (GUILayout.Button("從 JSON 讀入…"))
            {
                string path = EditorUtility.OpenFilePanel("讀取關卡 JSON", Application.dataPath, "json");
                if (!string.IsNullOrEmpty(path))
                {
                    Undo.RecordObject(_def, "讀取關卡 JSON");
                    _def.FromJson(File.ReadAllText(path));
                    MarkDirty();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("儲存資產"))
            {
                MarkDirty();
                AssetDatabase.SaveAssets();
            }
        }

        // ---------------------------------------------------------------- 工具

        private void MarkDirty()
        {
            if (_def == null) return;
            EditorUtility.SetDirty(_def);
        }

        private static Vector3 SceneViewFocusPoint()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return Vector3.zero;
            var p = view.pivot;
            return new Vector3(Mathf.Round(p.x * 2f) / 2f, 0f, Mathf.Round(p.z * 2f) / 2f);
        }

        private void SelectInScene(LevelElementRecord rec)
        {
            var tags = Object.FindObjectsByType<LevelElementTag>(FindObjectsSortMode.None);
            var hit = tags.FirstOrDefault(t => t.recordId == rec.id);
            if (hit == null) return;
            Selection.activeGameObject = hit.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        /// <summary>把場景裡被拖動過的元件座標寫回關卡資料。</summary>
        private void PullFromScene()
        {
            var tags = Object.FindObjectsByType<LevelElementTag>(FindObjectsSortMode.None);
            if (tags.Length == 0)
            {
                EditorUtility.DisplayDialog("從場景回寫", "場景裡找不到任何由關卡編輯器放置的元件。", "好");
                return;
            }

            Undo.RecordObject(_def, "從場景回寫");
            var byId = _def.elements.ToDictionary(e => e.id, e => e);
            var seen = new HashSet<string>();

            foreach (var tag in tags)
            {
                var t = tag.transform;
                if (!byId.TryGetValue(tag.recordId ?? "", out var rec))
                {
                    // 場景裡手動複製出來的新物件 -> 補一筆資料
                    rec = new LevelElementRecord { type = tag.type };
                    tag.recordId = rec.id;
                    _def.elements.Add(rec);
                }

                rec.type = tag.type;
                rec.position = t.localPosition;
                rec.rotationEuler = t.localEulerAngles;
                rec.scale = t.localScale;
                rec.variant = tag.variant;
                rec.intParam = tag.intParam;
                seen.Add(rec.id);
            }

            // 場景裡被刪掉的元件，資料也一起移除
            _def.elements.RemoveAll(e => !seen.Contains(e.id));

            MarkDirty();
            Debug.Log($"[關卡編輯器] 已從場景回寫 {seen.Count} 筆元件。");
        }
    }
}
