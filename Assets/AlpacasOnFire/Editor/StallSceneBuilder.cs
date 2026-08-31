using System.Collections.Generic;
using System.Linq;
using AlpacasOnFire.Core;
using AlpacasOnFire.Level;
using AlpacasOnFire.Orders;
using AlpacasOnFire.Stall;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static AlpacasOnFire.EditorTools.EditorBuildUtils;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 擺攤系統的測試場景。
    ///
    /// 內容刻意極簡（規格書：地板、素材點、出生點、一個手提箱）—— 這個場景的用途是
    /// 「把擺攤流程從頭到尾跑一次」，不是展示關卡設計，所以任何多餘的東西都只會干擾判斷。
    ///
    /// 場景結構沿用 LevelSceneBuilder 已驗證的兩段式流程：
    /// 先把骨架存成真正的場景檔，重新開啟之後才放置關卡元件。
    /// </summary>
    public static class StallSceneBuilder
    {
        public const string StallLevelAsset = LevelsDir + "/Level_StallTest.asset";
        public const string StallSceneName  = "Stall_Test";

        [MenuItem("羊駝很忙/擺攤/1. 建置擺攤測試場景", priority = 40)]
        public static void BuildStallScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildReport.Begin("建置擺攤測試場景");
            if (!LevelSceneBuilder.RequireCatalog())
            {
                BuildReport.Save();
                return;
            }

            EnsureFolders();
            CreateStallDefinition();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            BuildSceneInternal();
            RegisterInBuildSettings();
            BuildReport.Save();
        }

        [MenuItem("羊駝很忙/擺攤/2. 一鍵重置擺攤測試場景", priority = 41)]
        public static void ResetStallScene()
        {
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.name != StallSceneName)
            {
                if (!EditorUtility.DisplayDialog("一鍵重置",
                        $"目前開著的是「{active.name}」，不是擺攤測試場景。要直接重建 {StallSceneName} 嗎？",
                        "重建", "取消"))
                    return;
            }

            BuildReport.Begin("一鍵重置擺攤測試場景");
            if (!LevelSceneBuilder.RequireCatalog())
            {
                BuildReport.Save();
                return;
            }

            CreateStallDefinition();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            BuildSceneInternal();
            BuildReport.Info("擺攤測試場景已重置。按 Play 就可以從頭跑一次擺攤流程。");
            BuildReport.Save();
        }

        [MenuItem("羊駝很忙/擺攤/3. 檢查擺攤設置（診斷用）", priority = 42)]
        public static void ValidateStallSetup()
        {
            var catalog = LevelSceneBuilder.LoadCatalog();
            if (catalog == null)
            {
                Debug.LogError("[擺攤] 找不到 Resources/GameCatalog.asset，請先執行「1. 建置佔位資產」。");
                return;
            }

            var missing = new List<string>();

            bool hasSuitcase = catalog.items.Any(e => e.kind == ItemKind.Suitcase && e.prefab != null);
            if (!hasSuitcase) missing.Add("物品 Suitcase（手提箱）");

            // 工具架上會生出工具，所以工具本身的 prefab 也必須在
            foreach (var kind in new[] { ItemKind.Shears, ItemKind.SprayGun })
                if (!catalog.items.Any(e => e.kind == kind && e.prefab != null))
                    missing.Add($"工具 {kind}");

            foreach (var type in StallCatalog.Devices)
            {
                var entry = catalog.elements.FirstOrDefault(e => e.type == type && e.prefab != null);
                if (entry.prefab == null)
                {
                    missing.Add($"裝備 {type}");
                    continue;
                }

                if (entry.prefab.GetComponent<Fusion.NetworkObject>() == null)
                    missing.Add($"裝備 {type} 的 prefab 沒有 NetworkObject（執行期生成會失敗）");

                if (entry.prefab.GetComponentInChildren<DeployableDevice>(true) == null)
                    missing.Add($"裝備 {type} 的 prefab 沒有 DeployHandle（擺出來之後收不回去）");
            }

            // 開張鈴不在 StallCatalog.Devices 裡（它不是裝備），要另外檢查
            var bell = catalog.elements.FirstOrDefault(
                e => e.type == LevelElementType.ServiceBell && e.prefab != null);
            if (bell.prefab == null)
                missing.Add("開張鈴 ServiceBell（沒有它就無法開張）");
            else if (bell.prefab.GetComponent<Fusion.NetworkObject>() == null)
                missing.Add("開張鈴的 prefab 沒有 NetworkObject（執行期生成會失敗）");
            else if (bell.prefab.GetComponentInChildren<DeployableDevice>(true) != null)
                missing.Add("開張鈴不該有 DeployHandle —— 它是固定設施，不進網格");

            // 襯布放不下全部裝備是設計錯誤，要在建置時就抓出來，不要等到執行期
            if (!StallCatalog.MatFitsAllDevices(out int required, out int available))
                missing.Add($"襯布放不下全部裝備：需要 {required} 格，只有 {available} 格");

            // 預設佈局必須本身就不衝突，否則第一次開箱就會看到「改放到別格」的警告
            var occupancy = new StallGrid.Occupancy();
            foreach (var rec in StallCatalog.DefaultLayout)
            {
                StallGrid.RotatedFootprint(StallCatalog.Footprint(rec.DeviceType), rec.Facing,
                                           out int w, out int d);
                var check = occupancy.Check(rec.CellX, rec.CellZ, w, d);
                if (check != PlacementResult.Ok)
                    missing.Add($"預設佈局有問題：{rec} -> {check}");
                else
                    occupancy.OccupyFootprint(rec.CellX, rec.CellZ, w, d);
            }

            if (missing.Count == 0)
                Debug.Log($"[擺攤] 設置完整。襯布 {StallGrid.Cells}x{StallGrid.Cells} 格" +
                          $"（{GameTuning.StallMatSize:F1} x {GameTuning.StallMatSize:F1} 公尺）、" +
                          $"預設佈局 {StallCatalog.DefaultLayout.Length} 台無衝突。\n" +
                          "提醒：第一次執行前記得跑一次選單 Tools > Fusion > Rebuild Prefab Table，" +
                          "讓新的裝備 prefab 進到 Fusion 的 prefab 表裡。");
            else
                Debug.LogError("[擺攤] 缺少以下項目，請重跑「1. 建置佔位資產」：\n - " +
                               string.Join("\n - ", missing));
        }

        // ------------------------------------------------------------ 關卡資料

        /// <summary>
        /// 擺攤測試關卡：一塊很大的平地 + 出生點 + 幾個染料點 + 手提箱生成點。
        /// 機台一台都不預先放 —— 它們現在全部從手提箱裡拿出來，這正是要測的東西。
        /// </summary>
        public static LevelDefinition CreateStallDefinition()
        {
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(StallLevelAsset);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<LevelDefinition>();
                AssetDatabase.CreateAsset(def, StallLevelAsset);
            }

            def.levelName = "擺攤測試場";
            def.sceneName = StallSceneName;
            def.durationSeconds = GameTuning.StallDurationSeconds;
            def.star1 = GameTuning.Star1Threshold;
            def.star2 = GameTuning.Star2Threshold;
            def.star3 = GameTuning.Star3Threshold;
            def.orderPatterns = new[] { PatternType.TShirt };
            def.orderColors = new[] { DyeColorType.White, DyeColorType.Red };
            def.orderAccessories = new[] { AccessoryType.None };
            def.accessoryChance = 0f;
            def.elements = new List<LevelElementRecord>();

            // 60 x 60 的平地：襯布是 8x8，留很多空間試不同的擺攤地點
            def.Add(LevelElementType.FloorTile, new Vector3(0f, -0.1f, 0f)).scale = new Vector3(6f, 1f, 6f);

            def.Add(LevelElementType.PlayerSpawn, new Vector3(-3f, 0f, -12f));
            def.Add(LevelElementType.PlayerSpawn, new Vector3(3f, 0f, -12f));

            def.Add(LevelElementType.SuitcaseSpawn, new Vector3(0f, 0f, -9f));

            // 素材點：沿用工坊場景的取得方式（規格書：素材不改）
            var d1 = def.Add(LevelElementType.DyeSourceRed, new Vector3(-14f, 0f, 10f));
            var d2 = def.Add(LevelElementType.DyeSourceRed, new Vector3(-14f, 0f, 14f));
            var d3 = def.Add(LevelElementType.DyeSourceRed, new Vector3(-10f, 0f, 12f));
            d1.variant = d2.variant = d3.variant = DyeColorType.Red.ToString();

            // 一個斜坡當「不能擺攤」的反例，方便驗證平坦度檢測真的有作用
            var ramp = def.Add(LevelElementType.Wall, new Vector3(16f, -1.2f, 0f), new Vector3(0f, 0f, 18f));
            ramp.scale = new Vector3(14f, 1f, 14f);

            def.Add(LevelElementType.OrderBoardAnchor, new Vector3(0f, 0f, 16f), new Vector3(0f, 180f, 0f));

            EditorUtility.SetDirty(def);
            return def;
        }

        // ------------------------------------------------------------ 場景

        private static void BuildSceneInternal()
        {
            string path = $"{ScenesDir}/{StallSceneName}.unity";

            // --- 第一段：骨架 ---
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(StallLevelAsset);
            LevelSceneBuilder.CreateSystems(def);
            CreateLighting();
            ConfigureForStall(def);
            new GameObject(LevelBuilder.RootName);
            EditorSceneManager.SaveScene(scene, path);

            // --- 第二段：在已落地的場景裡放元件 ---
            // 換過場景之後 ScriptableObject 參考會變成 Unity 的「假 null」，一定要重載
            var opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(StallLevelAsset);
            LevelSceneBuilder.PopulateLevelRoot(def, LevelSceneBuilder.EnsureLevelRoot());

            // 手提箱的生成位置要跟著場景裡的 SuitcaseSpawn 走
            ApplySuitcaseSpawn(def);

            EditorSceneManager.MarkSceneDirty(opened);
            EditorSceneManager.SaveScene(opened);

            VerifyStallScene(path);
        }

        /// <summary>把 [GameSystems] 切成擺攤模式，並掛上 StallManager。</summary>
        private static void ConfigureForStall(LevelDefinition def)
        {
            var systems = GameObject.Find("[GameSystems]");
            if (systems == null)
            {
                BuildReport.Error("找不到 [GameSystems]，擺攤場景會缺少 StallManager。");
                return;
            }

            var director = systems.GetComponent<LevelDirector>();
            if (director != null)
            {
                SetBool(director, "_stallMode", true);
                SetFloat(director, "_durationSeconds", GameTuning.StallDurationSeconds);
            }

            var stall = systems.GetComponent<StallManager>();
            if (stall == null) stall = systems.AddComponent<StallManager>();
            SetBool(stall, "_spawnSuitcaseOnStart", true);
            SetVector3(stall, "_suitcaseSpawnPosition", new Vector3(0f, 0.4f, -9f));

            BuildReport.Line("  [GameSystems] 已切換成擺攤模式（LevelDirector.stallMode = true）並掛上 StallManager");
        }

        private static void ApplySuitcaseSpawn(LevelDefinition def)
        {
            if (def == null) return;

            var record = def.elements.FirstOrDefault(e => e.type == LevelElementType.SuitcaseSpawn);
            if (record == null) return;

            var systems = GameObject.Find("[GameSystems]");
            var stall = systems != null ? systems.GetComponent<StallManager>() : null;
            if (stall == null) return;

            SetVector3(stall, "_suitcaseSpawnPosition", record.position + Vector3.up * 0.4f);
            EditorUtility.SetDirty(stall);
            BuildReport.Line($"  手提箱生成位置：{record.position + Vector3.up * 0.4f}");
        }

        private static void CreateLighting()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(48f, 32f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.6f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.45f, 0.45f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.22f);
        }

        // ------------------------------------------------------------ 驗證

        /// <summary>
        /// 建完之後重新讀場景檔確認關鍵物件真的在，避免又做出一個空殼場景。
        /// 這幾項只要有一項缺了，Play 下去就會是「什麼都不能做」的狀態，很難從現象反推原因。
        /// </summary>
        private static void VerifyStallScene(string path)
        {
            var opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(StallLevelAsset);

            var problems = new List<string>();

            if (Object.FindFirstObjectByType<StallManager>() == null)
                problems.Add("場景裡沒有 StallManager");

            var director = Object.FindFirstObjectByType<LevelDirector>();
            if (director == null) problems.Add("場景裡沒有 LevelDirector");
            else if (!director.StallMode) problems.Add("LevelDirector 沒有勾選擺攤模式（計時會在開場就跑掉）");

            if (Object.FindFirstObjectByType<OrderBoard>() == null)
                problems.Add("場景裡沒有 OrderBoard");

            if (Object.FindFirstObjectByType<Networking.SpawnPointRegistry>() == null)
                problems.Add("場景裡沒有出生點（玩家會生在原點）");

            int placed = 0;
            foreach (var root in opened.GetRootGameObjects())
                if (root.name == LevelBuilder.RootName) placed = root.transform.childCount;

            int expected = def != null ? def.elements.Count : 0;
            if (expected > 0 && placed < expected)
                problems.Add($"[Level] 底下只有 {placed} 個物件，應該要有 {expected} 個");

            if (def != null && def.elements.All(e => e.type != LevelElementType.FloorTile))
                problems.Add("沒有任何 FloorTile —— 玩家會一直往下掉");

            if (problems.Count > 0)
            {
                BuildReport.Error($"擺攤測試場景驗證失敗：\n - {string.Join("\n - ", problems)}");
                return;
            }

            BuildReport.Info($"擺攤測試場景已建立並通過驗證：{path}（[Level] 共 {placed} 個物件）");
        }

        private static void RegisterInBuildSettings()
        {
            string path = $"{ScenesDir}/{StallSceneName}.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return;

            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == path)) return;

            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
            BuildReport.Line($"  已加入 Build Settings：{path}");
        }

        // ------------------------------------------------------------ 小工具

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { BuildReport.Warn($"{target.GetType().Name} 找不到欄位 {field}"); return; }
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector3(Object target, string field, Vector3 value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { BuildReport.Warn($"{target.GetType().Name} 找不到欄位 {field}"); return; }
            p.vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
