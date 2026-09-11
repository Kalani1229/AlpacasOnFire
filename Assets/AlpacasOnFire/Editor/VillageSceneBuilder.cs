using System.Collections.Generic;
using System.Linq;
using AlpacasOnFire.Core;
using AlpacasOnFire.Level;
using AlpacasOnFire.Machines;
using AlpacasOnFire.Npc;
using AlpacasOnFire.Orders;
using AlpacasOnFire.Stall;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static AlpacasOnFire.EditorTools.EditorBuildUtils;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// v6 羊駝村的測試場景（批 A + 批 B）。
    ///
    /// **做法刻意是「複製 Stall_Test」而不是從零蓋一個新場景骨架。** 兩個理由：
    ///  1. Stall_Test 是目前唯一驗證過能跑通的東西，它的地板／出生點／[GameSystems]／
    ///     手提箱生成點全部都是對的，重蓋一次只會多一次出錯機會
    ///  2. 這一批要測的是「剃毛 -> 背包」，不是場景配置
    ///
    /// 複製出來之後只做加法：掛 TeamStash 與 CustomerQueue、把 StallManager 切成
    /// Village loadout、放一批 WoolNpc。
    /// **原始的 Stall_Test 從頭到尾只被讀取，不會被寫入** —— 它仍然是壞掉時的退路。
    /// </summary>
    public static class VillageSceneBuilder
    {
        public const string VillageSceneName = "Village_Test";

        private static string SourceScenePath => $"{ScenesDir}/{StallSceneBuilder.StallSceneName}.unity";
        private static string VillageScenePath => $"{ScenesDir}/{VillageSceneName}.unity";

        /// <summary>
        /// NPC 的毛色分布。**貴的顏色比較少** —— 這是讓「選三色」變成真決策的關鍵：
        /// 紅毛要花時間找、還要等牠長回來，黃毛走過去就有一大把。
        ///
        ///   黃 4（15 元）、綠 3（20）、藍 2（30）、紅 1（45）
        ///
        /// **野生動物一律不長白毛。** 白毛是玩家自己這條產線的專屬產出 ——
        /// 互相剃隊友，每 6 秒長回一份，無限供應，所以它是最便宜的（10 元）。
        /// 如果野外也有白毛，那條產線就沒有存在的理由了：走過去剃一隻羊
        /// 永遠比跑回去找隊友輕鬆。
        ///
        /// 陣列直接列出每一隻的顏色，改比例只要改這一行。
        /// </summary>
        private static readonly DyeColorType[] NpcColours =
        {
            DyeColorType.Yellow, DyeColorType.Yellow, DyeColorType.Yellow, DyeColorType.Yellow,
            DyeColorType.Green,  DyeColorType.Green,  DyeColorType.Green,
            DyeColorType.Blue,   DyeColorType.Blue,
            DyeColorType.Red,
        };

        private static readonly int NpcCount = NpcColours.Length;
        private const float NpcSpreadRadius = 16f;

        // ------------------------------------------------------------ 選單

        [MenuItem("羊駝很忙/v6 羊駝村/1. 建置羊駝村測試場景", priority = 60)]
        public static void BuildVillageScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildReport.Begin("建置羊駝村測試場景（批 A）");
            BuildInternal();
            BuildReport.Save();
        }

        [MenuItem("羊駝很忙/v6 羊駝村/2. 一鍵重置羊駝村測試場景", priority = 61)]
        public static void ResetVillageScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildReport.Begin("一鍵重置羊駝村測試場景（批 A）");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(VillageScenePath) != null)
                AssetDatabase.DeleteAsset(VillageScenePath);

            BuildInternal();
            BuildReport.Info("羊駝村測試場景已重置。按 Play：E 拿剃毛器 -> 走近 NPC 按 Space 剃毛。");
            BuildReport.Save();
        }

        [MenuItem("羊駝很忙/v6 羊駝村/3. 檢查羊駝村設置（診斷用）", priority = 62)]
        public static void ValidateVillageSetup()
        {
            var catalog = LevelSceneBuilder.LoadCatalog();
            if (catalog == null)
            {
                Debug.LogError("[v6] 找不到 Resources/GameCatalog.asset，請先執行「1. 建置佔位資產」。");
                return;
            }

            var missing = new List<string>();

            var npc = catalog.elements.FirstOrDefault(
                e => e.type == LevelElementType.WoolNpc && e.prefab != null);
            if (npc.prefab == null)
            {
                missing.Add("WoolNpc prefab（請重跑「1. 建置佔位資產」）");
            }
            else
            {
                if (npc.prefab.GetComponent<Fusion.NetworkObject>() == null)
                    missing.Add("WoolNpc prefab 沒有 NetworkObject");
                if (npc.prefab.GetComponent<Fusion.NetworkCharacterController>() == null)
                    missing.Add("WoolNpc prefab 沒有 NetworkCharacterController（會在連線時抖動）");
                if (npc.prefab.GetComponent<Fusion.NetworkTransform>() != null)
                    missing.Add("WoolNpc prefab 同時有 NetworkTransform 與 NCC —— 兩者會打架，要拿掉前者");
                if (npc.prefab.GetComponentInChildren<DeployableDevice>(true) != null)
                    missing.Add("WoolNpc 不該有 DeployHandle（牠不是裝備，不進網格）");
            }

            if (!catalog.items.Any(e => e.kind == ItemKind.Shears && e.prefab != null))
                missing.Add("剃毛器 prefab（E 鍵的隨身工具需要它）");

            if (npc.prefab != null && npc.prefab.GetComponent<Customer>() == null)
                missing.Add("WoolNpc prefab 沒有 Customer 元件 —— 顧客系統不會運作");

            // 素材箱不在 loadout 裡（選色時動態生成），要另外檢查
            var crate = catalog.elements.FirstOrDefault(
                e => e.type == LevelElementType.MaterialCrate && e.prefab != null);
            if (crate.prefab == null)
                missing.Add("素材箱 prefab（選了顏色也不會有箱子冒出來）");
            else if (crate.prefab.GetComponentInChildren<DeployableDevice>(true) == null)
                missing.Add("素材箱沒有 DeployHandle（擺不進網格、也搬不動）");

            // Village loadout 的每一台裝備都要在
            foreach (var type in StallLoadout.Village.Devices)
            {
                var entry = catalog.elements.FirstOrDefault(e => e.type == type && e.prefab != null);
                if (entry.prefab == null) { missing.Add($"Village 裝備 {type}"); continue; }
                if (entry.prefab.GetComponentInChildren<DeployableDevice>(true) == null)
                    missing.Add($"Village 裝備 {type} 沒有 DeployHandle");
            }

            // 兩套 loadout 都要放得下、預設佈局都不能衝突
            foreach (var loadout in new[] { StallLoadout.Classic, StallLoadout.Village })
            {
                if (!StallCatalog.MatFits(loadout, out int req, out int avail))
                    missing.Add($"{loadout.Name} loadout 放不下：需要 {req} 格、只有 {avail} 格");

                var occupancy = new StallGrid.Occupancy();
                foreach (var rec in loadout.DefaultLayout)
                {
                    StallGrid.RotatedFootprint(StallCatalog.Footprint(rec.DeviceType), rec.Facing,
                                               out int w, out int d);
                    var check = occupancy.Check(rec.CellX, rec.CellZ, w, d);
                    if (check != PlacementResult.Ok) missing.Add($"{loadout.Name} 預設佈局衝突：{rec} -> {check}");
                    else occupancy.OccupyFootprint(rec.CellX, rec.CellZ, w, d);
                }
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
                missing.Add($"來源場景 {SourceScenePath} 不存在 —— 請先建置擺攤測試場景");

            if (missing.Count == 0)
                Debug.Log($"[v6] 批 A 設置完整。羊毛價格：白 {GameTuning.WoolPrice(DyeColorType.White)}／" +
                          $"黃 {GameTuning.WoolPrice(DyeColorType.Yellow)}／" +
                          $"綠 {GameTuning.WoolPrice(DyeColorType.Green)}／" +
                          $"藍 {GameTuning.WoolPrice(DyeColorType.Blue)}／" +
                          $"紅 {GameTuning.WoolPrice(DyeColorType.Red)}。\n" +
                          "提醒：新 prefab 要跑一次 Tools > Fusion > Rebuild Prefab Table。");
            else
                Debug.LogError("[v6] 缺少以下項目：\n - " + string.Join("\n - ", missing));
        }

        // ------------------------------------------------------------ 建置

        private static void BuildInternal()
        {
            if (!LevelSceneBuilder.RequireCatalog()) return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                BuildReport.Error($"來源場景不存在：{SourceScenePath}。" +
                                  "請先執行「羊駝很忙 / 擺攤 / 1. 建置擺攤測試場景」。");
                return;
            }

            // --- 第一段：複製場景檔 ---
            // 只讀來源、寫到新路徑，Stall_Test 完全不會被動到
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(VillageScenePath) != null)
                AssetDatabase.DeleteAsset(VillageScenePath);

            if (!AssetDatabase.CopyAsset(SourceScenePath, VillageScenePath))
            {
                BuildReport.Error($"複製場景失敗：{SourceScenePath} -> {VillageScenePath}");
                return;
            }
            AssetDatabase.Refresh();
            BuildReport.Info($"已從 {SourceScenePath} 複製出 {VillageScenePath}（來源未被修改）");

            // --- 第二段：在複製出來的場景裡做加法 ---
            var scene = EditorSceneManager.OpenScene(VillageScenePath, OpenSceneMode.Single);

            AttachTeamStash();
            int placed = SpawnNpcs();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            RegisterInBuildSettings();
            VerifyVillageScene(placed);
        }

        /// <summary>把 TeamStash 掛到既有的 [GameSystems] 上（純加法）。</summary>
        private static void AttachTeamStash()
        {
            var systems = GameObject.Find("[GameSystems]");
            if (systems == null)
            {
                BuildReport.Error("複製出來的場景裡找不到 [GameSystems]，TeamStash 掛不上去。");
                return;
            }

            if (systems.GetComponent<TeamStash>() == null)
            {
                systems.AddComponent<TeamStash>();
                BuildReport.Line("  [GameSystems] 掛上 TeamStash");
            }

            if (systems.GetComponent<CustomerQueue>() == null)
            {
                systems.AddComponent<CustomerQueue>();
                BuildReport.Line("  [GameSystems] 掛上 CustomerQueue");
            }

            // **切成 v6 的裝備組**。沒有這一步，開箱彈出來的還是縫紉機／果汁機／人偶。
            var stall = systems.GetComponent<StallManager>();
            if (stall != null)
            {
                SetBool(stall, "_villageLoadout", true);
                BuildReport.Line("  StallManager 切換成 Village loadout（織布機／交貨窗口／輸送帶 x2）");
            }
            else
            {
                BuildReport.Error("複製出來的場景裡找不到 StallManager，loadout 切不過去。");
            }

            EditorUtility.SetDirty(systems);
        }

        /// <summary>
        /// 放一批 NPC。放在一個獨立的 [Npcs] 根物件底下，不塞進 [Level] ——
        /// [Level] 是關卡編輯器管的，NPC 不屬於那套資料，混在一起之後「重建關卡內容」會把牠們清掉。
        /// </summary>
        private static int SpawnNpcs()
        {
            var catalog = LevelSceneBuilder.LoadCatalog();
            var entry = catalog.elements.FirstOrDefault(e => e.type == LevelElementType.WoolNpc);
            if (entry.prefab == null)
            {
                BuildReport.Error("Catalog 裡沒有 WoolNpc 的 prefab，場上不會有任何 NPC。" +
                                  "請重跑「羊駝很忙 / 1. 建置佔位資產」。");
                return 0;
            }

            var existing = GameObject.Find("[Npcs]");
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject("[Npcs]");
            int placed = 0;

            for (int i = 0; i < NpcCount; i++)
            {
                // 螺旋撒點：均勻分布又不會排成一直線
                float angle = i * 137.5f * Mathf.Deg2Rad;             // 黃金角
                float radius = NpcSpreadRadius * Mathf.Sqrt((i + 0.5f) / NpcCount);
                var pos = new Vector3(Mathf.Cos(angle) * radius, 0.2f, Mathf.Sin(angle) * radius + 6f);

                var go = PrefabUtility.InstantiatePrefab(entry.prefab) as GameObject;
                if (go == null)
                {
                    BuildReport.Warn($"InstantiatePrefab 回傳 null（第 {i} 隻），改用一般 Instantiate。");
                    go = Object.Instantiate(entry.prefab);
                }

                var colour = NpcColours[i % NpcColours.Length];
                go.name = $"WoolNpc_{colour}_{i}";
                go.transform.SetParent(root.transform, false);
                go.transform.position = pos;
                go.transform.rotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);

                // 顏色走序列化欄位帶進去：[Networked] 屬性沒辦法在編輯期預先寫入，
                // WoolNpc.Spawned() 會把 _startColor 複製到 ColorRaw。
                var npc = go.GetComponent<WoolNpc>();
                if (npc != null) SetEnum(npc, "_startColor", (int)colour);

                // 用程式改過 prefab instance 的屬性之後一定要記錄成 override，
                // 否則存檔時這些改動會被還原掉
                if (PrefabUtility.IsPartOfPrefabInstance(go))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
                    foreach (var comp in go.GetComponents<Component>())
                        if (comp != null) PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
                }

                placed++;
            }

            BuildReport.Info($"放置 {placed} 隻 WoolNpc（{NpcColours.Length} 色平均分配）");
            return placed;
        }

        // ------------------------------------------------------------ 驗證

        /// <summary>
        /// 建完之後重新讀場景檔確認關鍵物件真的在。
        /// 這幾項只要缺一項，Play 下去就會是「什麼都不能做」的狀態，很難從現象反推原因。
        /// </summary>
        private static void VerifyVillageScene(int expectedNpcs)
        {
            var opened = EditorSceneManager.OpenScene(VillageScenePath, OpenSceneMode.Single);
            var problems = new List<string>();

            if (Object.FindFirstObjectByType<TeamStash>() == null)
                problems.Add("沒有 TeamStash —— 剃下來的毛沒有地方放");

            if (Object.FindFirstObjectByType<CustomerQueue>() == null)
                problems.Add("沒有 CustomerQueue —— 不會有顧客上門");

            var stallMgr = Object.FindFirstObjectByType<StallManager>();
            if (stallMgr == null) problems.Add("沒有 StallManager");
            else
            {
                var so = new SerializedObject(stallMgr);
                var p = so.FindProperty("_villageLoadout");
                if (p == null || !p.boolValue)
                    problems.Add("StallManager 沒有勾 Village loadout —— 開箱會彈出縫紉機／果汁機／人偶");
            }

            var director = Object.FindFirstObjectByType<LevelDirector>();
            if (director == null) problems.Add("沒有 LevelDirector");
            else if (!director.StallMode) problems.Add("LevelDirector 沒有勾選擺攤模式（計時會在開場就跑掉）");

            if (Object.FindFirstObjectByType<Networking.SpawnPointRegistry>() == null)
                problems.Add("沒有出生點（玩家會生在原點）");

            int npcs = Object.FindObjectsByType<WoolNpc>(FindObjectsSortMode.None).Length;
            if (npcs != expectedNpcs)
                problems.Add($"場上有 {npcs} 隻 WoolNpc，應該要有 {expectedNpcs} 隻");

            bool hasFloor = false;
            foreach (var root in opened.GetRootGameObjects())
            {
                if (root.name != LevelBuilder.RootName) continue;
                foreach (Transform child in root.transform)
                {
                    var tag = child.GetComponent<LevelElementTag>();
                    if (tag != null && tag.type == LevelElementType.FloorTile) hasFloor = true;
                }
            }
            if (!hasFloor) problems.Add("[Level] 底下沒有 FloorTile —— 玩家會一直往下掉");

            if (problems.Count > 0)
            {
                BuildReport.Error($"羊駝村測試場景驗證失敗：\n - {string.Join("\n - ", problems)}");
                return;
            }

            BuildReport.Info($"羊駝村測試場景已建立並通過驗證：{VillageScenePath}" +
                             $"（NPC {npcs} 隻、TeamStash 已掛上）");
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { BuildReport.Warn($"{target.GetType().Name} 找不到欄位 {field}"); return; }
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RegisterInBuildSettings()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(VillageScenePath) == null) return;

            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == VillageScenePath)) return;

            list.Add(new EditorBuildSettingsScene(VillageScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            BuildReport.Line($"  已加入 Build Settings：{VillageScenePath}");
        }
    }
}
