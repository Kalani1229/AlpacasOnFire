using AlpacasOnFire.Level;
using UnityEditor;
using UnityEngine;

namespace AlpacasOnFire.EditorTools
{
    [CustomEditor(typeof(LevelDefinition))]
    public class LevelDefinitionInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var def = (LevelDefinition)target;

            EditorGUILayout.HelpBox(
                $"{def.levelName}\n元件 {def.elements.Count} 個　時間 {def.durationSeconds:F0} 秒\n" +
                $"星級門檻 ${def.star1} / ${def.star2} / ${def.star3}",
                MessageType.Info);

            if (GUILayout.Button("開啟關卡編輯器", GUILayout.Height(28f)))
                LevelEditorWindow.Open();

            if (GUILayout.Button("在目前場景生成"))
            {
                var root = LevelSceneBuilder.EnsureLevelRoot();
                LevelSceneBuilder.PopulateLevelRoot(def, root);
            }

            EditorGUILayout.Space(8);
            DrawDefaultInspector();
        }
    }
}
