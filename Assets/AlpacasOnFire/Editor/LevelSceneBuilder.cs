using System.Collections.Generic;
using System.Linq;
using AlpacasOnFire.Core;
using AlpacasOnFire.Level;
using AlpacasOnFire.Networking;
using AlpacasOnFire.Orders;
using AlpacasOnFire.UI;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static AlpacasOnFire.EditorTools.EditorBuildUtils;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 從 LevelDefinition 產生可以直接玩的場景：
    ///  - [Network]：NetworkRunner + GameLauncher
    ///  - [GameSystems]：LevelDirector + OrderBoard（Fusion 場景物件）
    ///  - [GameUI]：HUD / 暫停 / 結算 / 選版型
    ///  - [Level]：關卡編輯器放置的所有元件
    /// </summary>
    public static class LevelSceneBuilder
    {
        public const string TestLevelAsset  = LevelsDir + "/Level_Test.asset";
        public const string Level01Asset    = LevelsDir + "/Level01_Workshop.asset";
        public const string TestSceneName   = "Level_Test";
        public const string Level01SceneName = "Level01_Workshop";
        public const string TitleSceneName  = "Title";

        [MenuItem("羊駝很忙/舊版 Phase 1/建立-更新關卡資料（測試關 + 第一關）", priority = 90)]
        public static void CreateDefinitions()
        {
            EnsureFolders();
            CreateTestDefinition();
            CreateLevel01Definition();
            AssetDatabase.SaveAssets();
            Debug.Log("[羊駝很忙] 關卡資料已建立於 " + LevelsDir);
        }

        [MenuItem("羊駝很忙/舊版 Phase 1/建置工坊時期場景（標題 + 測試關 + 第一關）", priority = 91)]
        public static void BuildAllScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildReport.Begin("建置全部場景");
            BuildAllScenesInternal();
            BuildReport.Save();
        }

        public static void BuildAllScenesInternal()
        {
            if (!RequireCatalog()) return;

            EnsureFolders();
            CreateTestDefinition();
            CreateLevel01Definition();

            // 關鍵：先把關卡資料寫進磁碟並重新載入，避免用到記憶體中還沒同步的版本
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var test = AssetDatabase.LoadAssetAtPath<LevelDefinition>(TestLevelAsset);
            var lvl1 = AssetDatabase.LoadAssetAtPath<LevelDefinition>(Level01Asset);

            BuildReport.Info($"關卡資料：測試關 {test?.elements.Count ?? -1} 筆、第一關 {lvl1?.elements.Count ?? -1} 筆");

            BuildTitleScene();
            BuildScene(TestLevelAsset, TestSceneName);
            BuildScene(Level01Asset, Level01SceneName);

            RegisterBuildSettings();
            BuildReport.Info("三個場景都建好了，並已加入 Build Settings。");
        }

        [MenuItem("羊駝很忙/0. 一鍵重建（資產 + 擺攤測試場景）", priority = -1)]
        public static void RebuildEverything()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildReport.Begin("一鍵重建");

            // 資產（含擺攤裝備 —— PlaceholderAssetBuilder 內部會呼叫 StallAssetBuilder）
            PlaceholderAssetBuilder.BuildAllInternal();

            // 場景只重建「現在在用的」擺攤測試場景。
            // 這裡刻意**不**碰 Phase 1 的 Title / Level_Test / Level01_Workshop ——
            // 那些是工坊時期的舊場景，重建會把你丟回舊版本的樣子。
            // 真的要動它們，走「舊版 Phase 1」子選單。
            StallSceneBuilder.RebuildForFullRebuild();

            BuildReport.Save();
        }

        [MenuItem("羊駝很忙/3. 重建目前場景的關卡內容", priority = 3)]
        public static void RebuildCurrentSceneLevel()
        {
            if (!RequireCatalog()) return;

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var def = AssetDatabase.FindAssets("t:LevelDefinition", new[] { LevelsDir })
                .Select(g => AssetDatabase.LoadAssetAtPath<LevelDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(d => d != null &&
                                     (d.sceneName == sceneName || d.name == sceneName));

            if (def == null)
            {
                Debug.LogError($"[羊駝很忙] 找不到對應「{sceneName}」的 LevelDefinition。" +
                               "請用關卡編輯器選一份資料，按「在目前場景生成」。");
                return;
            }

            var root = EnsureLevelRoot();
            PopulateLevelRoot(def, root);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log($"[羊駝很忙] 已重建「{sceneName}」的關卡內容。記得存檔（Ctrl+S）。");
        }

        // ------------------------------------------------------------ 關卡資料

        private static LevelDefinition LoadOrCreate(string path)
        {
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(path);
            if (def != null) return def;
            def = ScriptableObject.CreateInstance<LevelDefinition>();
            AssetDatabase.CreateAsset(def, path);
            return def;
        }

        /// <summary>測試關卡：所有元件排成一列，間隔固定，方便逐一驗證。</summary>
        public static LevelDefinition CreateTestDefinition()
        {
            var def = LoadOrCreate(TestLevelAsset);
            def.levelName = "測試關卡（全元件展示）";
            def.sceneName = TestSceneName;
            def.durationSeconds = GameTuning.LevelDurationSeconds;
            def.star1 = GameTuning.Star1Threshold;
            def.star2 = GameTuning.Star2Threshold;
            def.star3 = GameTuning.Star3Threshold;
            def.orderPatterns = new[] { PatternType.TShirt };
            def.orderColors = new[] { DyeColorType.White, DyeColorType.Red };
            def.orderAccessories = new[] { AccessoryType.None };
            def.accessoryChance = 0f;
            def.elements = new List<LevelElementRecord>();

            // 地板 70 x 30
            def.Add(LevelElementType.FloorTile, new Vector3(0f, -0.1f, 0f)).scale = new Vector3(7f, 1f, 3f);

            // 一列元件，間隔 6 公尺
            var row = new[]
            {
                LevelElementType.Shears,
                LevelElementType.Shears,
                LevelElementType.SewingMachine,
                LevelElementType.Juicer,
                LevelElementType.Mannequin,
                LevelElementType.DyeSourceRed,
                LevelElementType.AccessoryDispenser,
                LevelElementType.BoxDispenser,
                LevelElementType.Mailbox,
                LevelElementType.RecyclingMachine,
                LevelElementType.Npc,
            };

            float startX = -(row.Length - 1) * 3f;
            for (int i = 0; i < row.Length; i++)
            {
                float y = row[i] is LevelElementType.Shears ? 0.35f : 0f;
                def.Add(row[i], new Vector3(startX + i * 6f, y, 0f), new Vector3(0f, 180f, 0f));
            }

            def.Add(LevelElementType.PlayerSpawn, new Vector3(0f, 0f, -8f));
            def.Add(LevelElementType.OrderBoardAnchor, new Vector3(0f, 0f, 8f), new Vector3(0f, 180f, 0f));

            EditorUtility.SetDirty(def);
            return def;
        }

        /// <summary>正式第一關：工坊（室內）+ 戶外素材區。</summary>
        public static LevelDefinition CreateLevel01Definition()
        {
            var def = LoadOrCreate(Level01Asset);
            def.levelName = "第一關 — 工坊";
            def.sceneName = Level01SceneName;
            def.durationSeconds = GameTuning.LevelDurationSeconds;
            def.star1 = GameTuning.Star1Threshold;
            def.star2 = GameTuning.Star2Threshold;
            def.star3 = GameTuning.Star3Threshold;
            def.orderPatterns = new[] { PatternType.TShirt };
            def.orderColors = new[] { DyeColorType.White, DyeColorType.Red };
            def.orderAccessories = new[] { AccessoryType.None };
            def.accessoryChance = 0f;
            def.elements = new List<LevelElementRecord>();

            // ---- 地板 ----
            // 工坊 28 x 16（x -14~14, z -8~8）
            def.Add(LevelElementType.FloorTile, new Vector3(0f, -0.1f, 0f)).scale = new Vector3(2.8f, 1f, 1.6f);
            // 戶外 40 x 30（z 7~37）
            def.Add(LevelElementType.FloorTile, new Vector3(0f, -0.1f, 22f)).scale = new Vector3(4f, 1f, 3f);

            // ---- 工坊牆（北牆中間留門通往戶外）----
            def.Add(LevelElementType.Wall, new Vector3(0f, 0f, -8f)).scale = new Vector3(28f, 1f, 1f);
            def.Add(LevelElementType.Wall, new Vector3(-8f, 0f, 8f)).scale = new Vector3(12f, 1f, 1f);
            def.Add(LevelElementType.Wall, new Vector3(8f, 0f, 8f)).scale = new Vector3(12f, 1f, 1f);
            def.Add(LevelElementType.Wall, new Vector3(-14f, 0f, 0f)).scale = new Vector3(1f, 1f, 16f);
            def.Add(LevelElementType.Wall, new Vector3(14f, 0f, 0f)).scale = new Vector3(1f, 1f, 16f);

            // ---- ① 剃毛與縫紉台（入口側）----
            def.Add(LevelElementType.PlayerSpawn, new Vector3(-11.5f, 0f, 0f), new Vector3(0f, 90f, 0f));
            def.Add(LevelElementType.Shears, new Vector3(-9.5f, 0.35f, 2.5f));
            def.Add(LevelElementType.Shears, new Vector3(-9.5f, 0.35f, -2.5f));
            def.Add(LevelElementType.SewingMachine, new Vector3(-6f, 0f, 0f), new Vector3(0f, -90f, 0f));

            // ---- ② 染色與飾品站（中段）----
            def.Add(LevelElementType.Juicer, new Vector3(-1f, 0f, 4.5f), new Vector3(0f, 180f, 0f));
            def.Add(LevelElementType.Mannequin, new Vector3(-1.5f, 0f, -3.5f), new Vector3(0f, 0f, 0f));
            def.Add(LevelElementType.Mannequin, new Vector3(1.5f, 0f, -3.5f), new Vector3(0f, 0f, 0f));
            var acc = def.Add(LevelElementType.AccessoryDispenser, new Vector3(4f, 0f, 4.5f), new Vector3(0f, 180f, 0f));
            acc.variant = AccessoryType.Button.ToString();
            def.Add(LevelElementType.RecyclingMachine, new Vector3(4f, 0f, -4.5f));

            // ---- ③ 包裝出貨口（出口側）----
            def.Add(LevelElementType.BoxDispenser, new Vector3(8.5f, 0f, 3f), new Vector3(0f, 180f, 0f));
            def.Add(LevelElementType.Mailbox, new Vector3(11.5f, 0f, 0f), new Vector3(0f, 90f, 0f));
            def.Add(LevelElementType.OrderBoardAnchor, new Vector3(11.8f, 0f, 4f), new Vector3(0f, 90f, 0f));

            // ---- 戶外素材區（跟工坊有一段步行距離）----
            var d1 = def.Add(LevelElementType.DyeSourceRed, new Vector3(0f, 0f, 27f));
            var d2 = def.Add(LevelElementType.DyeSourceRed, new Vector3(5f, 0f, 30f));
            var d3 = def.Add(LevelElementType.DyeSourceRed, new Vector3(-5f, 0f, 30f));
            d1.variant = d2.variant = d3.variant = DyeColorType.Red.ToString();

            EditorUtility.SetDirty(def);
            return def;
        }

        // ------------------------------------------------------------ 場景

        /// <summary>沒有 Catalog 就不要建場景，否則會做出一個空殼場景（只有 UI、沒有任何物件）。</summary>
        public static bool RequireCatalog()
        {
            var catalog = LoadCatalog();
            if (catalog != null && catalog.playerPrefab != null && catalog.elements.Length > 0) return true;

            Debug.LogError("[羊駝很忙] Resources/GameCatalog.asset 不存在或不完整。" +
                           "請先執行「羊駝很忙 / 1. 建置佔位資產」，確認 Console 沒有紅字後再建場景。" +
                           "（也可以用「4. 檢查設置」看缺什麼）");
            return false;
        }

        /// <summary>
        /// 建立一個可直接玩的關卡場景。
        ///
        /// 流程刻意分成兩段：先把骨架（燈光／Fusion／系統／UI／空的 [Level]）存成真正的場景檔，
        /// 重新開啟之後才放置關卡元件。原因是在「尚未存檔的新場景」裡放 prefab instance
        /// 並不可靠，存檔時可能整批被丟掉；先落地再放置，走的就跟「5. 重建目前場景的關卡內容」
        /// 完全相同的路徑（那條路徑已驗證可用）。
        /// </summary>
        /// <summary>
        /// 建立一個可直接玩的關卡場景。
        ///
        /// **參數是 LevelDefinition 的資產路徑，不是物件參考** —— 這點很重要：
        /// EditorSceneManager.NewScene / OpenScene 會觸發資產卸載，讓原本持有的
        /// ScriptableObject 參考變成 Unity 的「假 null」。之前就是因為這樣，
        /// PopulateLevelRoot 一進去就 `if (def == null) return;` 靜靜跳過，
        /// 產出一個 [Level] 全空的場景。所以每次換完場景都要重新載入一次。
        /// </summary>
        public static void BuildScene(string defAssetPath, string sceneName)
        {
            string path = $"{ScenesDir}/{sceneName}.unity";

            // --- 第一段：骨架 ---
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var def = ReloadDefinition(defAssetPath);
            CreateLighting();
            CreateSystems(def);
            new GameObject(LevelBuilder.RootName);
            EditorSceneManager.SaveScene(scene, path);

            // --- 第二段：在已落地的場景裡放元件 ---
            var opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            def = ReloadDefinition(defAssetPath);          // ★ 換過場景，一定要重載
            PopulateLevelRoot(def, EnsureLevelRoot());
            EditorSceneManager.MarkSceneDirty(opened);
            EditorSceneManager.SaveScene(opened);

            VerifyScene(path, defAssetPath);
        }

        private static LevelDefinition ReloadDefinition(string assetPath)
        {
            var def = AssetDatabase.LoadAssetAtPath<LevelDefinition>(assetPath);
            if (def == null)
                BuildReport.Error($"載入關卡資料失敗：{assetPath}");
            return def;
        }

        /// <summary>建完之後重新讀場景檔確認元件真的存在，避免再產出空殼場景。</summary>
        private static void VerifyScene(string path, string defAssetPath)
        {
            var opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var def = ReloadDefinition(defAssetPath);      // ★ 開完場景才讀，避免又拿到假 null
            if (def == null)
            {
                BuildReport.Error($"驗證 {path} 時讀不到關卡資料，無法比對數量。");
                return;
            }
            int expected = def.elements.Count;

            int actual = 0;
            foreach (var root in opened.GetRootGameObjects())
            {
                if (root.name != LevelBuilder.RootName) continue;
                actual = root.transform.childCount;
            }

            if (expected > 0 && actual < expected)
            {
                BuildReport.Error($"場景 {path} 驗證失敗：[Level] 底下只有 {actual} 個物件，應該要有 {expected} 個。");
                return;
            }

            // 沒有地板的話玩家一 Play 就會一直往下掉，這是一定要擋下來的錯誤
            int floors = def.elements.Count(e => e.type == LevelElementType.FloorTile);
            if (floors == 0)
            {
                BuildReport.Error($"{path} 沒有任何 FloorTile —— 玩家會一直往下掉。" +
                                  "請在關卡編輯器加入地板，或重跑「2. 建立/更新關卡資料」。");
                return;
            }

            BuildReport.Info($"場景已建立並通過驗證：{path}（[Level] 共 {actual} 個物件，其中地板 {floors} 塊）");
        }

        public static void BuildTitleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateLighting();

            var go = new GameObject("[TitleUI]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var menu = go.AddComponent<TitleMenu>();
            SetString(menu, "_gameSceneName", Level01SceneName);

            var cam = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0f, 2f, -10f);

            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{TitleSceneName}.unity");
        }

        private static void SetString(Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
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

        /// <summary>建立 Fusion runner、關卡系統與 UI。</summary>
        public static void CreateSystems(LevelDefinition def)
        {
            var net = new GameObject("[Network]");
            net.AddComponent<NetworkRunner>();
            net.AddComponent<GameLauncher>();

            var sys = new GameObject("[GameSystems]");
            sys.AddComponent<NetworkObject>();
            var director = sys.AddComponent<LevelDirector>();
            var board = sys.AddComponent<OrderBoard>();

            if (def != null)
            {
                SetFloat(director, "_durationSeconds", def.durationSeconds);
                SetInt(director, "_star1", def.star1);
                SetInt(director, "_star2", def.star2);
                SetInt(director, "_star3", def.star3);

                SetEnumArray(board, "_patterns", def.orderPatterns.Select(x => (int)x).ToArray());
                SetEnumArray(board, "_colors", def.orderColors.Select(x => (int)x).ToArray());
                SetEnumArray(board, "_accessories", def.orderAccessories.Select(x => (int)x).ToArray());
                SetFloat(board, "_accessoryChance", def.accessoryChance);
            }

            var ui = new GameObject("[GameUI]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            ui.AddComponent<GameUIRoot>();

            // 開場到玩家生成之間的過場相機；本機玩家的 PlayerCameraRig 一出現就會把它關掉
            var boot = new GameObject("BootstrapCamera", typeof(Camera), typeof(AudioListener));
            boot.tag = "MainCamera";
            boot.transform.position = new Vector3(0f, 14f, -14f);
            boot.transform.rotation = Quaternion.Euler(40f, 0f, 0f);
        }

        private static void SetEnumArray(Object target, string field, int[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).enumValueIndex = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>編輯期一律用 AssetDatabase 載入 Catalog，不要靠 Resources.Load。</summary>
        public static GameCatalog LoadCatalog()
            => AssetDatabase.LoadAssetAtPath<GameCatalog>($"{ResourcesDir}/GameCatalog.asset");

        /// <summary>把 LevelDefinition 的元件放進 [Level] 底下（會先清空）。</summary>
        public static void PopulateLevelRoot(LevelDefinition def, Transform levelRoot)
        {
            for (int i = levelRoot.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(levelRoot.GetChild(i).gameObject);

            if (def == null)
            {
                BuildReport.Error("PopulateLevelRoot 收到的關卡資料是 null（很可能是換場景時被卸載了），" +
                                  "場景會是空的。請改用資產路徑重新載入後再呼叫。");
                return;
            }

            var catalog = LoadCatalog();
            if (catalog == null)
            {
                BuildReport.Error("載入 GameCatalog 失敗，場景會是空的。請先執行「1. 建置佔位資產」。");
                return;
            }

            BuildReport.Line($"--- 放置 {def.levelName}：資料有 {def.elements.Count} 筆，" +
                             $"Catalog 有 {catalog.elements.Length} 種元件 ---");

            int placed = 0;
            var missing = new List<string>();

            foreach (var rec in def.elements)
            {
                GameObject go = null;
                try
                {
                    go = LevelBuilder.Instantiate(rec, levelRoot, prefab =>
                    {
                        // 正常路徑：保留 prefab 連結
                        var inst = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                        // 退路：連結失敗也要有東西，總比空場景好
                        if (inst == null)
                        {
                            BuildReport.Warn($"InstantiatePrefab 回傳 null（{prefab.name}），改用一般 Instantiate。");
                            inst = Object.Instantiate(prefab);
                        }
                        return inst;
                    }, catalog);
                }
                catch (System.Exception e)
                {
                    BuildReport.Exception($"放置 {rec.type}", e);
                }

                if (go != null)
                {
                    // 用程式改 prefab instance 的屬性（位置／旋轉／縮放／染料顏色）之後，
                    // 一定要記錄成 prefab override，否則存檔時這些改動會被還原掉。
                    if (PrefabUtility.IsPartOfPrefabInstance(go))
                    {
                        PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                        foreach (var comp in go.GetComponents<Component>())
                            if (comp != null) PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
                    }
                    placed++;
                }
                else
                {
                    missing.Add(rec.type.ToString());
                    var prefab = catalog.GetElement(rec.type);
                    BuildReport.Line($"  失敗：{rec.type}（Catalog 中的 prefab = " +
                                     $"{(prefab == null ? "null" : prefab.name)}）");
                }
            }

            if (placed == 0 && def.elements.Count > 0)
                BuildReport.Error($"{def.levelName}：{def.elements.Count} 筆元件全部沒放出來。" +
                                  $"缺少的類型：{string.Join(", ", missing.Distinct())}");
            else if (missing.Count > 0)
                BuildReport.Warn($"{def.levelName}：放置了 {placed}/{def.elements.Count} 個，" +
                                 $"缺少：{string.Join(", ", missing.Distinct())}");
            else
                BuildReport.Info($"{def.levelName}：放置了 {placed}/{def.elements.Count} 個元件。");
        }

        public static Transform EnsureLevelRoot()
        {
            var existing = GameObject.Find(LevelBuilder.RootName);
            if (existing != null) return existing.transform;
            return new GameObject(LevelBuilder.RootName).transform;
        }

        private static void RegisterBuildSettings()
        {
            var wanted = new[]
            {
                $"{ScenesDir}/{TitleSceneName}.unity",
                $"{ScenesDir}/{Level01SceneName}.unity",
                $"{ScenesDir}/{TestSceneName}.unity",
            };

            var list = new List<EditorBuildSettingsScene>();
            foreach (var path in wanted)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) continue;
                list.Add(new EditorBuildSettingsScene(path, true));
            }
            foreach (var s in EditorBuildSettings.scenes)
            {
                if (list.Any(x => x.path == s.path)) continue;
                list.Add(s);
            }
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
