using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEditor;
using UnityEngine;
using static AlpacasOnFire.EditorTools.EditorBuildUtils;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 擺攤系統的佔位資產。刻意獨立成一個檔案，讓 PlaceholderAssetBuilder 的改動
    /// 只剩下「呼叫這裡」那一行，之後對照 Phase 1 的版本很容易看出差異。
    ///
    /// 三個 Editor 端的地雷（都是 Phase 1 踩過的）在這裡一律遵守：
    ///  - 不用 AssetDatabase.StartAssetEditing() 包住 SaveAsPrefabAsset / CreateAsset
    ///  - 不跨場景持有 ScriptableObject 參考
    ///  - 用 using static 匯入時避開跟 Unity 型別同名的方法（這裡沒有 Material(...) 這種）
    /// </summary>
    public static class StallAssetBuilder
    {
        /// <summary>把擺攤系統的物品與元件加進 Catalog。由 PlaceholderAssetBuilder 呼叫。</summary>
        public static void Build(List<GameCatalog.ItemEntry> outItems,
                                 List<GameCatalog.ElementEntry> outElements,
                                 List<string> failures,
                                 int itemLayer)
        {
            Add(failures, "Item_Suitcase", () => outItems.Add(BuildSuitcase(itemLayer)));
            Add(failures, "Machine_DeliveryCounter", () => outElements.Add(BuildDeliveryCounter()));
            Add(failures, "Machine_Conveyor", () => outElements.Add(BuildConveyor()));
            Add(failures, "Machine_ToolRack", () => BuildToolRacks(outElements));
            Add(failures, "Stall_ServiceBell", () => outElements.Add(BuildServiceBell()));
            Add(failures, "Level_SuitcaseSpawn", () => outElements.Add(BuildSuitcaseSpawnMarker()));

            // 縫紉機／果汁機／人偶要能被擺出來與收回：補一個 DeployHandle 子物件
            Add(failures, "DeployHandle", () => AttachDeployHandles(outElements));
        }

        private static void Add(List<string> failures, string label, System.Action action)
        {
            try
            {
                action();
                BuildReport.Line($"  OK  {label}");
            }
            catch (System.Exception e)
            {
                failures.Add(label);
                BuildReport.Exception($"建置 {label}", e);
            }
        }

        // ---------------------------------------------------------------- 手提箱

        private static GameCatalog.ItemEntry BuildSuitcase(int layer)
        {
            var shellMat = Mat("M_Suitcase", new Color(0.42f, 0.28f, 0.18f));
            var trimMat = Mat("M_SuitcaseTrim", new Color(0.85f, 0.72f, 0.35f));
            var lightMat = Mat("M_SuitcaseLight", new Color(0.35f, 0.35f, 0.38f));

            var root = new GameObject("Item_Suitcase");

            var body = Prim(PrimitiveType.Cube, "Shell", root.transform, new Vector3(0f, 0.16f, 0f),
                            new Vector3(0.78f, 0.32f, 0.5f), shellMat);

            // 箱蓋：獨立的樞紐，開箱時掀起來
            var hinge = Empty("LidHinge", root.transform, new Vector3(0f, 0.32f, -0.25f));
            Prim(PrimitiveType.Cube, "Lid", hinge.transform, new Vector3(0f, 0.03f, 0.25f),
                 new Vector3(0.78f, 0.06f, 0.5f), shellMat, keepCollider: false);

            Prim(PrimitiveType.Cube, "Handle", root.transform, new Vector3(0f, 0.38f, 0f),
                 new Vector3(0.26f, 0.06f, 0.06f), trimMat, keepCollider: false);

            var light = Prim(PrimitiveType.Sphere, "StateLight", root.transform,
                             new Vector3(0.3f, 0.34f, 0.2f), Vector3.one * 0.11f, lightMat, keepCollider: false);

            // ★ 關鍵：一顆 trigger 碰撞體，讓手提箱「拿在手上時」仍然能被準心選到。
            //   CarriableItem.UpdateColliders() 只會關掉非 trigger 的碰撞體，
            //   所以這顆會一直有效，開箱的 Space 才有東西可以打。
            var probe = new GameObject("HeldProbe");
            probe.transform.SetParent(root.transform, false);
            probe.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            var probeCol = probe.AddComponent<SphereCollider>();
            probeCol.isTrigger = true;
            probeCol.radius = 0.45f;

            if (root.GetComponent<NetworkObject>() == null) root.AddComponent<NetworkObject>();
            if (root.GetComponent<NetworkTransform>() == null) root.AddComponent<NetworkTransform>();

            var item = root.AddComponent<SuitcaseItem>();
            SetEnum(item, "_kind", (int)ItemKind.Suitcase);
            SetRef(item, "_visualRoot", body.transform);
            SetRef(item, "_lidVisual", hinge.transform);
            SetRef(item, "_stateLight", light.GetComponent<Renderer>());
            // _tintTargets 刻意留空：手提箱要保留 prefab 的木箱配色，不跟著 Spec 變色

            SetLayerRecursive(root, layer);
            var prefab = SavePrefab(root, "Item_Suitcase");
            return new GameCatalog.ItemEntry
            {
                kind = ItemKind.Suitcase,
                prefab = prefab.GetComponent<NetworkObject>(),
            };
        }

        // ---------------------------------------------------------------- 交貨窗口

        private static GameCatalog.ElementEntry BuildDeliveryCounter()
        {
            var bodyMat = Mat("M_DeliveryCounter", PlaceholderPalette.Mailbox);
            var topMat = Mat("M_DeliveryCounterTop", new Color(0.86f, 0.82f, 0.72f));
            var lightMat = Mat("M_StatusLight", new Color(0.3f, 0.9f, 0.4f));

            var root = new GameObject("Machine_DeliveryCounter");
            float h = GameTuning.DeliveryCounterHeight;
            float w = GameTuning.MachineFootprint;

            // 櫃檯本體
            Prim(PrimitiveType.Cube, "Body", root.transform, new Vector3(0f, h * 0.5f, 0f),
                 new Vector3(w, h, w * 0.7f), bodyMat);

            // 檯面往顧客側（+Z）伸出去，讓「窗口朝向」一眼看得出來
            Prim(PrimitiveType.Cube, "Counter", root.transform, new Vector3(0f, h + 0.05f, 0.22f),
                 new Vector3(w * 1.15f, 0.1f, w * 1.05f), topMat, keepCollider: false);

            // 兩根窗框，框出「窗口」
            Prim(PrimitiveType.Cube, "FrameL", root.transform, new Vector3(-w * 0.5f, h + 0.55f, 0f),
                 new Vector3(0.1f, 1.0f, 0.1f), bodyMat, keepCollider: false);
            Prim(PrimitiveType.Cube, "FrameR", root.transform, new Vector3(w * 0.5f, h + 0.55f, 0f),
                 new Vector3(0.1f, 1.0f, 0.1f), bodyMat, keepCollider: false);
            Prim(PrimitiveType.Cube, "FrameTop", root.transform, new Vector3(0f, h + 1.05f, 0f),
                 new Vector3(w * 1.1f, 0.1f, 0.1f), bodyMat, keepCollider: false);

            var light = Prim(PrimitiveType.Sphere, "StatusLight", root.transform,
                             new Vector3(0f, h + 0.95f, 0.1f), Vector3.one * 0.16f, lightMat, keepCollider: false);

            // 玩家站在 -Z 側操作，顧客排在 +Z 側
            var interact = Empty("InteractionAnchor", root.transform, new Vector3(0f, h * 0.8f, -0.6f));
            var queue = Empty("CustomerQueueAnchor", root.transform,
                              new Vector3(0f, 0f, GameTuning.CustomerQueueDistance));

            root.AddComponent<NetworkObject>();
            var counter = root.AddComponent<DeliveryCounter>();
            SetRef(counter, "_interactionAnchor", interact.transform);
            SetRef(counter, "_customerQueueAnchor", queue.transform);
            SetRef(counter, "_statusLight", light.GetComponent<Renderer>());

            AddDeployHandle(root, LevelElementType.DeliveryCounter, new Vector3(0f, h * 0.5f, 0f),
                            new Vector3(w * 1.2f, h, w * 1.2f));

            var prefab = SavePrefab(root, "Machine_DeliveryCounter");
            return new GameCatalog.ElementEntry
            {
                type = LevelElementType.DeliveryCounter,
                prefab = prefab,
            };
        }

        // ---------------------------------------------------------------- 輸送帶

        private static GameCatalog.ElementEntry BuildConveyor()
        {
            var frameMat = Mat("M_ConveyorFrame", new Color(0.38f, 0.36f, 0.34f));
            var beltMat = Mat("M_ConveyorBelt", new Color(0.22f, 0.22f, 0.24f));
            var markerMat = Mat("M_ConveyorMarker", new Color(0.30f, 0.85f, 0.95f));

            var root = new GameObject("Machine_Conveyor");
            float len = GameTuning.ConveyorLength;
            float wid = GameTuning.ConveyorWidth;
            float hgt = GameTuning.ConveyorHeight;

            // 帶面（有碰撞體，東西才放得上去）
            var belt = Prim(PrimitiveType.Cube, "Belt", root.transform, new Vector3(0f, hgt - 0.05f, 0f),
                            new Vector3(wid, 0.1f, len), beltMat);

            // 兩側護欄，避免東西滑出去，也讓方向更明顯
            Prim(PrimitiveType.Cube, "RailL", root.transform, new Vector3(-wid * 0.5f, hgt + 0.06f, 0f),
                 new Vector3(0.07f, 0.16f, len), frameMat);
            Prim(PrimitiveType.Cube, "RailR", root.transform, new Vector3(wid * 0.5f, hgt + 0.06f, 0f),
                 new Vector3(0.07f, 0.16f, len), frameMat);

            // 支腳
            Prim(PrimitiveType.Cube, "LegBack", root.transform, new Vector3(0f, hgt * 0.5f, -len * 0.4f),
                 new Vector3(wid * 0.8f, hgt, 0.12f), frameMat, keepCollider: false);
            Prim(PrimitiveType.Cube, "LegFront", root.transform, new Vector3(0f, hgt * 0.5f, len * 0.4f),
                 new Vector3(wid * 0.8f, hgt, 0.12f), frameMat, keepCollider: false);

            // 方向標記：沿著帶面排一排，Render() 會輪流點亮
            var markers = new Renderer[4];
            for (int i = 0; i < markers.Length; i++)
            {
                float z = -len * 0.5f + len * (i + 0.5f) / markers.Length;
                var m = Prim(PrimitiveType.Cube, $"Marker{i}", root.transform,
                             new Vector3(0f, hgt + 0.02f, z),
                             new Vector3(wid * 0.5f, 0.03f, 0.16f), markerMat, keepCollider: false);
                markers[i] = m.GetComponent<Renderer>();
                markers[i].enabled = i == 0;
            }

            var output = Empty("OutputAnchor", root.transform, new Vector3(0f, hgt, len * 0.5f + 0.45f));

            root.AddComponent<NetworkObject>();
            var conveyor = root.AddComponent<Conveyor>();
            SetRef(conveyor, "_beltSurface", belt.transform);
            SetRef(conveyor, "_outputAnchor", output.transform);
            SetRefArray(conveyor, "_directionMarkers", markers);

            AddDeployHandle(root, LevelElementType.Conveyor, new Vector3(0f, hgt * 0.5f, 0f),
                            new Vector3(wid * 1.3f, hgt + 0.4f, len));

            var prefab = SavePrefab(root, "Machine_Conveyor");
            return new GameCatalog.ElementEntry
            {
                type = LevelElementType.Conveyor,
                prefab = prefab,
            };
        }

        // ---------------------------------------------------------------- 工具架

        /// <summary>
        /// 剃毛器架與噴槍架共用同一個 prefab，靠 DeployableDevice 的裝備類型分辨。
        /// 兩個 LevelElementType 指向同一份資產，省一個 prefab 也省一次維護。
        ///
        /// 注意 DeployHandle 上的 _deviceType 只是「沒有網路狀態時的退路」——
        /// 實際生成時 StallManager 會用 MarkDeployed() 把正確的類型寫進 [Networked] 欄位，
        /// 所以同一份 prefab 兩種用途不會混淆。
        /// </summary>
        private static void BuildToolRacks(List<GameCatalog.ElementEntry> outElements)
        {
            var frameMat = Mat("M_ToolRackFrame", new Color(0.45f, 0.38f, 0.28f));
            var iconMat = Mat("M_ToolRackIcon", PlaceholderPalette.ShearsBlade);

            var root = new GameObject("Machine_ToolRack");
            const float h = 1.0f;
            float w = GameTuning.MachineFootprint * 0.62f;

            Prim(PrimitiveType.Cube, "Post", root.transform, new Vector3(0f, h * 0.5f, 0f),
                 new Vector3(0.16f, h, 0.16f), frameMat);
            Prim(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.06f, 0f),
                 new Vector3(w, 0.12f, w), frameMat);
            Prim(PrimitiveType.Cube, "Arm", root.transform, new Vector3(0f, h, 0f),
                 new Vector3(w * 0.9f, 0.1f, 0.16f), frameMat, keepCollider: false);

            var icon = Prim(PrimitiveType.Sphere, "ToolIcon", root.transform,
                            new Vector3(0f, h + 0.16f, 0f), Vector3.one * 0.2f, iconMat,
                            keepCollider: false);

            // 工具生在架子前方一點點，玩家一眼看得到、也伸手就拿得到
            var toolAnchor = Empty("ToolAnchor", root.transform, new Vector3(0f, h * 0.75f, 0.25f));
            var interact = Empty("InteractionAnchor", root.transform, new Vector3(0f, h * 0.7f, -0.5f));

            root.AddComponent<NetworkObject>();
            var rack = root.AddComponent<ToolRack>();
            SetRef(rack, "_interactionAnchor", interact.transform);
            SetRef(rack, "_toolAnchor", toolAnchor.transform);
            SetRef(rack, "_toolIcon", icon.GetComponent<Renderer>());

            AddDeployHandle(root, LevelElementType.ToolRackShears,
                            new Vector3(0f, h * 0.5f, 0f), new Vector3(w * 1.3f, h, w * 1.3f));

            var prefab = SavePrefab(root, "Machine_ToolRack");

            outElements.Add(new GameCatalog.ElementEntry
                { type = LevelElementType.ToolRackShears, prefab = prefab });

            BuildReport.Line("  OK  Machine_ToolRack（剃毛器架）");
        }

        // ---------------------------------------------------------------- 開張鈴

        /// <summary>
        /// 開張鈴：襯布背緣的固定設施，**沒有 DeployHandle** ——
        /// 它是控制不是裝備，不進網格、不佔格子、不能被搬動。
        /// 由 StallManager 在進佈置模式時生成、敲掉或收攤時消失。
        /// </summary>
        private static GameCatalog.ElementEntry BuildServiceBell()
        {
            var baseMat = Mat("M_BellBase", new Color(0.35f, 0.26f, 0.18f));
            var domeMat = Mat("M_BellDome", new Color(0.98f, 0.82f, 0.30f));

            var root = new GameObject("Stall_ServiceBell");
            float h = GameTuning.StallBellHeight;

            // 小柱子 + 檯面，讓鈴鐺立在羊駝按得到的高度
            Prim(PrimitiveType.Cylinder, "Post", root.transform, new Vector3(0f, h * 0.5f, 0f),
                 new Vector3(0.18f, h * 0.5f, 0.18f), baseMat);
            Prim(PrimitiveType.Cylinder, "Plate", root.transform, new Vector3(0f, h, 0f),
                 new Vector3(0.5f, 0.03f, 0.5f), baseMat);

            // 鈴身：半球用壓扁的球代替，佔位階段夠用了
            var dome = Prim(PrimitiveType.Sphere, "Dome", root.transform,
                            new Vector3(0f, h + 0.12f, 0f),
                            new Vector3(0.36f, 0.26f, 0.36f), domeMat, keepCollider: false);
            Prim(PrimitiveType.Sphere, "Knob", root.transform, new Vector3(0f, h + 0.26f, 0f),
                 Vector3.one * 0.09f, domeMat, keepCollider: false);

            var interact = Empty("InteractionAnchor", root.transform, new Vector3(0f, h + 0.1f, 0f));

            root.AddComponent<NetworkObject>();
            var bell = root.AddComponent<ServiceBell>();
            SetRef(bell, "_interactionAnchor", interact.transform);
            SetRef(bell, "_dome", dome.transform);
            SetRef(bell, "_domeRenderer", dome.GetComponent<Renderer>());

            var prefab = SavePrefab(root, "Stall_ServiceBell");
            return new GameCatalog.ElementEntry
            {
                type = LevelElementType.ServiceBell,
                prefab = prefab,
            };
        }

        // ---------------------------------------------------------------- 手提箱生成點

        private static GameCatalog.ElementEntry BuildSuitcaseSpawnMarker()
        {
            var mat = Mat("M_SuitcaseSpawn", new Color(0.55f, 0.45f, 0.3f));
            var root = new GameObject("Level_SuitcaseSpawn");
            Prim(PrimitiveType.Cylinder, "Pad", root.transform, new Vector3(0f, 0.03f, 0f),
                 new Vector3(1.1f, 0.03f, 1.1f), mat, keepCollider: false);

            var prefab = SavePrefab(root, "Level_SuitcaseSpawn");
            return new GameCatalog.ElementEntry
            {
                type = LevelElementType.SuitcaseSpawn,
                prefab = prefab,
            };
        }

        // ---------------------------------------------------------------- DeployHandle

        /// <summary>
        /// 幫既有機台 prefab（縫紉機／果汁機／人偶）補上 DeployHandle。
        /// **機台自己的腳本一行都沒動** —— 只是多掛一個子物件。
        /// </summary>
        private static void AttachDeployHandles(List<GameCatalog.ElementEntry> elements)
        {
            var targets = new (LevelElementType type, string prefabName, float height)[]
            {
                (LevelElementType.SewingMachine, "Machine_SewingMachine", GameTuning.MachineHeight),
                (LevelElementType.Juicer,        "Machine_Juicer",        GameTuning.MachineHeight),
                (LevelElementType.Mannequin,     "Machine_Mannequin",     GameTuning.MachineHeight),
            };

            foreach (var (type, prefabName, height) in targets)
            {
                string path = $"{PrefabsDir}/{prefabName}.prefab";
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    BuildReport.Warn($"找不到 {path}，跳過 DeployHandle。");
                    continue;
                }

                // 用 prefab contents 編輯，改完再存回去（比直接改 asset 安全）
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    AddDeployHandle(contents, type, new Vector3(0f, height * 0.5f, 0f),
                                    new Vector3(GameTuning.MachineFootprint * 1.25f, height,
                                                GameTuning.MachineFootprint * 1.25f));
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    BuildReport.Line($"  OK  DeployHandle -> {prefabName}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
        }

        /// <summary>
        /// 加一個 DeployHandle 子物件：自己的 trigger 碰撞體 + DeployableDevice。
        ///
        /// 為什麼一定要放在子物件而不是根物件上：PlayerInteractor 是用
        /// GetComponentInParent&lt;IInteractable&gt;() 找候選人的，同一個物件上有兩個
        /// IInteractable 時只會拿到其中一個、而且順序不保證。放在子物件上，
        /// 機台本體與 DeployHandle 各自有碰撞體，兩個都會進候選清單，
        /// 最後由 InteractionPriority 決定誰贏 —— 這才是可預期的行為。
        /// </summary>
        public static void AddDeployHandle(GameObject root, LevelElementType type,
                                           Vector3 center, Vector3 size)
        {
            var existing = root.transform.Find("DeployHandle");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var handle = new GameObject("DeployHandle");
            handle.transform.SetParent(root.transform, false);
            handle.transform.localPosition = Vector3.zero;

            var col = handle.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = center;
            col.size = size;

            var anchor = Empty("HandleAnchor", handle.transform, center + Vector3.up * (size.y * 0.35f));

            var dev = handle.AddComponent<DeployableDevice>();
            SetEnum(dev, "_deviceType", EnumIndexOf(type));
            SetRef(dev, "_interactionAnchor", anchor.transform);
        }

        /// <summary>
        /// SerializedProperty.enumValueIndex 要的是「在列舉裡的第幾個」，不是列舉的數值。
        /// LevelElementType 目前剛好是連號的，但為了之後有人插值仍然正確，這裡照定義順序找。
        /// </summary>
        private static int EnumIndexOf(LevelElementType type)
        {
            var values = (LevelElementType[])System.Enum.GetValues(typeof(LevelElementType));
            for (int i = 0; i < values.Length; i++)
                if (values[i] == type) return i;
            return 0;
        }
    }
}
