using System.Collections.Generic;
using AlpacasOnFire.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static AlpacasOnFire.EditorTools.EditorBuildUtils;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 七片佔位路面，讓美術交件之前就看得到路、看得出選片邏輯有沒有出錯。
    ///
    /// **拼接是唯一重要的約束。** 七片共用同一組尺寸常數：
    /// 路面蓋滿整格、pivot 在正中央、厚度與高度一致、路緣寬度一致 ——
    /// 轉角接直線、T 字接十字才不會錯位。
    ///
    /// 每一片的標線不同，一眼就看得出生成器挑的是哪一片、轉的方向對不對：
    /// <code>
    ///   直線   一條貫穿的中線、兩側路緣
    ///   轉角   L 形中線、兩側路緣（外角那兩邊）
    ///   T 字   T 形中線、封住的那一側有路緣
    ///   十字   四向中線、沒有路緣
    ///   死路   一條中線 + 中央一條封口橫槓、三側路緣
    ///   孤立   中央一個方塊、四周路緣
    /// </code>
    ///
    /// **朝向必須對齊 RandomMapBuilder.GetRoadPrefab 的基準遮罩**（旋轉 0 時的連接方向）：
    ///   vertical = 北+南、horizontal = 東+西、corner = 北+西、tJunction = 北+東+南、deadEnd = 北。
    /// 北 = +Z、東 = +X。GetRoadPrefab 再用 RotationForMask 轉到正確方向。
    /// </summary>
    public static class RoadPlaceholderBuilder
    {
        /// <summary>
        /// 佔位路面的設計邊長（公尺）。
        ///
        /// 規格原本要求「改 tileSize 要同步改這裡」。改成不需要同步：每片都掛
        /// RoadTileInfo 記住這個設計邊長，RandomMapBuilder 生成時自動縮放成場景的 tileSize。
        /// 場景目前是 tileSize 10，照規格寫死 4 公尺的話路面之間會有 6 公尺的縫。
        /// </summary>
        public const float DesignTileSize = 4f;

        private const float SlabY         = 0.03f;   // 比一般地面略高一點點，避免 Z-fighting
        private const float SlabThickness = 0.05f;
        private const float CurbWidth     = 0.4f;
        private const float LineWidth     = 0.18f;

        private static readonly Color AsphaltColor = new Color(0.20f, 0.20f, 0.22f);
        private static readonly Color CurbColor    = new Color(0.72f, 0.72f, 0.70f);
        private static readonly Color LineColor    = new Color(0.95f, 0.86f, 0.35f);

        [System.Flags]
        private enum Side { None = 0, N = 1, E = 2, S = 4, W = 8, All = 15 }

        private static readonly (string name, Side open)[] Pieces =
        {
            ("Road_Horizontal", Side.E | Side.W),
            ("Road_Vertical",   Side.N | Side.S),
            ("Road_Corner",     Side.N | Side.W),
            ("Road_TJunction",  Side.N | Side.E | Side.S),
            ("Road_Crossroad",  Side.All),
            ("Road_DeadEnd",    Side.N),
            ("Road_Isolated",   Side.None),
        };

        /// <summary>產出七片。由「1. 建置佔位資產」在最後呼叫，也可以單獨呼叫。</summary>
        public static void BuildAll()
        {
            var asphalt = Mat("M_RoadAsphalt", AsphaltColor);
            var curb = Mat("M_RoadCurb", CurbColor);
            var line = Mat("M_RoadLine", LineColor, 0.4f);

            foreach (var (name, open) in Pieces)
            {
                BuildPiece(name, open, asphalt, curb, line);
                BuildReport.Line($"  OK  {name}");
            }
        }

        private static void BuildPiece(string name, Side open, Material asphalt, Material curb, Material line)
        {
            const float s = DesignTileSize;
            float half = s * 0.5f;
            float top = SlabY + SlabThickness * 0.5f;

            var root = new GameObject(name);

            // 路面：蓋滿整格。**沒有碰撞體** —— 地面已經有了，路面再疊一層碰撞
            // 會在路跟人行道交界處變成一個 5 公分的小台階，而且擺攤的平坦度檢查會吃到它。
            Prim(PrimitiveType.Cube, "Asphalt", root.transform, new Vector3(0f, SlabY, 0f),
                 new Vector3(s, SlabThickness, s), asphalt, keepCollider: false);

            // 路緣：每一個「沒有接路」的邊一條。比路面高 2 毫米，蓋在路面上方不會閃爍。
            float curbY = SlabY + 0.001f;
            float curbH = SlabThickness + 0.002f;
            foreach (var side in new[] { Side.N, Side.E, Side.S, Side.W })
            {
                if ((open & side) != 0) continue;
                var dir = Dir(side);
                bool alongX = side == Side.N || side == Side.S;
                var pos = new Vector3(dir.x * (half - CurbWidth * 0.5f), curbY, dir.z * (half - CurbWidth * 0.5f));
                var scale = alongX ? new Vector3(s, curbH, CurbWidth) : new Vector3(CurbWidth, curbH, s);
                Prim(PrimitiveType.Cube, $"Curb_{side}", root.transform, pos, scale, curb, keepCollider: false);
            }

            // 標線：從中心往每一個接路的方向畫一條到邊緣。多畫半個線寬，讓幾條在中心接得起來。
            float lineY = top + 0.0015f;
            const float lineH = 0.003f;
            float len = half + LineWidth * 0.5f;
            foreach (var side in new[] { Side.N, Side.E, Side.S, Side.W })
            {
                if ((open & side) == 0) continue;
                var dir = Dir(side);
                bool alongZ = side == Side.N || side == Side.S;
                var pos = new Vector3(dir.x * (len * 0.5f - LineWidth * 0.5f), lineY,
                                      dir.z * (len * 0.5f - LineWidth * 0.5f));
                var scale = alongZ ? new Vector3(LineWidth, lineH, len) : new Vector3(len, lineH, LineWidth);
                Prim(PrimitiveType.Cube, $"Line_{side}", root.transform, pos, scale, line, keepCollider: false);
            }

            // 死路：中央一條橫的封口槓，垂直於唯一的出口（北）
            if (open == Side.N)
                Prim(PrimitiveType.Cube, "Line_Cap", root.transform, new Vector3(0f, lineY, 0f),
                     new Vector3(s * 0.5f, lineH, LineWidth * 1.6f), line, keepCollider: false);

            // 孤立：中央一個方塊
            if (open == Side.None)
                Prim(PrimitiveType.Cube, "Line_Dot", root.transform, new Vector3(0f, lineY, 0f),
                     new Vector3(0.6f, lineH, 0.6f), line, keepCollider: false);

            var info = root.AddComponent<RoadTileInfo>();
            info.designTileSize = s;

            SavePrefab(root, name);
        }

        // ================================================================ 支道（窄版）

        /// <summary>
        /// 支道七片：跟幹道同一套樣式（深灰柏油、黃色中線、淺色路緣），只是車道窄。
        ///
        /// **為什麼不是直接把幹道那七片縮窄：** 直路縮寬度沒問題，但轉角、T 字、十字的
        /// 每一條臂都要伸到格子邊緣才接得上隔壁那一格 —— 整片縮窄會把臂也縮短，
        /// 路口跟旁邊的直路之間就斷一截。所以窄版是重新組的：
        /// 中央一塊方形 + 每個接口一條伸到邊緣的臂，路緣貼在車道外側。
        ///
        /// 車道以外**不鋪任何東西**，露出底下的地板，留給兩旁的建築伸過來佔
        /// （RandomMapBuilder.IsInsideBlock）。路緣寬度用 RandomMapBuilder.CurbFraction，
        /// 建築伸到路緣外側為止，兩邊剛好貼齊。
        /// </summary>
        private static readonly (string name, Side open)[] AlleyPieces =
        {
            ("Road_Alley_Horizontal", Side.E | Side.W),
            ("Road_Alley_Vertical",   Side.N | Side.S),
            ("Road_Alley_Corner",     Side.N | Side.W),
            ("Road_Alley_TJunction",  Side.N | Side.E | Side.S),
            ("Road_Alley_Crossroad",  Side.All),
            ("Road_Alley_DeadEnd",    Side.N),
            ("Road_Alley_Isolated",   Side.None),
        };

        /// <summary>產出窄版支道七片。laneRatio = 車道寬佔一格的比例（RandomMapBuilder.alleyLaneRatio）。</summary>
        public static void BuildAlleys(float laneRatio)
        {
            var asphalt = Mat("M_RoadAsphalt", AsphaltColor);
            var curb = Mat("M_RoadCurb", CurbColor);
            var line = Mat("M_RoadLine", LineColor, 0.4f);

            float hw = Mathf.Clamp(laneRatio, 0.1f, 1f) * DesignTileSize * 0.5f;
            foreach (var (name, open) in AlleyPieces)
                BuildAlleyPiece(name, open, hw, asphalt, curb, line);
        }

        private static void BuildAlleyPiece(string name, Side open, float hw,
                                            Material asphalt, Material curb, Material line)
        {
            const float s = DesignTileSize;
            float half = s * 0.5f;
            float c = RandomMapBuilder.CurbFraction * s;
            float top = SlabY + SlabThickness * 0.5f;
            var sides = new[] { Side.N, Side.E, Side.S, Side.W };

            var root = new GameObject(name);

            // 車道：中央方塊 + 每個接口一條伸到格子邊緣的臂
            Prim(PrimitiveType.Cube, "Asphalt_Center", root.transform, new Vector3(0f, SlabY, 0f),
                 new Vector3(hw * 2f, SlabThickness, hw * 2f), asphalt, keepCollider: false);

            float armLen = half - hw;
            foreach (var side in sides)
            {
                if ((open & side) == 0 || armLen <= 0f) continue;
                var d = Dir(side);
                bool alongZ = side == Side.N || side == Side.S;
                var pos = new Vector3(d.x * (hw + armLen * 0.5f), SlabY, d.z * (hw + armLen * 0.5f));
                var scale = alongZ ? new Vector3(hw * 2f, SlabThickness, armLen)
                                   : new Vector3(armLen, SlabThickness, hw * 2f);
                Prim(PrimitiveType.Cube, $"Asphalt_{side}", root.transform, pos, scale, asphalt, keepCollider: false);
            }

            // 路緣：貼在車道外側。三層（臂的兩側、中央方塊封住的邊、四個角）彼此有一點重疊，
            // 各差 1 毫米高，重疊處不會閃爍。
            float curbH = SlabThickness + 0.002f;
            foreach (var side in sides)
            {
                var d = Dir(side);
                bool alongZ = side == Side.N || side == Side.S;

                if ((open & side) != 0 && armLen > 0f)
                {
                    // 臂的兩側
                    foreach (float sgn in new[] { -1f, 1f })
                    {
                        float off = sgn * (hw + c * 0.5f);
                        float mid = hw + armLen * 0.5f;
                        var pos = alongZ ? new Vector3(off, SlabY + 0.001f, d.z * mid)
                                         : new Vector3(d.x * mid, SlabY + 0.001f, off);
                        var scale = alongZ ? new Vector3(c, curbH, armLen) : new Vector3(armLen, curbH, c);
                        Prim(PrimitiveType.Cube, $"Curb_{side}_{(sgn < 0 ? "a" : "b")}", root.transform,
                             pos, scale, curb, keepCollider: false);
                    }
                }
                else
                {
                    // 中央方塊封住的那一邊
                    var pos = new Vector3(d.x * (hw + c * 0.5f), SlabY + 0.002f, d.z * (hw + c * 0.5f));
                    var scale = alongZ ? new Vector3(hw * 2f, curbH + 0.001f, c) : new Vector3(c, curbH + 0.001f, hw * 2f);
                    Prim(PrimitiveType.Cube, $"Curb_{side}", root.transform, pos, scale, curb, keepCollider: false);
                }
            }

            // 四個角各補一小塊，讓路緣轉角接起來
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    Prim(PrimitiveType.Cube, "Curb_Corner", root.transform,
                         new Vector3(sx * (hw + c * 0.5f), SlabY + 0.003f, sz * (hw + c * 0.5f)),
                         new Vector3(c, curbH + 0.002f, c), curb, keepCollider: false);

            // 標線：跟幹道一樣從中心往每個接口畫到邊緣
            float lineY = top + 0.0015f;
            const float lineH = 0.003f;
            float len = half + LineWidth * 0.5f;
            foreach (var side in sides)
            {
                if ((open & side) == 0) continue;
                var d = Dir(side);
                bool alongZ = side == Side.N || side == Side.S;
                var pos = new Vector3(d.x * (len * 0.5f - LineWidth * 0.5f), lineY, d.z * (len * 0.5f - LineWidth * 0.5f));
                var scale = alongZ ? new Vector3(LineWidth, lineH, len) : new Vector3(len, lineH, LineWidth);
                Prim(PrimitiveType.Cube, $"Line_{side}", root.transform, pos, scale, line, keepCollider: false);
            }

            if (open == Side.N)
                Prim(PrimitiveType.Cube, "Line_Cap", root.transform, new Vector3(0f, lineY, 0f),
                     new Vector3(hw * 1.2f, lineH, LineWidth * 1.6f), line, keepCollider: false);
            if (open == Side.None)
                Prim(PrimitiveType.Cube, "Line_Dot", root.transform, new Vector3(0f, lineY, 0f),
                     new Vector3(0.4f, lineH, 0.4f), line, keepCollider: false);

            root.AddComponent<RoadTileInfo>().designTileSize = s;
            SavePrefab(root, name);
        }

        private static Vector3 Dir(Side side) => side switch
        {
            Side.N => Vector3.forward,
            Side.E => Vector3.right,
            Side.S => Vector3.back,
            _      => Vector3.left,
        };

        // ================================================================ 指派到場景

        /// <summary>
        /// 把七片佔位路面塞進目前開啟場景裡所有 RandomMapBuilder 的 roadPrefabs。
        ///
        /// **會覆蓋原本的指派。** 場景上原本七格都指向同一片美術地面，覆蓋前會把
        /// 原本是什麼印出來，要還原的話照著拉回去就好。
        /// </summary>
        [MenuItem("羊駝很忙/v6 羊駝村/4. 指派佔位路面到場景的 RandomMapBuilder", priority = 63)]
        public static void AssignToScene()
        {
            var builders = Object.FindObjectsByType<RandomMapBuilder>(FindObjectsInactive.Include,
                                                                      FindObjectsSortMode.None);
            if (builders.Length == 0)
            {
                Debug.LogError("[地圖] 目前開啟的場景裡找不到 RandomMapBuilder。" +
                               "請先打開 Village_Test（或掛著 RandomMapBuilder 的場景）再執行這個選單。");
                return;
            }

            // 佔位路面還沒產過就先產
            var prefabs = LoadPieces();
            if (prefabs.Count < Pieces.Length)
            {
                BuildAll();
                AssetDatabase.SaveAssets();
                prefabs = LoadPieces();
            }

            if (prefabs.Count < Pieces.Length)
            {
                Debug.LogError("[地圖] 佔位路面產生失敗，請先跑「1. 建置佔位資產」看建置報告。");
                return;
            }

            string[] fields = { "horizontal", "vertical", "corner", "tJunction", "crossroad", "deadEnd", "isolated" };

            foreach (var builder in builders)
            {
                // 支道每次都照場景目前的 alleyLaneRatio 重做 —— 改了寬度重跑這個選單就會跟著變
                BuildAlleys(builder.alleyLaneRatio);
                AssetDatabase.SaveAssets();

                var so = new SerializedObject(builder);
                var before = new List<string>();

                for (int i = 0; i < fields.Length; i++)
                {
                    // 幹道：原本那七片（整格柏油）
                    AssignOne(so, $"arterialPrefabs.{fields[i]}", LoadPrefab(Pieces[i].name), before, builder);
                    // 支道：同樣式的窄版
                    AssignOne(so, $"alleyPrefabs.{fields[i]}", LoadPrefab(AlleyPieces[i].name), before, builder);
                }

                // 幹道是整格柏油，支道從任何一側接進來都接得上，不需要「幹道穿過支道」那片特例。
                // 清掉它，不然會沿用之前美術路面那一片。
                AssignOne(so, "arterialCrossAlleyPrefab", null, before, builder);

                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);

                Debug.Log($"[地圖] 已指派佔位路面給 {builder.name}：幹道 7 片（整格）、" +
                          $"支道 7 片（車道 {builder.alleyLaneRatio * builder.tileSize:0.#} 公尺，沒有人行道）。\n" +
                          "覆蓋前的指派：\n  " + string.Join("\n  ", before) +
                          "\n**記得存檔場景（Cmd+S）**，再 Clear Generated Map → Generate Map。", builder);
            }
        }

        private static GameObject LoadPrefab(string name)
            => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/{name}.prefab");

        private static void AssignOne(SerializedObject so, string path, GameObject value, List<string> log, Object ctx)
        {
            var p = so.FindProperty(path);
            if (p == null) { Debug.LogError($"[地圖] RandomMapBuilder 找不到欄位 {path}", ctx); return; }
            log.Add($"{path} = {(p.objectReferenceValue != null ? p.objectReferenceValue.name : "空")}");
            p.objectReferenceValue = value;
        }

        private static Dictionary<string, GameObject> LoadPieces()
        {
            var map = new Dictionary<string, GameObject>();
            foreach (var (name, _) in Pieces)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/{name}.prefab");
                if (go != null) map[name] = go;
            }
            return map;
        }
    }
}
