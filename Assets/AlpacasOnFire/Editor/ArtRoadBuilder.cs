using System.Collections.Generic;
using AlpacasOnFire.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static AlpacasOnFire.EditorTools.EditorBuildUtils;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 用 RPG_FPS 素材包的道路貼圖，組出十五片可以拼接的路面。
    ///
    /// 素材包只有兩張道路貼圖，沒有路口、轉角這些片型：
    ///   Road_with_pavements_v1  一條直路（兩側人行道 + 路緣 + 中間柏油）
    ///   Seamless_asphalt_v1     無縫柏油
    /// 所以這裡用程式把它們拼成路口：
    /// <code>
    ///   1. 整格鋪人行道磚（從 Road_with_pavements 裁出來的那一排磚，T_RoadSidewalk.png）
    ///   2. 上面疊柏油車道：中央一塊方形 + 往每個接口伸一條臂
    ///   3. 車道邊緣沿著沒接路的地方畫路緣（同一張圖裁出來的路緣條，T_RoadCurb.png）
    /// </code>
    ///
    /// **幹道車道寬、有人行道；支道車道窄、沒有人行道** —— 支道車道以外露出地板，
    /// 兩旁街區的建築可以伸過來佔（RandomMapBuilder.IsInsideBlock），路是被建築夾窄的。
    /// 幹道比例照原貼圖：車道佔 60%、兩側人行道各 20%。
    ///
    /// **接縫規則**：幹道格子上「不在直線方向」的接口一定是支道匯入，那一條臂畫成支道寬度
    /// （T 字的側枝、幹道穿過支道的十字）。所以支道接幹道的地方車道寬度一定對得上。
    ///
    /// UV 以公尺計算、而且每格剛好整數次重複，所以相鄰兩格的貼圖是連續的，看不出格線。
    /// 全部是平面（沒有厚度、沒有碰撞體），走路的地面仍然是底下的地板。
    /// </summary>
    public static class ArtRoadBuilder
    {
        private const string PackRoot  = "Assets/Plugins/RPG_FPS_game_assets_industrial";
        private const string AsphaltMatPath = PackRoot + "/Textures/Asphalt/Seamless_asphalt_v1/Seamless_asphalt_v1.mat";
        private const string SidewalkTexPath = "Assets/AlpacasOnFire/Textures/Roads/T_RoadSidewalk.png";
        private const string CurbTexPath     = "Assets/AlpacasOnFire/Textures/Roads/T_RoadCurb.png";
        private const string MeshDir   = "Assets/AlpacasOnFire/Meshes/Roads";
        private const string PrefabDir = "Assets/AlpacasOnFire/Prefabs/Roads";
        private const string MatDir    = "Assets/AlpacasOnFire/Materials";

        /// <summary>幹道車道半寬，佔一格的比例。0.3 = 車道佔 60%，兩側人行道各 20%（跟原貼圖一樣）。</summary>
        public const float ArterialHalf = 0.30f;

        /// <summary>
        /// 路緣寬度，佔一格的比例（原貼圖約 5%）。
        /// 跟 RandomMapBuilder 共用 —— 支道兩旁的建築就是伸到路緣外側為止，兩邊要一致。
        /// </summary>
        private const float CurbFrac = RandomMapBuilder.CurbFraction;

        // 支道車道寬度**不寫死在這裡**，讀場景上 RandomMapBuilder.alleyLaneRatio。
        // 建築能伸進支道多遠也是讀同一個值，路面與建築才會剛好貼齊。

        private const float SidewalkY = 0.030f;   // 比地板略高，避免 Z-fighting
        private const float AsphaltY  = 0.034f;
        // 路緣分三層（臂的兩側、中央方塊的邊、四個角），彼此會有一點重疊。
        // 重疊的地方如果高度一樣會閃爍（Z-fighting），所以各差 1 毫米，上面那層永遠贏。
        private const float CurbY       = 0.038f;
        private const float CurbEdgeY   = 0.039f;
        private const float CurbCornerY = 0.040f;

        [System.Flags]
        private enum Side { None = 0, N = 1, E = 2, S = 4, W = 8 }
        private static readonly Side[] Sides = { Side.N, Side.E, Side.S, Side.W };

        /// <summary>
        /// 一片路面：名稱、每個接口的車道半寬（0 = 不接路）、中央方塊的半寬。
        /// 接口順序 N、E、S、W，旋轉 0 時的方向必須對齊 GetRoadPrefab 的基準遮罩：
        /// vertical=北南、horizontal=東西、corner=北西、tJunction=北東南、deadEnd=北。
        /// </summary>
        private readonly struct Piece
        {
            public readonly string Name;
            public readonly float N, E, S, W, Center;
            /// <summary>
            /// 要不要整格鋪人行道。**幹道鋪、支道不鋪** ——
            /// 支道車道以外的地方露出底下的地板，留給兩旁的建築伸過來佔。
            /// </summary>
            public readonly bool Sidewalk;

            public Piece(string name, float n, float e, float s, float w, float center, bool sidewalk)
            { Name = name; N = n; E = e; S = s; W = w; Center = center; Sidewalk = sidewalk; }

            public float Arm(Side side) => side switch
            {
                Side.N => N, Side.E => E, Side.S => S, _ => W,
            };
        }

        private static readonly string[] SetFields =
            { "horizontal", "vertical", "corner", "tJunction", "crossroad", "deadEnd", "isolated" };

        // 下面三組依支道車道半寬（L）產生。順序對應 SetFields。

        private static Piece[] ArterialPieces(float L)
        {
            const float A = ArterialHalf;
            return new Piece[]
            {
                new("ArtRoad_Arterial_Horizontal", 0, A, 0, A, A, true),
                new("ArtRoad_Arterial_Vertical",   A, 0, A, 0, A, true),
                new("ArtRoad_Arterial_Corner",     A, 0, 0, A, A, true),
                new("ArtRoad_Arterial_TJunction",  A, L, A, 0, A, true),   // 東側是支道匯入：畫成支道寬度
                new("ArtRoad_Arterial_Crossroad",  A, A, A, A, A, true),   // 幹道碰幹道：四向全寬
                new("ArtRoad_Arterial_DeadEnd",    A, 0, 0, 0, A, true),
                new("ArtRoad_Arterial_Isolated",   0, 0, 0, 0, A, true),
            };
        }

        // 幹道南北穿過、支道從東西進來
        private static Piece CrossAlley(float L) =>
            new("ArtRoad_Arterial_CrossAlley", ArterialHalf, L, ArterialHalf, L, ArterialHalf, true);

        private static Piece[] AlleyPieces(float L) => new Piece[]
        {
            new("ArtRoad_Alley_Horizontal", 0, L, 0, L, L, false),
            new("ArtRoad_Alley_Vertical",   L, 0, L, 0, L, false),
            new("ArtRoad_Alley_Corner",     L, 0, 0, L, L, false),
            new("ArtRoad_Alley_TJunction",  L, L, L, 0, L, false),
            new("ArtRoad_Alley_Crossroad",  L, L, L, L, L, false),
            new("ArtRoad_Alley_DeadEnd",    L, 0, 0, 0, L, false),
            new("ArtRoad_Alley_Isolated",   0, 0, 0, 0, L, false),
        };

        // ================================================================ 選單

        [MenuItem("羊駝很忙/v6 羊駝村/6. 用美術貼圖產生路面並指派", priority = 65)]
        public static void BuildAndAssign()
        {
            var builders = Object.FindObjectsByType<RandomMapBuilder>(FindObjectsInactive.Include,
                                                                      FindObjectsSortMode.None);
            if (builders.Length == 0)
            {
                Debug.LogError("[地圖] 目前開啟的場景裡找不到 RandomMapBuilder。請先打開 Village_Test。");
                return;
            }

            // 以場景的 tileSize 來做，貼圖的比例才會剛好（掛了 RoadTileInfo，改 tileSize 也會自動縮放）。
            // 支道車道寬度讀場景上的 alleyLaneRatio —— 建築能伸進支道多遠也是讀它。
            float tile = builders[0].tileSize;
            float alleyHalf = Mathf.Clamp(builders[0].alleyLaneRatio, 0.2f, 1f) * 0.5f;
            var prefabs = BuildAll(tile, alleyHalf);
            if (prefabs == null) return;

            var arterial = ArterialPieces(alleyHalf);
            var alley = AlleyPieces(alleyHalf);

            foreach (var builder in builders)
            {
                var so = new SerializedObject(builder);
                for (int i = 0; i < SetFields.Length; i++)
                {
                    Set(so, $"arterialPrefabs.{SetFields[i]}", prefabs[arterial[i].Name], builder);
                    Set(so, $"alleyPrefabs.{SetFields[i]}", prefabs[alley[i].Name], builder);
                }
                Set(so, "arterialCrossAlleyPrefab", prefabs[CrossAlley(alleyHalf).Name], builder);
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(builder.gameObject.scene);
            }

            float lane = alleyHalf * 2f * tile;
            float reach = Mathf.Max(0f, tile * (0.5f - alleyHalf - CurbFrac));
            Debug.Log($"[地圖] 已用美術貼圖產生十五片路面（{tile:0.#} 公尺一格）並指派給 {builders.Length} 個 RandomMapBuilder。\n" +
                      $"幹道：車道 {ArterialHalf * 2 * tile:0.#} 公尺 + 兩側人行道。" +
                      $"支道：車道 {lane:0.#} 公尺、沒有人行道，兩旁建築可以伸進來 {reach:0.#} 公尺。\n" +
                      "**記得存檔場景（Cmd+S）**，再 Clear Generated Map → Generate Map。");
        }

        private static void Set(SerializedObject so, string path, GameObject value, Object ctx)
        {
            var p = so.FindProperty(path);
            if (p == null) { Debug.LogError($"[地圖] RandomMapBuilder 找不到欄位 {path}", ctx); return; }
            p.objectReferenceValue = value;
        }

        // ================================================================ 產生

        public static Dictionary<string, GameObject> BuildAll(float tile, float alleyHalf)
        {
            var asphalt = AssetDatabase.LoadAssetAtPath<Material>(AsphaltMatPath);
            if (asphalt == null)
            {
                Debug.LogError($"[地圖] 找不到柏油材質 {AsphaltMatPath}（素材包被移動或刪除了？）");
                return null;
            }

            // 原貼圖（2048×1024）由上往下：人行道磚 0~14%、路緣 14~19%、柏油 20~80%、
            // 路緣 81~86%、人行道磚 87~100%。從下半部裁一排磚、一條路緣。
            var sidewalkTex = EnsureCrop(SidewalkTexPath, 896f / 1024f, 1000f / 1024f);
            var curbTex = EnsureCrop(CurbTexPath, 834f / 1024f, 872f / 1024f);
            if (sidewalkTex == null || curbTex == null)
            {
                Debug.LogError($"[地圖] 找不到人行道／路緣貼圖（{SidewalkTexPath}、{CurbTexPath}）。");
                return null;
            }

            EnsureDir(MeshDir);
            EnsureDir(PrefabDir);

            // 人行道與路緣沿用柏油材質的 shader，確保跟專案的 render pipeline 一致
            var sidewalk = TexturedMat("M_ArtRoadSidewalk", asphalt, sidewalkTex);
            var curb = TexturedMat("M_ArtRoadCurb", asphalt, curbTex);
            var mats = new[] { sidewalk, asphalt, curb };

            var result = new Dictionary<string, GameObject>();
            foreach (var p in ArterialPieces(alleyHalf)) result[p.Name] = BuildPiece(p, tile, mats);
            var cross = CrossAlley(alleyHalf);
            result[cross.Name] = BuildPiece(cross, tile, mats);
            foreach (var p in AlleyPieces(alleyHalf)) result[p.Name] = BuildPiece(p, tile, mats);

            AssetDatabase.SaveAssets();
            return result;
        }

        private const string SourceTexPath =
            PackRoot + "/Textures/Asphalt/Road_with_pavements_v1/Road_with_pavements_v1.tga";

        /// <summary>
        /// 取得裁好的貼圖；沒有的話就從素材包的原貼圖現場裁一張。
        ///
        /// 為什麼要自己會裁：這兩張 PNG 是從 Road_with_pavements_v1.tga 裁出來的，
        /// 如果檔案不見了（沒進版控、被刪、在 Unity 外面產生但還沒被匯入），
        /// 整個選單就只會報錯。自己裁的話，只要素材包在就一定跑得起來。
        ///
        /// top / bottom 是「從圖片上緣算起」的比例（0 = 最上面）。
        /// </summary>
        private static Texture2D EnsureCrop(string outPath, float top, float bottom)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (tex != null) return tex;

            // 檔案在、但 Unity 還沒匯入（在 Unity 外面寫進去的檔案常常這樣）
            if (System.IO.File.Exists(outPath))
            {
                AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
                if (tex != null) return tex;
            }

            // 從原貼圖裁。要讀像素，原貼圖得暫時設成可讀、不壓縮，裁完還原。
            var importer = AssetImporter.GetAtPath(SourceTexPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[地圖] 找不到原貼圖 {SourceTexPath}，沒辦法裁人行道／路緣。");
                return null;
            }

            bool wasReadable = importer.isReadable;
            var wasCompression = importer.textureCompression;
            try
            {
                if (!wasReadable || wasCompression != TextureImporterCompression.Uncompressed)
                {
                    importer.isReadable = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }

                var src = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTexPath);
                // Unity 的像素座標原點在左下角，要把「從上緣算」換過來。
                // 用比例而不是固定像素：匯入時如果被縮小（Max Size < 2048），位置照樣對。
                int y0 = Mathf.RoundToInt((1f - bottom) * src.height);
                int y1 = Mathf.RoundToInt((1f - top) * src.height);
                int h = Mathf.Max(1, y1 - y0);

                var pixels = src.GetPixels(0, y0, src.width, h);
                var crop = new Texture2D(src.width, h, TextureFormat.RGB24, false);
                crop.SetPixels(pixels);
                crop.Apply();

                EnsureDir(System.IO.Path.GetDirectoryName(outPath).Replace('\\', '/'));
                System.IO.File.WriteAllBytes(outPath, crop.EncodeToPNG());
                Object.DestroyImmediate(crop);
            }
            finally
            {
                if (importer.isReadable != wasReadable || importer.textureCompression != wasCompression)
                {
                    importer.isReadable = wasReadable;
                    importer.textureCompression = wasCompression;
                    importer.SaveAndReimport();
                }
            }

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            if (tex != null) Debug.Log($"[地圖] 已從原貼圖裁出 {outPath}");
            return tex;
        }

        private static Material TexturedMat(string name, Material template, Texture2D tex)
        {
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(template) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = template.shader;
            }

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            mat.mainTextureScale = Vector2.one;
            mat.mainTextureOffset = Vector2.zero;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ================================================================ 幾何

        /// <summary>
        /// 三個子網格：0 人行道、1 柏油、2 路緣。
        /// UV 以公尺計，每格整數次重複 —— 相鄰兩格的貼圖接得起來：
        ///   柏油   每 tile/2 公尺重複一次（一格 2 次）
        ///   人行道 橫向每格 1 次、縱向每 tile/8 一排磚（一格 8 排）
        ///   路緣   沿長邊每 tile/2 重複一次
        /// </summary>
        private static GameObject BuildPiece(Piece p, float tile, Material[] mats)
        {
            float h = tile * 0.5f;
            float c = tile * CurbFrac;
            float hc = p.Center * tile;

            var sidewalk = new MeshPart();
            var asphalt = new MeshPart();
            var curb = new MeshPart();

            // 1. 人行道：整格。直路的磚排跟路平行（東西向的路 -> 磚排沿 X）
            //    **支道不鋪**：車道以外露出地板，留給兩旁的建築伸過來佔
            bool rowsAlongZ = p.N > 0 && p.S > 0 && p.E == 0 && p.W == 0;
            if (p.Sidewalk)
                sidewalk.Quad(-h, h, -h, h, SidewalkY, tile, tile / 8f, rowsAlongZ, h);

            // 2. 柏油：中央方塊 + 每個接口一條臂
            float asphaltRep = tile * 0.5f;
            if (hc > 0f) asphalt.Quad(-hc, hc, -hc, hc, AsphaltY, asphaltRep, asphaltRep, false, h);
            foreach (var side in Sides)
            {
                float a = p.Arm(side) * tile;
                if (a <= 0f) continue;
                Rect(side, hc, h, a, out float x0, out float x1, out float z0, out float z1);
                asphalt.Quad(x0, x1, z0, z1, AsphaltY, asphaltRep, asphaltRep, false, h);
            }

            // 3. 路緣
            //   a. 每條臂的兩側邊：從中央方塊邊緣到格子邊緣
            //   b. 中央方塊的每一邊，扣掉那一邊臂佔掉的區段，剩下的畫路緣
            //   c. 中央方塊的四個角各補一小塊，讓路緣轉角接起來
            float curbRep = tile * 0.5f;
            foreach (var side in Sides)
            {
                float a = p.Arm(side) * tile;
                if (a > 0f)
                {
                    // 臂的兩側
                    foreach (float s in new[] { -1f, 1f })
                    {
                        float off = s * (a + c * 0.5f);
                        switch (side)
                        {
                            case Side.N: curb.Strip(off - c * 0.5f, off + c * 0.5f, hc, h, CurbY, curbRep, alongZ: true, h); break;
                            case Side.S: curb.Strip(off - c * 0.5f, off + c * 0.5f, -h, -hc, CurbY, curbRep, alongZ: true, h); break;
                            case Side.E: curb.Strip(hc, h, off - c * 0.5f, off + c * 0.5f, CurbY, curbRep, alongZ: false, h); break;
                            case Side.W: curb.Strip(-h, -hc, off - c * 0.5f, off + c * 0.5f, CurbY, curbRep, alongZ: false, h); break;
                        }
                    }
                }

                if (hc <= 0f) continue;

                // 中央方塊這一邊，臂沒佔到的區段
                foreach (var (from, to) in Uncovered(-hc, hc, a))
                {
                    switch (side)
                    {
                        case Side.N: curb.Strip(from, to, hc, hc + c, CurbEdgeY, curbRep, alongZ: false, h); break;
                        case Side.S: curb.Strip(from, to, -hc - c, -hc, CurbEdgeY, curbRep, alongZ: false, h); break;
                        case Side.E: curb.Strip(hc, hc + c, from, to, CurbEdgeY, curbRep, alongZ: true, h); break;
                        case Side.W: curb.Strip(-hc - c, -hc, from, to, CurbEdgeY, curbRep, alongZ: true, h); break;
                    }
                }
            }

            if (hc > 0f)
                foreach (float sx in new[] { -1f, 1f })
                    foreach (float sz in new[] { -1f, 1f })
                    {
                        float x0 = sx > 0 ? hc : -hc - c, z0 = sz > 0 ? hc : -hc - c;
                        curb.Strip(x0, x0 + c, z0, z0 + c, CurbCornerY, curbRep, alongZ: false, h);
                    }

            // 組成一個網格、三個子網格
            var mesh = new Mesh { name = p.Name };
            var verts = new List<Vector3>(); var uvs = new List<Vector2>();
            var parts = new[] { sidewalk, asphalt, curb };
            var tris = new List<int>[3];
            for (int i = 0; i < 3; i++)
            {
                int baseIndex = verts.Count;
                verts.AddRange(parts[i].Verts);
                uvs.AddRange(parts[i].Uvs);
                tris[i] = new List<int>();
                foreach (int t in parts[i].Tris) tris[i].Add(t + baseIndex);
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 3;
            for (int i = 0; i < 3; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string meshPath = $"{MeshDir}/{p.Name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(mesh, existing); mesh = existing; }
            else AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject(p.Name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.Off;   // 平面貼在地上，不需要投影
            go.AddComponent<RoadTileInfo>().designTileSize = tile;

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{p.Name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>某一側的臂：從中央方塊邊緣（hc）伸到格子邊緣（h），寬 2a。</summary>
        private static void Rect(Side side, float hc, float h, float a,
                                 out float x0, out float x1, out float z0, out float z1)
        {
            switch (side)
            {
                case Side.N: x0 = -a; x1 = a; z0 = hc; z1 = h; break;
                case Side.S: x0 = -a; x1 = a; z0 = -h; z1 = -hc; break;
                case Side.E: x0 = hc; x1 = h; z0 = -a; z1 = a; break;
                default:     x0 = -h; x1 = -hc; z0 = -a; z1 = a; break;
            }
        }

        /// <summary>[lo, hi] 扣掉 [-a, a] 之後剩下的區段。a = 0 表示整段都要畫。</summary>
        private static IEnumerable<(float, float)> Uncovered(float lo, float hi, float a)
        {
            if (a <= 0f) { yield return (lo, hi); yield break; }
            if (-a > lo + 0.001f) yield return (lo, -a);
            if (a < hi - 0.001f) yield return (a, hi);
        }

        private sealed class MeshPart
        {
            public readonly List<Vector3> Verts = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<int> Tris = new();

            /// <summary>
            /// 平放的四邊形。UV = 位置（以格子左下角為原點）÷ 重複長度，
            /// swap 為 true 時 U 沿 Z、V 沿 X（讓磚排跟南北向的路平行）。
            /// </summary>
            public void Quad(float x0, float x1, float z0, float z1, float y,
                             float repU, float repV, bool swap, float half)
            {
                int b = Verts.Count;
                Verts.Add(new Vector3(x0, y, z0));
                Verts.Add(new Vector3(x0, y, z1));
                Verts.Add(new Vector3(x1, y, z1));
                Verts.Add(new Vector3(x1, y, z0));
                foreach (var v in new[] { Verts[b], Verts[b + 1], Verts[b + 2], Verts[b + 3] })
                {
                    float u = (v.x + half), w = (v.z + half);
                    Uvs.Add(swap ? new Vector2(w / repU, u / repV) : new Vector2(u / repU, w / repV));
                }
                // 從上往下看是順時針 —— Unity 的正面
                Tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }

            /// <summary>路緣條：U 沿長邊（公尺 ÷ rep），V 橫跨整條（0~1）。</summary>
            public void Strip(float x0, float x1, float z0, float z1, float y, float rep, bool alongZ, float half)
            {
                int b = Verts.Count;
                Verts.Add(new Vector3(x0, y, z0));
                Verts.Add(new Vector3(x0, y, z1));
                Verts.Add(new Vector3(x1, y, z1));
                Verts.Add(new Vector3(x1, y, z0));
                for (int i = 0; i < 4; i++)
                {
                    var v = Verts[b + i];
                    float along = alongZ ? (v.z + half) / rep : (v.x + half) / rep;
                    float across = alongZ ? (Mathf.Approximately(v.x, x0) ? 0f : 1f)
                                          : (Mathf.Approximately(v.z, z0) ? 0f : 1f);
                    Uvs.Add(new Vector2(along, across));
                }
                Tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
        }

        private static void EnsureDir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
