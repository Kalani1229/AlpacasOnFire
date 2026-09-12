using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
using AlpacasOnFire.Machines;
using AlpacasOnFire.Npc;
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
            Add(failures, "Npc_WoolNpc", () => outElements.Add(BuildWoolNpc()));
            // 兩台織布機共用建置流程，只差版型與機體顏色（暖褐＝T恤、冷藍＝襯衫）
            Add(failures, "Machine_WeavingMachine", () => outElements.Add(
                BuildWeavingMachine(LevelElementType.WeavingMachine, PatternType.TShirt,
                                    "Machine_WeavingMachine", "M_WeavingMachine",
                                    new Color(0.55f, 0.40f, 0.22f))));

            Add(failures, "Machine_WeavingMachineShirt", () => outElements.Add(
                BuildWeavingMachine(LevelElementType.WeavingMachineShirt, PatternType.Shirt,
                                    "Machine_WeavingMachineShirt", "M_WeavingMachineShirt",
                                    new Color(0.32f, 0.45f, 0.62f))));
            Add(failures, "Machine_MaterialCrate", () => outElements.Add(BuildMaterialCrate()));
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

            // v6：箱體正面三個小色塊，顯示這一場帶了哪三種毛、目前要拿哪一格。
            // 玩家不用開面板就看得出材料狀況。
            var slotMat = Mat("M_SuitcaseSlot", PlaceholderPalette.Wool);
            var colorSlots = new Renderer[SuitcaseItem.ColorSlots];
            for (int i = 0; i < colorSlots.Length; i++)
            {
                var slot = Prim(PrimitiveType.Cube, $"ColorSlot{i}", root.transform,
                    new Vector3(-0.22f + i * 0.22f, 0.2f, 0.26f),
                    new Vector3(0.16f, 0.16f, 0.04f), slotMat, keepCollider: false);
                colorSlots[i] = slot.GetComponent<Renderer>();
                colorSlots[i].enabled = false;
            }

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
            SetRefArray(item, "_colorSlotVisuals", colorSlots);
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

        // ---------------------------------------------------------------- v6 織布機

        /// <summary>
        /// 織布機。機體上有**兩格色塊**：第一格主色、第二格點綴色 ——
        /// 玩家要能不看 UI 就確認「我放了什麼、順序對不對」，
        /// 因為放入順序決定成品是「紅底白紋」還是「白底紅紋」。
        /// </summary>
        /// <summary>
        /// 兩台織布機共用這一支，只差 prefab 名稱、機體顏色與產出版型。
        ///
        /// **一台機器只做一種版型**（跟縫紉機同一條規則），所以想同時接 T-shirt 與襯衫的單
        /// 就得擺兩台、吃掉兩格。機體顏色刻意差很多，佈置與營業時都要一眼分得出來 ——
        /// 兩台外觀一樣的話，玩家會把毛放錯機器，而那個錯誤要等 4 秒後才看得到。
        /// </summary>
        private static GameCatalog.ElementEntry BuildWeavingMachine(
            LevelElementType type, PatternType pattern, string prefabName,
            string bodyMatName, Color bodyColor)
        {
            var bodyMat = Mat(bodyMatName, bodyColor);
            var slotMat = Mat("M_WeaveSlot", PlaceholderPalette.Wool);
            var lightMat = Mat("M_StatusLight", new Color(0.3f, 0.9f, 0.4f));

            var fillMat   = Mat("M_WeaveFill",   new Color(0.95f, 0.75f, 0.15f));

            float h = GameTuning.MachineHeight;
            float w = GameTuning.MachineFootprint;

            var root = new GameObject(prefabName);

            var body = Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, w), bodyMat);

            // 兩格毛槽並排在機台頂面，左邊是主色、右邊是點綴色
            var slots = new Renderer[2];
            for (int i = 0; i < slots.Length; i++)
            {
                var sl = Prim(PrimitiveType.Sphere, $"WoolSlot{i}", root.transform,
                    new Vector3(-0.28f + i * 0.56f, h + 0.14f, 0.1f),
                    Vector3.one * 0.26f, slotMat, keepCollider: false);
                slots[i] = sl.GetComponent<Renderer>();
                slots[i].enabled = false;
            }

            var status = Prim(PrimitiveType.Sphere, "StatusLight", root.transform,
                new Vector3(0.45f, h + 0.12f, -0.45f), Vector3.one * 0.18f, lightMat, keepCollider: false);

            // ---- 進度條：跟縫紉機／果汁機同一套（MachineBase 的預設畫法）----
            //
            // 整條 = 目前這件衣服的完成點：單色時整條就是 4 秒，
            // 放了第二份毛之後整條變成 7 秒。所以不需要中間的刻度線，
            // 也不需要「目標終點」那一段 —— 終點永遠就是條子的右端。
            var bar = Prim(PrimitiveType.Cube, "ProgressBar", root.transform,
                new Vector3(0f, h + 0.30f, -0.6f), new Vector3(1f, 0.09f, 0.09f),
                fillMat, keepCollider: false);
            bar.SetActive(false);   // 只有織製中才顯示

            // 操作面在背面（-Z），跟其他機台一致
            var interact = Empty("InteractionAnchor", root.transform, new Vector3(0f, h * 0.6f, -0.75f));
            var output = Empty("OutputAnchor", root.transform, new Vector3(0f, h + 0.25f, -0.85f));

            root.AddComponent<NetworkObject>();
            var m = root.AddComponent<WeavingMachine>();
            SetRef(m, "_interactionAnchor", interact.transform);
            SetRef(m, "_outputAnchor", output.transform);
            SetRef(m, "_statusLight", status.GetComponent<Renderer>());
            SetRef(m, "_bodyRenderer", body.GetComponent<Renderer>());
            SetRefArray(m, "_woolSlotVisuals", slots);

            SetRef(m, "_progressBar", bar.transform);

            SetEnum(m, "_outputPattern", (int)pattern);

            AddDeployHandle(root, type,
                            new Vector3(0f, h * 0.5f, 0f), new Vector3(w * 1.25f, h, w * 1.25f));

            var prefab = SavePrefab(root, prefabName);
            return new GameCatalog.ElementEntry
            {
                type = type,
                prefab = prefab,
            };
        }

        // ---------------------------------------------------------------- v6 素材箱

        /// <summary>
        /// 素材箱。**不在 loadout 的裝備清單裡** —— 它是選色之後由
        /// StallManager.SyncCrates() 動態生出來的，所以這裡只負責做出 prefab。
        ///
        /// 箱體不設固定顏色：執行期由 MaterialCrate 依裝載的顏色染色。
        /// </summary>
        private static GameCatalog.ElementEntry BuildMaterialCrate()
        {
            var bodyMat = Mat("M_MaterialCrate", PlaceholderPalette.Wool);
            var frameMat = Mat("M_MaterialCrateFrame", new Color(0.38f, 0.29f, 0.20f));
            var fillMat = Mat("M_MaterialCrateFill", new Color(0.95f, 0.95f, 0.92f));

            var root = new GameObject("Machine_MaterialCrate");
            float w = GameTuning.MachineFootprint * 0.82f;
            const float h = 0.9f;

            // 箱體：執行期會被染成裝載的顏色，所以這裡用白色當底
            var body = Prim(PrimitiveType.Cube, "Body", root.transform,
                new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, w), bodyMat);

            // 木框讓它看起來像箱子而不是一坨色塊
            Prim(PrimitiveType.Cube, "RimTop", root.transform, new Vector3(0f, h + 0.03f, 0f),
                 new Vector3(w * 1.08f, 0.08f, w * 1.08f), frameMat, keepCollider: false);
            Prim(PrimitiveType.Cube, "RimBottom", root.transform, new Vector3(0f, 0.04f, 0f),
                 new Vector3(w * 1.08f, 0.08f, w * 1.08f), frameMat, keepCollider: false);

            // 剩餘量條：貼在箱子側面，沿 Y 縮放
            var fill = Prim(PrimitiveType.Cube, "FillBar", root.transform,
                new Vector3(0f, h * 0.5f, -w * 0.5f - 0.04f),
                new Vector3(w * 0.5f, h * 0.8f, 0.05f), fillMat, keepCollider: false);
            fill.SetActive(false);

            var woolAnchor = Empty("WoolAnchor", root.transform, new Vector3(0f, h + 0.2f, 0f));
            var interact = Empty("InteractionAnchor", root.transform, new Vector3(0f, h * 0.7f, 0f));

            root.AddComponent<NetworkObject>();
            var crate = root.AddComponent<MaterialCrate>();
            SetRef(crate, "_interactionAnchor", interact.transform);
            SetRef(crate, "_bodyRenderer", body.GetComponent<Renderer>());
            SetRef(crate, "_fillBar", fill.transform);
            SetRef(crate, "_woolAnchor", woolAnchor.transform);

            AddDeployHandle(root, LevelElementType.MaterialCrate,
                            new Vector3(0f, h * 0.5f, 0f), new Vector3(w * 1.25f, h, w * 1.25f));

            var prefab = SavePrefab(root, "Machine_MaterialCrate");
            return new GameCatalog.ElementEntry
            {
                type = LevelElementType.MaterialCrate,
                prefab = prefab,
            };
        }

        // ---------------------------------------------------------------- v6 羊毛 NPC

        /// <summary>
        /// 會走動、身上長毛的 NPC。
        ///
        /// **移動用 NetworkCharacterController**，跟玩家 prefab 一樣 ——
        /// 不可以用裸 CharacterController + NetworkTransform，那在 Fusion 重模擬時
        /// 會抖動並瞬間大步移動（玩家身上已經踩過一次）。
        /// 注意 NCC 本身就是 NetworkTRSP，**不要再掛 NetworkTransform**，兩者會打架。
        ///
        /// 它不是「裝備」，所以**沒有 DeployHandle** —— 不進網格、不能被搬動、
        /// 也不會被收攤收走。
        /// </summary>
        private static GameCatalog.ElementEntry BuildWoolNpc()
        {
            var bodyMat = Mat("M_NpcBody", PlaceholderPalette.Wool);
            var faceMat = Mat("M_NpcFace", new Color(0.3f, 0.3f, 0.32f));
            var tuftMat = Mat("M_NpcTuft", PlaceholderPalette.Wool);

            var root = new GameObject("Npc_WoolNpc");

            // 身體比玩家矮胖一點，一眼分得出誰是玩家誰是 NPC
            const float height = 1.35f;
            const float radius = 0.34f;

            var body = Prim(PrimitiveType.Capsule, "Body", root.transform,
                new Vector3(0f, height * 0.5f, 0f),
                new Vector3(radius * 2f, height * 0.5f, radius * 2f), bodyMat, keepCollider: false);

            Prim(PrimitiveType.Cube, "Snout", body.transform, new Vector3(0f, 0.5f, 0.6f),
                 new Vector3(0.4f, 0.3f, 0.5f), faceMat, keepCollider: false);

            // 三撮毛，剩幾份就顯示幾撮
            var tufts = new Renderer[GameTuning.NpcFleeceMax];
            for (int i = 0; i < tufts.Length; i++)
            {
                float angle = -35f + i * 35f;
                var offset = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0f, 0.28f);
                var tuft = Prim(PrimitiveType.Sphere, $"Tuft{i}", root.transform,
                    new Vector3(offset.x, height + 0.1f, offset.z),
                    Vector3.one * 0.34f, tuftMat, keepCollider: false);
                tufts[i] = tuft.GetComponent<Renderer>();
            }

            var interact = Empty("InteractionAnchor", root.transform, new Vector3(0f, height * 0.75f, 0f));

            var cc = root.AddComponent<CharacterController>();
            cc.height = height;
            cc.radius = radius;
            cc.center = new Vector3(0f, height * 0.5f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.3f;
            cc.skinWidth = 0.03f;

            root.AddComponent<NetworkObject>();

            var ncc = root.AddComponent<NetworkCharacterController>();
            ncc.gravity      = -GameTuning.NpcGravity;
            ncc.acceleration = 20f;
            ncc.braking      = 20f;
            ncc.maxSpeed     = GameTuning.NpcWanderSpeed;
            ncc.rotationSpeed = 8f;

            var npc = root.AddComponent<WoolNpc>();
            // 惡搞系統：跟玩家掛的是同一支元件，所以「打誰都一樣」
            root.AddComponent<Prank.StaggerStatus>();
            SetRef(npc, "_interactionAnchor", interact.transform);
            SetRef(npc, "_bodyRenderer", body.GetComponent<Renderer>());
            SetRefArray(npc, "_fleeceTufts", tufts);

            // 明確寫死成非白色。場景建置器會逐隻覆蓋掉，但手動拖一隻進場景的人
            // 不該拿到一隻白羊 —— 白毛是玩家互剃專屬的產出。
            SetEnum(npc, "_startColor", (int)DyeColorType.Yellow);

            // ---- 顧客模式：同一個 prefab，被徵召時才啟用 ----
            // 設計文件明講顧客「就是場上那群羊」，所以不做第二種 prefab。
            var signMat = Mat("M_CustomerSign", new Color(0.96f, 0.94f, 0.88f));
            var barMat = Mat("M_CustomerPatience", new Color(0.4f, 0.9f, 0.5f));

            var signRoot = Empty("CustomerSign", root.transform, new Vector3(0f, height + 0.75f, 0f));

            Prim(PrimitiveType.Cube, "Board", signRoot.transform, Vector3.zero,
                 new Vector3(0.86f, 0.5f, 0.06f), signMat, keepCollider: false);

            var swatches = new Renderer[2];
            for (int i = 0; i < swatches.Length; i++)
            {
                var sw = Prim(PrimitiveType.Cube, $"Want{i}", signRoot.transform,
                    new Vector3(-0.2f + i * 0.4f, 0.05f, -0.05f),
                    new Vector3(0.3f, 0.3f, 0.04f), signMat, keepCollider: false);
                swatches[i] = sw.GetComponent<Renderer>();
            }

            var patience = Prim(PrimitiveType.Cube, "PatienceBar", signRoot.transform,
                new Vector3(0f, -0.2f, -0.05f), new Vector3(0.78f, 0.07f, 0.04f), barMat,
                keepCollider: false);

            signRoot.SetActive(false);

            var customer = root.AddComponent<Customer>();
            SetRef(customer, "_signRoot", signRoot.transform);
            SetRefArray(customer, "_signSwatches", swatches);
            SetRef(customer, "_patienceBar", patience.transform);

            var prefab = SavePrefab(root, "Npc_WoolNpc");
            return new GameCatalog.ElementEntry
            {
                type = LevelElementType.WoolNpc,
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
            SetEnum(dev, "_deviceType", (int)type);
            SetRef(dev, "_interactionAnchor", anchor.transform);
        }

    }
}
