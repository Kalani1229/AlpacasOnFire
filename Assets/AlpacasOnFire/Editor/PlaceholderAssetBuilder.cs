using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
using AlpacasOnFire.Machines;
using AlpacasOnFire.Level;
using AlpacasOnFire.Networking;
using AlpacasOnFire.Player;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEditor;
using UnityEngine;
using static AlpacasOnFire.EditorTools.EditorBuildUtils;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 一鍵產生 Phase 1 需要的所有佔位資產：材質、prefab、GameCatalog。
    /// 可以重複執行（會覆蓋既有 prefab），改了配色或尺寸就重跑一次。
    /// </summary>
    public static class PlaceholderAssetBuilder
    {
        public const string PlayerLayer = "Player";
        public const string ItemLayer   = "CarriedItem";

        [MenuItem("羊駝很忙/1. 建置佔位資產（材質 + Prefab + Catalog）", priority = 0)]
        public static void BuildAll()
        {
            BuildReport.Begin("建置佔位資產");
            BuildAllInternal();
            BuildReport.Save();
        }

        public static void BuildAllInternal()
        {
            EnsureFolders();
            ResetCaches();
            int playerLayer = EnsureLayer(PlayerLayer);
            int itemLayer = EnsureLayer(ItemLayer);

            var items = new List<GameCatalog.ItemEntry>();
            var elements = new List<GameCatalog.ElementEntry>();
            var failures = new List<string>();

            // 注意：這裡刻意「不」用 AssetDatabase.StartAssetEditing()。
            // SaveAsPrefabAsset 與 CreateAsset 都會觸發匯入，跟 StartAssetEditing 混用是
            // Unity 不支援的，會在不固定的位置丟例外、讓建置中途停掉。
            GameObject player = null;
            Step("Alpaca_Player", failures, () => player = BuildPlayer(playerLayer));
            Step("Items", failures, () => BuildItems(items, itemLayer, failures));
            Step("Machines", failures, () => BuildMachines(elements, failures));
            Step("LevelPieces", failures, () => BuildLevelPieces(elements, items, failures));
            // 擺攤系統的資產一定要排在最後：AttachDeployHandles 會改寫上面剛存好的機台 prefab
            Step("Stall", failures, () => StallAssetBuilder.Build(items, elements, failures, itemLayer));

            var catalog = LoadOrCreateCatalog();
            catalog.playerPrefab = player != null ? player.GetComponent<NetworkObject>() : null;
            catalog.items = items.ToArray();
            catalog.elements = elements.ToArray();
            EditorUtility.SetDirty(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // ---- 結果報告 ----
            BuildReport.Line("--- Catalog 內容 ---");
            BuildReport.Line($"playerPrefab = {(catalog.playerPrefab != null ? catalog.playerPrefab.name : "null")}");
            foreach (var e in catalog.items)
                BuildReport.Line($"  item {e.kind} -> {(e.prefab != null ? e.prefab.name : "null")}");
            foreach (var e in catalog.elements)
                BuildReport.Line($"  element {e.type} -> {(e.prefab != null ? e.prefab.name : "null")}");

            if (failures.Count > 0)
                BuildReport.Error($"建置完成，但有 {failures.Count} 項失敗：" + string.Join(", ", failures));
            else
                BuildReport.Info($"佔位資產建置完成：玩家 prefab {(catalog.playerPrefab != null ? "OK" : "缺少")}、" +
                                 $"物品 {items.Count} 種、關卡元件 {elements.Count} 種。");

            Selection.activeObject = catalog;
        }

        private static void AddItem(List<GameCatalog.ItemEntry> list, List<string> failures,
                                    string label, System.Func<GameCatalog.ItemEntry> build)
        {
            try { list.Add(build()); BuildReport.Line($"  OK  {label}"); }
            catch (System.Exception e)
            {
                failures.Add(label);
                BuildReport.Exception($"建置 {label}", e);
            }
        }

        private static void AddElement(List<GameCatalog.ElementEntry> list, List<string> failures,
                                       string label, System.Func<GameCatalog.ElementEntry> build)
        {
            try { list.Add(build()); BuildReport.Line($"  OK  {label}"); }
            catch (System.Exception e)
            {
                failures.Add(label);
                BuildReport.Exception($"建置 {label}", e);
            }
        }

        /// <summary>單一建置步驟：失敗就記下來但不中斷整個流程。</summary>
        private static void Step(string label, List<string> failures, System.Action action)
        {
            try
            {
                action();
            }
            catch (System.Exception e)
            {
                failures.Add(label);
                BuildReport.Exception($"建置 {label}", e);
            }
        }

        [MenuItem("羊駝很忙/4. 檢查設置（診斷用）", priority = 4)]
        public static void ValidateSetup()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>($"{ResourcesDir}/GameCatalog.asset");
            if (catalog == null)
            {
                Debug.LogError("[羊駝很忙] 找不到 Resources/GameCatalog.asset。請先執行「1. 建置佔位資產」。");
                return;
            }

            var missing = new List<string>();
            if (catalog.playerPrefab == null) missing.Add("玩家 prefab");

            foreach (ItemKind kind in System.Enum.GetValues(typeof(ItemKind)))
            {
                if (kind == ItemKind.None) continue;
                bool found = false;
                foreach (var e in catalog.items) if (e.kind == kind && e.prefab != null) found = true;
                if (!found) missing.Add($"物品 {kind}");
            }

            foreach (LevelElementType type in System.Enum.GetValues(typeof(LevelElementType)))
            {
                bool found = false;
                foreach (var e in catalog.elements) if (e.type == type && e.prefab != null) found = true;
                if (!found) missing.Add($"關卡元件 {type}");
            }

            if (missing.Count == 0)
                Debug.Log("[羊駝很忙] Catalog 完整，可以建置場景了。");
            else
                Debug.LogError("[羊駝很忙] Catalog 缺少以下項目，請重跑「1. 建置佔位資產」：\n - " +
                               string.Join("\n - ", missing));
        }

        private static GameCatalog LoadOrCreateCatalog()
        {
            string path = $"{ResourcesDir}/GameCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>(path);
            if (catalog != null) return catalog;

            catalog = ScriptableObject.CreateInstance<GameCatalog>();
            AssetDatabase.CreateAsset(catalog, path);
            return catalog;
        }

        // ---------------------------------------------------------------- 玩家

        private static GameObject BuildPlayer(int layer)
        {
            var mat = Mat("M_Alpaca", PlaceholderPalette.PlayerColor(0));
            var garmentMat = Mat("M_Garment", PlaceholderPalette.Wool);
            var fleeceMat = Mat("M_Wool", PlaceholderPalette.Wool);

            var root = new GameObject("Alpaca_Player");

            // 身體：膠囊，高度 = 羊駝站立高度
            var body = Prim(PrimitiveType.Capsule, "Body", root.transform,
                new Vector3(0f, GameTuning.AlpacaHeight * 0.5f, 0f),
                new Vector3(GameTuning.AlpacaRadius * 2f, GameTuning.AlpacaHeight * 0.5f, GameTuning.AlpacaRadius * 2f),
                mat, keepCollider: false);

            // 面向指示（讓佔位角色看得出朝向）
            Prim(PrimitiveType.Cube, "Snout", body.transform, new Vector3(0f, 0.55f, 0.55f),
                 new Vector3(0.45f, 0.35f, 0.55f), mat, keepCollider: false);

            var garment = Prim(PrimitiveType.Cube, "GarmentVisual", root.transform,
                new Vector3(0f, 1.0f, 0f), new Vector3(0.85f, 0.55f, 0.75f), garmentMat, keepCollider: false);
            garment.GetComponent<Renderer>().enabled = false;

            var fleece = Prim(PrimitiveType.Sphere, "FleeceIndicator", root.transform,
                new Vector3(0f, 1.72f, 0f), Vector3.one * 0.42f, fleeceMat, keepCollider: false);

            var head  = Empty("HeadAnchor", root.transform, new Vector3(0f, GameTuning.EyeHeight, 0f));
            var hand  = Empty("HandAnchor", root.transform, new Vector3(0.25f, 1.05f, 0.6f));
            var catchA = Empty("CatchAnchor", root.transform, new Vector3(0f, 1.0f, 0f));
            var garmentAnchor = Empty("GarmentAnchor", root.transform, new Vector3(0f, 1.0f, 0f));

            var cc = root.AddComponent<CharacterController>();
            cc.height = GameTuning.AlpacaHeight;
            cc.radius = GameTuning.AlpacaRadius;
            cc.center = new Vector3(0f, GameTuning.AlpacaHeight * 0.5f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.02f;

            if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();

            // 用 Fusion 內建的 NetworkCharacterController，而不是 NetworkTransform + 原生 CharacterController。
            // 原因：Fusion 重模擬前必須先 disable/enable CharacterController，transform 的新位置才會被它認得；
            // 少了這步，用戶端會在幾秒後開始抖動並瞬間大步移動。
            // 注意 NCC 本身就是 NetworkTRSP，不可以再掛 NetworkTransform，否則兩者會互相打架。
            var ncc = root.GetComponent<NetworkCharacterController>();
            if (ncc == null) ncc = root.AddComponent<NetworkCharacterController>();
            ncc.gravity       = -GameTuning.Gravity;
            ncc.acceleration  = GameTuning.MoveAcceleration;
            ncc.braking       = GameTuning.MoveBraking;
            ncc.maxSpeed      = GameTuning.MoveSpeed;
            ncc.rotationSpeed = 0f;   // 共用朝向：不讓它自動轉向移動方向

            var pc = root.AddComponent<PlayerController>();
            root.AddComponent<PlayerCarry>();
            // 擺攤系統：每個玩家自己的放置狀態。PlayerController 一行都沒動，只是多掛一個元件。
            root.AddComponent<Stall.PlayerStallAgent>();

            SetRef(pc, "_handAnchor", hand.transform);
            SetRef(pc, "_headAnchor", head.transform);
            SetRef(pc, "_catchAnchor", catchA.transform);
            SetRef(pc, "_garmentAnchor", garmentAnchor.transform);
            SetRef(pc, "_bodyRenderer", body.GetComponent<Renderer>());
            SetRef(pc, "_garmentRenderer", garment.GetComponent<Renderer>());
            SetRef(pc, "_fleeceIndicator", fleece.GetComponent<Renderer>());

            // 擋住畫布時會整隻換成這個半透明材質
            SetRef(pc, "_fadeMaterial",
                   TransparentMat("M_AlpacaFade", PlaceholderPalette.PlayerColor(0),
                                  GameTuning.LocalPlayerFadeAlpha));

            SetLayerRecursive(root, layer);
            return SavePrefab(root, "Alpaca_Player");
        }

        // ---------------------------------------------------------------- 物品

        private static void BuildItems(List<GameCatalog.ItemEntry> outItems, int layer, List<string> failures)
        {
            AddItem(outItems, failures, "Item_Wool", () => Item(ItemKind.Wool, "Item_Wool", layer, root =>
            {
                var mat = Mat("M_Wool", PlaceholderPalette.Wool);
                var s = Prim(PrimitiveType.Sphere, "Blob", root.transform, Vector3.zero, Vector3.one * 0.32f, mat);
                return (typeof(CarriableItem), new Renderer[] { s.GetComponent<Renderer>() });
            }));

            AddItem(outItems, failures, "Item_DyeMaterial", () => Item(ItemKind.DyeMaterial, "Item_DyeMaterial", layer, root =>
            {
                var mat = Mat("M_Dye", PlaceholderPalette.Dye(DyeColorType.Red));
                var s = Prim(PrimitiveType.Sphere, "Blob", root.transform, Vector3.zero, Vector3.one * 0.3f, mat);
                return (typeof(CarriableItem), new Renderer[] { s.GetComponent<Renderer>() });
            }));

            AddItem(outItems, failures, "Item_HairTonic", () => Item(ItemKind.HairTonic, "Item_HairTonic", layer, root =>
            {
                var mat = Mat("M_HairTonic", PlaceholderPalette.HairTonic);
                var s = Prim(PrimitiveType.Sphere, "Blob", root.transform, Vector3.zero, Vector3.one * 0.3f, mat);
                return (typeof(CarriableItem), new Renderer[] { s.GetComponent<Renderer>() });
            }));

            // 染劑罐 = 顏料罐 = 畫筆。果汁機榨出來就直接拿去刷衣服，沒有中間工具。
            AddItem(outItems, failures, "Item_DyeCanister", () => Item(ItemKind.DyeCanister, "Item_DyeCanister", layer, root =>
            {
                var mat = Mat("M_Dye", PlaceholderPalette.Dye(DyeColorType.Red));
                var s = Prim(PrimitiveType.Cylinder, "Canister", root.transform, Vector3.zero,
                             new Vector3(0.26f, 0.2f, 0.26f), mat);

                // 罐子裡的顏料量：用高度表示剩多少
                var level = Prim(PrimitiveType.Cylinder, "PaintLevel", root.transform,
                                 new Vector3(0f, 0.02f, 0f), new Vector3(0.2f, 0.16f, 0.2f), mat,
                                 keepCollider: false);

                var canister = root.AddComponent<DyeCanisterTool>();
                SetRef(canister, "_levelIndicator", level.GetComponent<Renderer>());
                return (typeof(DyeCanisterTool), new Renderer[] { s.GetComponent<Renderer>() });
            }, componentAlreadyAdded: true));

            AddItem(outItems, failures, "Item_Accessory", () => Item(ItemKind.Accessory, "Item_Accessory", layer, root =>
            {
                var mat = Mat("M_Accessory", PlaceholderPalette.Accessory);
                var s = Prim(PrimitiveType.Sphere, "Bead", root.transform, Vector3.zero, Vector3.one * 0.22f, mat);
                return (typeof(AccessoryItem), new Renderer[] { s.GetComponent<Renderer>() });
            }));

            AddItem(outItems, failures, "Item_Garment", () => Item(ItemKind.Garment, "Item_Garment", layer, root =>
            {
                var mat = Mat("M_Garment", PlaceholderPalette.Wool);
                var s = Prim(PrimitiveType.Cube, "Cloth", root.transform, Vector3.zero,
                             GameTuning.GarmentItemSize, mat);
                return (typeof(GarmentItem), new Renderer[] { s.GetComponent<Renderer>() });
            }));

            AddItem(outItems, failures, "Item_Box", () => Item(ItemKind.Box, "Item_Box", layer, root =>
            {
                var mat = Mat("M_Box", PlaceholderPalette.Box);
                var s = Prim(PrimitiveType.Cube, "Crate", root.transform, Vector3.zero,
                             new Vector3(0.62f, 0.34f, 0.52f), mat);

                var indicatorMat = Mat("M_BoxContent", PlaceholderPalette.Wool);
                var ind = Prim(PrimitiveType.Cube, "ContentIndicator", root.transform, new Vector3(0f, 0.22f, 0f),
                               new Vector3(0.34f, 0.1f, 0.28f), indicatorMat, keepCollider: false);
                ind.GetComponent<Renderer>().enabled = false;

                var box = root.AddComponent<BoxItem>();
                SetRef(box, "_contentIndicator", ind.GetComponent<Renderer>());
                return (typeof(BoxItem), new Renderer[] { s.GetComponent<Renderer>() });
            }, componentAlreadyAdded: true));

            AddItem(outItems, failures, "Item_Shears", () => Item(ItemKind.Shears, "Item_Shears", layer, root =>
            {
                var body = Mat("M_ToolBody", PlaceholderPalette.ToolBody);
                var blade = Mat("M_ShearsBlade", PlaceholderPalette.ShearsBlade);
                var b = Prim(PrimitiveType.Cube, "Handle", root.transform, Vector3.zero,
                             new Vector3(0.12f, 0.12f, 0.46f), body);
                Prim(PrimitiveType.Cube, "Blade", root.transform, new Vector3(0f, 0f, 0.34f),
                     new Vector3(0.1f, 0.06f, 0.28f), blade, keepCollider: false);
                return (typeof(ShearsTool), new Renderer[] { b.GetComponent<Renderer>() });
            }));

        }

        private static GameCatalog.ItemEntry Item(ItemKind kind, string prefabName, int layer,
            System.Func<GameObject, (System.Type type, Renderer[] tint)> build,
            bool componentAlreadyAdded = false)
        {
            var root = new GameObject(prefabName);
            var (type, tint) = build(root);

            // CarriableItem 有 [RequireComponent(NetworkObject)]，可能已經自動加過了
            if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();
            if (root.GetComponent<NetworkTransform>() == null) root.AddComponent<NetworkTransform>();

            var item = componentAlreadyAdded
                ? (CarriableItem)root.GetComponent(type)
                : (CarriableItem)root.AddComponent(type);

            SetEnum(item, "_kind", (int)kind);
            SetRefArray(item, "_tintTargets", tint);
            SetRef(item, "_visualRoot", root.transform);

            if (item is AccessoryItem acc)
                SetEnum(acc, "_accessoryType", (int)AccessoryType.Button);

            SetLayerRecursive(root, layer);
            var prefab = SavePrefab(root, prefabName);
            return new GameCatalog.ItemEntry { kind = kind, prefab = prefab.GetComponent<NetworkObject>() };
        }

        // ---------------------------------------------------------------- 機台

        private static void BuildMachines(List<GameCatalog.ElementEntry> outElements, List<string> failures)
        {
            AddElement(outElements, failures, "SewingMachine", BuildSewingMachine);
            AddElement(outElements, failures, "Juicer", BuildJuicer);
            AddElement(outElements, failures, "Mannequin", BuildMannequin);
            AddElement(outElements, failures, "Mailbox", () => BuildSimpleStation<Mailbox>(
                LevelElementType.Mailbox, "Machine_Mailbox", "M_Mailbox", PlaceholderPalette.Mailbox, 1.4f));
            AddElement(outElements, failures, "BoxDispenser", () => BuildSimpleStation<BoxDispenser>(
                LevelElementType.BoxDispenser, "Machine_BoxDispenser", "M_BoxDispenser",
                PlaceholderPalette.BoxDispenser, 1.2f));
            AddElement(outElements, failures, "RecyclingMachine", () => BuildSimpleStation<RecyclingMachine>(
                LevelElementType.RecyclingMachine, "Machine_Recycling", "M_Recycling",
                PlaceholderPalette.Recycling, GameTuning.MachineHeight));
            AddElement(outElements, failures, "AccessoryDispenser", BuildAccessoryDispenser);

            // 四種顏色的染料點共用同一個 prefab，實際顏色由 LevelElementRecord.variant 決定
            AddElement(outElements, failures, "DyeSource", BuildDyeSource);
            var dyePrefab = outElements.Count > 0 && outElements[^1].type == LevelElementType.DyeSourceRed
                          ? outElements[^1].prefab : null;
            if (dyePrefab != null)
            {
                outElements.Add(new GameCatalog.ElementEntry { type = LevelElementType.DyeSourceBlue,   prefab = dyePrefab });
                outElements.Add(new GameCatalog.ElementEntry { type = LevelElementType.DyeSourceGreen,  prefab = dyePrefab });
                outElements.Add(new GameCatalog.ElementEntry { type = LevelElementType.DyeSourceYellow, prefab = dyePrefab });
            }
        }

        private static GameObject MachineShell(string name, string matName, Color color, float height,
                                               out GameObject body, out Transform interact, out Transform output)
        {
            var mat = Mat(matName, color);
            var root = new GameObject(name);

            body = Prim(PrimitiveType.Cube, "Body", root.transform,
                        new Vector3(0f, height * 0.5f, 0f),
                        new Vector3(GameTuning.MachineFootprint, height, GameTuning.MachineFootprint), mat);

            interact = Empty("InteractionAnchor", root.transform, new Vector3(0f, height * 0.6f, -0.75f)).transform;
            output = Empty("OutputAnchor", root.transform, new Vector3(0f, height + 0.25f, -0.85f)).transform;
            return root;
        }

        private static GameCatalog.ElementEntry BuildSewingMachine()
        {
            var root = MachineShell("Machine_SewingMachine", "M_SewingMachine", PlaceholderPalette.SewingMachine,
                                    GameTuning.MachineHeight, out _, out var interact, out var output);

            var lightMat = Mat("M_StatusLight", new Color(0.3f, 0.9f, 0.4f));
            var barMat = Mat("M_ProgressBar", new Color(0.95f, 0.8f, 0.2f));
            var woolMat = Mat("M_Wool", PlaceholderPalette.Wool);

            var status = Prim(PrimitiveType.Sphere, "StatusLight", root.transform,
                new Vector3(0.45f, GameTuning.MachineHeight + 0.12f, -0.45f), Vector3.one * 0.18f, lightMat, false);

            var bar = Prim(PrimitiveType.Cube, "ProgressBar", root.transform,
                new Vector3(0f, GameTuning.MachineHeight + 0.2f, -0.6f),
                new Vector3(1f, 0.09f, 0.09f), barMat, false);
            bar.SetActive(false);

            var slots = new Renderer[GameTuning.SewingWoolRequired];
            for (int i = 0; i < slots.Length; i++)
            {
                var s = Prim(PrimitiveType.Sphere, $"WoolSlot{i}", root.transform,
                    new Vector3(-0.36f + i * 0.36f, GameTuning.MachineHeight + 0.12f, 0.2f),
                    Vector3.one * 0.22f, woolMat, false);
                s.GetComponent<Renderer>().enabled = false;
                slots[i] = s.GetComponent<Renderer>();
            }

            root.AddComponent<NetworkObject>();
            var m = root.AddComponent<SewingMachine>();
            SetRef(m, "_interactionAnchor", interact);
            SetRef(m, "_outputAnchor", output);
            SetRef(m, "_progressBar", bar.transform);
            SetRef(m, "_statusLight", status.GetComponent<Renderer>());
            SetRefArray(m, "_woolSlots", slots);

            var prefab = SavePrefab(root, "Machine_SewingMachine");
            return new GameCatalog.ElementEntry { type = LevelElementType.SewingMachine, prefab = prefab };
        }

        private static GameCatalog.ElementEntry BuildJuicer()
        {
            var root = MachineShell("Machine_Juicer", "M_Juicer", PlaceholderPalette.Juicer,
                                    GameTuning.MachineHeight, out _, out var interact, out var output);

            var lightMat = Mat("M_StatusLight", new Color(0.3f, 0.9f, 0.4f));
            var barMat = Mat("M_ProgressBar", new Color(0.95f, 0.8f, 0.2f));

            var status = Prim(PrimitiveType.Sphere, "StatusLight", root.transform,
                new Vector3(0.45f, GameTuning.MachineHeight + 0.12f, -0.45f), Vector3.one * 0.18f, lightMat, false);
            var bar = Prim(PrimitiveType.Cube, "ProgressBar", root.transform,
                new Vector3(0f, GameTuning.MachineHeight + 0.2f, -0.6f),
                new Vector3(1f, 0.09f, 0.09f), barMat, false);
            bar.SetActive(false);

            root.AddComponent<NetworkObject>();
            var m = root.AddComponent<Juicer>();
            SetRef(m, "_interactionAnchor", interact);
            SetRef(m, "_outputAnchor", output);
            SetRef(m, "_progressBar", bar.transform);
            SetRef(m, "_statusLight", status.GetComponent<Renderer>());

            var prefab = SavePrefab(root, "Machine_Juicer");
            return new GameCatalog.ElementEntry { type = LevelElementType.Juicer, prefab = prefab };
        }

        private static GameCatalog.ElementEntry BuildMannequin()
        {
            var mat = Mat("M_Mannequin", PlaceholderPalette.Mannequin);
            var garmentMat = Mat("M_Garment", PlaceholderPalette.Wool);
            var barMat = Mat("M_PaintBar", new Color(0.2f, 0.85f, 0.9f));

            var root = new GameObject("Machine_Mannequin");
            float h = GameTuning.MachineHeight;

            Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, h * 0.5f, 0f),
                 new Vector3(GameTuning.MachineFootprint, h, GameTuning.MachineFootprint), mat);

            var garmentAnchor = Empty("GarmentAnchor", root.transform, new Vector3(0f, h * 0.6f, 0f));
            // 畫布：一片正方形的薄板，像掛起來的一塊布。
            // 往操作面推出來，不然整片會埋在人偶本體裡看不到。
            // 要留著碰撞體 —— 玩家是用射線刷上去的，沒有碰撞體就刷不到。
            var garmentPos = new Vector3(0f, GameTuning.GarmentHostCenterY, -GameTuning.GarmentFrontOffset);
            var garment = Prim(PrimitiveType.Cube, "GarmentVisual", root.transform,
                garmentPos, GameTuning.GarmentOnHostSize, garmentMat, keepCollider: true);
            garment.GetComponent<Renderer>().enabled = false;

            // 完成提示的外框：比畫布大一圈、擺在畫布正後方，只露出邊。
            // 塗到門檻時發光三秒，這是玩家唯一會知道「這件算完成了」的訊號。
            var frameMat = TransparentMat("M_PaintDoneFrame", new Color(1f, 0.95f, 0.6f), 1f);
            var frameSize = new Vector3(GameTuning.GarmentOnHostSize.x * 1.10f,
                                        GameTuning.GarmentOnHostSize.y * 1.10f,
                                        0.06f);
            var frame = Prim(PrimitiveType.Cube, "CompletionFrame", root.transform,
                garmentPos + new Vector3(0f, 0f, GameTuning.GarmentOnHostSize.z * 0.5f + 0.03f),
                frameSize, frameMat, keepCollider: false);
            frame.GetComponent<Renderer>().enabled = false;

            var bar = Prim(PrimitiveType.Cube, "PaintProgress", root.transform,
                new Vector3(0f, h + 0.2f, -0.6f), new Vector3(1f, 0.09f, 0.09f), barMat, false);
            bar.SetActive(false);

            var interact = Empty("InteractionAnchor", root.transform, new Vector3(0f, h * 0.6f, -0.75f));

            root.AddComponent<NetworkObject>();

            // 塗抹表面：Mannequin 有 [RequireComponent]，這裡先加好並接上引用
            var paint = root.AddComponent<GarmentPaintSurface>();
            SetRef(paint, "_garmentRenderer", garment.GetComponent<Renderer>());
            SetRef(paint, "_paintCollider", garment.GetComponent<Collider>());

            var m = root.AddComponent<Mannequin>();
            SetRef(m, "_interactionAnchor", interact.transform);
            SetRef(m, "_garmentAnchor", garmentAnchor.transform);
            SetRef(m, "_garmentRenderer", garment.GetComponent<Renderer>());
            SetRef(m, "_paintProgressBar", bar.transform);
            SetRef(m, "_completionFrame", frame.GetComponent<Renderer>());

            var prefab = SavePrefab(root, "Machine_Mannequin");
            return new GameCatalog.ElementEntry { type = LevelElementType.Mannequin, prefab = prefab };
        }

        private static GameCatalog.ElementEntry BuildSimpleStation<T>(LevelElementType type, string prefabName,
            string matName, Color color, float height) where T : Interaction.NetworkInteractable
        {
            var root = MachineShell(prefabName, matName, color, height, out _, out var interact, out _);
            root.AddComponent<NetworkObject>();
            var m = root.AddComponent<T>();
            SetRef(m, "_interactionAnchor", interact);

            var prefab = SavePrefab(root, prefabName);
            return new GameCatalog.ElementEntry { type = type, prefab = prefab };
        }

        private static GameCatalog.ElementEntry BuildAccessoryDispenser()
        {
            var root = MachineShell("Machine_AccessoryDispenser", "M_AccessoryStand", PlaceholderPalette.Accessory,
                                    1.2f, out _, out var interact, out _);
            root.AddComponent<NetworkObject>();
            var m = root.AddComponent<AccessoryDispenser>();
            SetRef(m, "_interactionAnchor", interact);
            SetEnum(m, "_accessory", (int)AccessoryType.Button);

            var prefab = SavePrefab(root, "Machine_AccessoryDispenser");
            return new GameCatalog.ElementEntry { type = LevelElementType.AccessoryDispenser, prefab = prefab };
        }

        private static GameCatalog.ElementEntry BuildDyeSource()
        {
            var mat = Mat("M_Dye", PlaceholderPalette.Dye(DyeColorType.Red));
            var standMat = Mat("M_DyeStand", new Color(0.35f, 0.33f, 0.30f));

            var root = new GameObject("Node_DyeSource");
            Prim(PrimitiveType.Cylinder, "Stand", root.transform, new Vector3(0f, 0.2f, 0f),
                 new Vector3(0.9f, 0.2f, 0.9f), standMat);
            var blob = Prim(PrimitiveType.Sphere, "Blob", root.transform, new Vector3(0f, 0.62f, 0f),
                            Vector3.one * 0.55f, mat);
            var interact = Empty("InteractionAnchor", root.transform, new Vector3(0f, 0.6f, 0f));

            root.AddComponent<NetworkObject>();
            var node = root.AddComponent<DyeSourceNode>();
            SetRef(node, "_interactionAnchor", interact.transform);
            SetRef(node, "_blobRenderer", blob.GetComponent<Renderer>());
            SetEnum(node, "_color", (int)DyeColorType.Red);

            var prefab = SavePrefab(root, "Node_DyeSource");
            return new GameCatalog.ElementEntry { type = LevelElementType.DyeSourceRed, prefab = prefab };
        }

        // ---------------------------------------------------------------- 場景零件

        private static void BuildLevelPieces(List<GameCatalog.ElementEntry> outElements,
                                             List<GameCatalog.ItemEntry> items, List<string> failures)
        {
            // 出生點
            Step("Level_PlayerSpawn", failures, () =>
            {
                var root = new GameObject("Level_PlayerSpawn");
                root.AddComponent<SpawnPointRegistry>();
                var prefab = SavePrefab(root, "Level_PlayerSpawn");
                outElements.Add(new GameCatalog.ElementEntry { type = LevelElementType.PlayerSpawn, prefab = prefab });
            });

            // 地板 10 x 0.2 x 10
            Step("Level_FloorTile", failures, () =>
            {
                var mat = Mat("M_Floor", PlaceholderPalette.Floor);
                var root = new GameObject("Level_FloorTile");
                Prim(PrimitiveType.Cube, "Tile", root.transform, Vector3.zero, new Vector3(10f, 0.2f, 10f), mat);
                var prefab = SavePrefab(root, "Level_FloorTile");
                outElements.Add(new GameCatalog.ElementEntry { type = LevelElementType.FloorTile, prefab = prefab });
            });

            // 牆 1 x 3 x 1（用 record.scale 拉長）
            Step("Level_Wall", failures, () =>
            {
                var mat = Mat("M_Wall", PlaceholderPalette.Wall);
                var root = new GameObject("Level_Wall");
                Prim(PrimitiveType.Cube, "Wall", root.transform, new Vector3(0f, 1.5f, 0f), new Vector3(1f, 3f, 1f), mat);
                var prefab = SavePrefab(root, "Level_Wall");
                outElements.Add(new GameCatalog.ElementEntry { type = LevelElementType.Wall, prefab = prefab });
            });

            // NPC 佔位（Phase 2 才有 AI）
            Step("Level_Npc", failures, () =>
            {
                var mat = Mat("M_Npc", PlaceholderPalette.Npc);
                var root = new GameObject("Level_NpcPlaceholder");
                Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, 0.9f, 0f),
                     new Vector3(0.8f, 1.8f, 0.8f), mat);
                Prim(PrimitiveType.Cube, "Face", root.transform, new Vector3(0f, 1.3f, 0.45f),
                     new Vector3(0.4f, 0.25f, 0.1f), Mat("M_NpcFace", new Color(0.3f, 0.3f, 0.32f)), false);
                var prefab = SavePrefab(root, "Level_NpcPlaceholder");
                outElements.Add(new GameCatalog.ElementEntry { type = LevelElementType.Npc, prefab = prefab });
            });

            // 訂單板錨點（世界空間的視覺參考，HUD 本身是螢幕空間）
            Step("Level_OrderBoardAnchor", failures, () =>
            {
                var mat = Mat("M_OrderBoard", new Color(0.85f, 0.82f, 0.75f));
                var root = new GameObject("Level_OrderBoardAnchor");
                Prim(PrimitiveType.Cube, "Board", root.transform, new Vector3(0f, 1.6f, 0f),
                     new Vector3(2.2f, 1.4f, 0.14f), mat);
                var prefab = SavePrefab(root, "Level_OrderBoardAnchor");
                outElements.Add(new GameCatalog.ElementEntry
                    { type = LevelElementType.OrderBoardAnchor, prefab = prefab });
            });

            // 工具（剃毛器 / 噴槍）也能被關卡編輯器放置：直接沿用物品 prefab
            foreach (var it in items)
            {
                if (it.prefab == null) continue;
                if (it.kind == ItemKind.Shears)
                    outElements.Add(new GameCatalog.ElementEntry
                        { type = LevelElementType.Shears, prefab = it.prefab.gameObject });
            }
        }
    }
}
