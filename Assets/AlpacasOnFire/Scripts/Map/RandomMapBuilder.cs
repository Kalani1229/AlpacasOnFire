using System;
using System.Collections.Generic;
using AlpacasOnFire.Core;
using UnityEngine;
using UnityEngine.Serialization;

/// <author> Copilot (k: i didn't review the code, it works, so it's fine) </author>

namespace AlpacasOnFire.Map
{
    public class RandomMapBuilder : MonoBehaviour
    {

        [Header("Maze Map")]
        [Min(5)] public int mapWidth = 25;
        [Min(5)] public int mapHeight = 25;
        [Min(1f)] public float tileSize = 4f;
        [Min(1)] public int blockTileCount = 4;
        [Range(0f, 1f)] public float cycleChance = 0.12f;

        [Header("Floor and Roads")]
        public GameObject floorPrefab;

        // 原本只有一組 roadPrefabs；分成兩級之後舊的那組就是幹道。
        // FormerlySerializedAs 讓場景上原本拉好的七格自動搬過來，不會變成空的。
        [FormerlySerializedAs("roadPrefabs")]
        [Tooltip("幹道（貫穿全圖的直線，車道寬）用的七片。")]
        public RoadPrefabSet arterialPrefabs;

        [Tooltip("支道（其餘的迷宮走廊，車道窄）用的七片。某一格留空就沿用幹道那一片。")]
        public RoadPrefabSet alleyPrefabs;

        [Tooltip("幹道直直穿過、左右兩側是支道的十字路口（側枝畫成支道寬度，接縫才對得上）。\n" +
                 "留空就用 arterialPrefabs.crossroad。")]
        public GameObject arterialCrossAlleyPrefab;

        [Min(0f)] public float groundY;

        [Header("Arterials")]
        [Min(0)] public int arterialRows = 2;     // 橫向幹道幾條
        [Min(0)] public int arterialCols = 2;     // 縱向幾條
        [Tooltip("同方向的幹道之間至少隔幾個街區。設太大的話小地圖每次都只放得下同一組位置。")]
        [Min(1)] public int arterialMinGap = 2;

        [Header("Alleys")]
        [Tooltip("支道車道寬度，佔一格的比例（0.36 = 格子 10 公尺時車道 3.6 公尺）。\n" +
                 "兩個地方會讀它：\n" +
                 "・美術路面（選單 6）照這個寬度畫支道車道，支道不鋪人行道\n" +
                 "・支道兩旁的建築與小物件可以伸進支道那一格，一直到路緣外側為止\n" +
                 "改了之後要重跑選單 6 讓路面跟著變。設 1 = 不讓建築伸進支道。")]
        [Range(0.2f, 1f)] public float alleyLaneRatio = 0.36f;

        /// <summary>路緣寬度，佔一格的比例。美術路面與「建築能伸多遠」共用，兩邊才會剛好貼齊。</summary>
        public const float CurbFraction = 0.05f;

        [Header("Buildings")]
        public GameObject[] buildingPrefabs;
        [Tooltip("Random uniform scale range for buildings.")]
        public Vector2 buildingScaleRange = Vector2.one;
        [Min(0)] public int minBuildingsPerBlock = 1;
        [Min(0)] public int maxBuildingsPerBlock = 3;

        [Header("Small Objects")]
        public GameObject[] smallObjectPrefabs;
        [Tooltip("Random uniform scale range for small objects.")]
        public Vector2 smallObjectScaleRange = Vector2.one;
        [Min(0)] public int minSmallObjectsPerBlock = 2;
        [Min(0)] public int maxSmallObjectsPerBlock = 8;

        [Header("Placement")]
        [Min(1)] public int placementAttempts = 30;
        [Min(0f)] public float clearance = 0.35f;

        // ---- 以下是地圖 A 批次新增的欄位 ----

        [Header("Plazas")]
        [Tooltip("保留幾個淨空的廣場（擺攤用）。")]
        [Min(1)] public int plazaCount = 4;
        [Tooltip("廣場地面用的 prefab。留空就沿用一般地面，但染成 plazaFloorTint。")]
        public GameObject plazaFloorPrefab;
        [Tooltip("沒有指定 plazaFloorPrefab 時，把一般地面染成這個顏色。要跟街道、一般地面一眼分得出來。")]
        public Color plazaFloorTint = new Color(0.92f, 0.66f, 0.38f);
        [Tooltip("廣場之間的最小中心距離，佔地圖邊長的比例。太近的話四個廣場會擠在同一角。")]
        [Range(0f, 0.5f)] public float plazaMinSpacing = 0.2f;

        [Header("Boundary Regions")]
        [Tooltip("碰到地圖邊界的非道路區域要不要也放建築。\n" +
                 "迷宮沒打通的牆會一路連到外圍，這種區域常佔非道路面積的三到七成，" +
                 "不放的話會變成一大片沒標示的空地。")]
        public bool fillBoundaryRegions = true;

        [Header("Border")]
        [Tooltip("外圍用來擋住玩家的建築。留空就用 buildingPrefabs；兩個都空就生成方塊牆。")]
        public GameObject[] borderPrefabs;
        [Min(1)] public int borderThickness = 1;   // 外圍幾格厚
        [Tooltip("外牆最低高度（公尺）。要明顯高於羊駝（1.8），讀起來才是牆不是障礙物。")]
        [Min(2f)] public float borderMinHeight = 5f;
        [Tooltip("外牆建築蓋滿格子之外再多撐的比例，讓相鄰建築重疊一點、不留縫。")]
        [Range(1f, 1.5f)] public float borderOverlap = 1.15f;

        [Header("Generation")]
        public bool generateOnStart = true;
        public bool useRandomSeed;
        public int seed = 12345;

        // public functions ---

        [ContextMenu("Generate Map")]
        public void GenerateMap()
        {
            ClearGeneratedMap();

            int mazeWidth = MakeOdd(Mathf.Max(5, mapWidth));
            int mazeHeight = MakeOdd(Mathf.Max(5, mapHeight));
            int spacing = Mathf.Max(2, blockTileCount + 1);
            int width = (mazeWidth - 1) * spacing + 1;
            int height = (mazeHeight - 1) * spacing + 1;
            var random = new System.Random(useRandomSeed ? Environment.TickCount : seed);
            var generated = new GameObject("Generated Map");
            generated.transform.SetParent(transform, false);
            generatedRoot = generated.transform;

            bool[,] mazeRoads = BuildMaze(mazeWidth, mazeHeight, random);
            bool[,] expanded = ExpandRoadMap(mazeRoads, spacing, width, height);

            // 道路分級：RoadType[,] 是唯一的真相，下游（GetRoadMask、FindBlocks、廣場、外牆）
            // 讀的是從它衍生的 bool[,]，那幾支完全不用改。
            RoadType[,] roadTypes = ClassifyRoads(expanded, random, mazeWidth, mazeHeight, spacing, width, height);
            bool[,] roads = ToRoadMask(roadTypes, width, height);
            _roadTypes = roadTypes;   // IsInsideBlock 要知道哪些格子是支道
            _plazaCenters.Clear();
            StoreArterialCenters(roadTypes, width, height);

            // 街區與廣場要在鋪地面之前算出來 —— 廣場的地面跟一般地面不同。
            // （FindBlocks 不用亂數，所以提前呼叫不會改變亂數序列；
            //   廣場挑選插在街區內容之前，同一個 seed 仍然每次都得到同一張圖。）
            var blocks = FindBlocks(roads, width, height);
            blocks = TrimBorderRing(blocks, width, height);
            var plazaCells = PickPlazas(blocks, roads, random, width, height);

            GenerateFloor(width, height, plazaCells);
            GenerateRoads(roads, roadTypes, width, height);

            foreach (var block in blocks)
                GenerateBlockContents(block, random, width, height);

            GenerateBorder(random, width, height);

            if (blocks.Count == 0)
                Debug.LogWarning("RandomMapBuilder found no enclosed blocks. Increase map size or cycle chance.", this);

            LogSummary(roadTypes, width, height);

            // ---- 地圖 B：NavMesh 一定是最後一步 ----
            // 之後加進來的東西（路障、死路…）只要生在 Generated Map 底下，就會自動被烤進去。
            _mapBounds = new Bounds(transform.position,
                                    new Vector3(width * tileSize, 0f, height * tileSize));
            EnsureCollidersOnGenerated();
            BuildNavMesh();
            _generatedThisSession = Application.isPlaying;
        }

        [ContextMenu("Clear Generated Map")]
        public void ClearGeneratedMap()
        {
            // NavMesh 是直接加進 NavMesh 系統的，不跟著物件一起刪 —— 要自己拿掉
            RemoveNavMesh();
            _generatedThisSession = false;

            // 除了記住的那一份，也把底下所有叫 "Generated Map" 的都清掉 ——
            // 萬一有一份沒被記住（Undo、生成到一半出錯），它會變成孤兒，按 Clear 永遠清不到。
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == generatedRoot || child.name != "Generated Map") continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }

            if (generatedRoot == null) return;

            if (Application.isPlaying) Destroy(generatedRoot.gameObject);
            else DestroyImmediate(generatedRoot.gameObject);

            generatedRoot = null;
        }

        // life cycle ---

        private void Start()
        {
            // GameLauncher 會在啟動 Runner 之前先呼叫 EnsureGenerated()。
            // 這裡保留給沒有 GameLauncher 的場景；兩邊誰先跑都沒關係，第二個會跳過。
            if (Application.isPlaying && generateOnStart) EnsureGenerated();
        }

        private void OnDestroy()
        {
            if (!Application.isPlaying) ClearGeneratedMap();
            else RemoveNavMesh();   // 換場景時把這張地圖的 NavMesh 一起拿掉
        }

        // private functions ---

        private bool[,] BuildMaze(int width, int height, System.Random random)
        {
            var roads = new bool[width, height];
            var stack = new List<Vector2Int> { new Vector2Int(1, 1) };
            roads[1, 1] = true;

            while (stack.Count > 0)
            {
                var current = stack[stack.Count - 1];
                var directions = ShuffledDirections(random);
                bool advanced = false;

                foreach (var direction in directions)
                {
                    var next = current + direction * 2;
                    if (next.x <= 0 || next.x >= width - 1 || next.y <= 0 || next.y >= height - 1)
                        continue;
                    if (roads[next.x, next.y]) continue;

                    roads[current.x + direction.x, current.y + direction.y] = true;
                    roads[next.x, next.y] = true;
                    stack.Add(next);
                    advanced = true;
                    break;
                }

                if (!advanced) stack.RemoveAt(stack.Count - 1);
            }

            int loopAttempts = Mathf.RoundToInt(width * height * cycleChance);
            for (int i = 0; i < loopAttempts; i++)
            {
                int x = random.Next(1, width - 1);
                int y = random.Next(1, height - 1);
                if (roads[x, y]) continue;

                bool horizontal = roads[x - 1, y] && roads[x + 1, y];
                bool vertical = roads[x, y - 1] && roads[x, y + 1];
                if (horizontal || vertical) roads[x, y] = true;
            }

            return roads;
        }

        private static bool[,] ExpandRoadMap(bool[,] mazeRoads, int spacing, int width, int height)
        {
            var roads = new bool[width, height];
            int mazeWidth = mazeRoads.GetLength(0);
            int mazeHeight = mazeRoads.GetLength(1);

            for (int y = 0; y < mazeHeight; y++)
            {
                for (int x = 0; x < mazeWidth; x++)
                {
                    if (!mazeRoads[x, y]) continue;

                    int startX = x * spacing;
                    int startY = y * spacing;
                    roads[startX, startY] = true;

                    if (x + 1 < mazeWidth && mazeRoads[x + 1, y])
                    {
                        for (int roadX = startX + 1; roadX <= startX + spacing; roadX++)
                            roads[roadX, startY] = true;
                    }

                    if (y + 1 < mazeHeight && mazeRoads[x, y + 1])
                    {
                        for (int roadY = startY + 1; roadY <= startY + spacing; roadY++)
                            roads[startX, roadY] = true;
                    }
                }
            }

            return roads;
        }

        /// <summary>
        /// 鋪地面。廣場的格子換成 plazaFloorPrefab，沒有的話用一般地面但染色 ——
        /// **廣場一定要看得出來**，不然「哪裡擺得出攤位」會變成玩家試錯。
        /// </summary>
        private void GenerateFloor(int width, int height, HashSet<Vector2Int> plazaCells)
        {
            if (floorPrefab == null && plazaFloorPrefab == null)
            {
                Debug.LogWarning("RandomMapBuilder needs a floorPrefab to create ground under roads and buildings.", this);
                return;
            }

            MaterialPropertyBlock tint = null;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var position = GridToWorld(x, y, width, height);
                    bool plaza = plazaCells != null && plazaCells.Contains(new Vector2Int(x, y));

                    if (!plaza)
                    {
                        if (floorPrefab != null)
                            InstantiateGenerated(floorPrefab, position, Quaternion.identity, "Floor");
                        continue;
                    }

                    if (plazaFloorPrefab != null)
                    {
                        InstantiateGenerated(plazaFloorPrefab, position, Quaternion.identity, "Plaza Floor");
                        continue;
                    }

                    if (floorPrefab == null) continue;

                    // 沒有專用的廣場地面：一般地面 + 染色。用 MaterialPropertyBlock，
                    // 不會改到共用的材質資產（改了的話整張地圖的地面都會一起變色）。
                    var instance = InstantiateGenerated(floorPrefab, position, Quaternion.identity, "Plaza Floor");
                    tint ??= MakeTintBlock(plazaFloorTint);
                    foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                        r.SetPropertyBlock(tint);
                }
            }
        }

        private static MaterialPropertyBlock MakeTintBlock(Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);   // URP Lit
            block.SetColor("_Color", color);       // Built-in / 舊 shader
            return block;
        }

        /// <summary>
        /// 鋪路面。mask -> 哪一片、轉幾度的邏輯沒變，只是先看這一格是哪一級，決定從哪一組拿。
        ///
        /// 特例：「幹道直直穿過、左右兩側是支道」的十字。mask 跟幹道碰幹道一樣是 4，
        /// 但側枝要畫成支道寬度，不然每個這種路口都有錯位 —— 而且它比幹道碰幹道多得多。
        /// </summary>
        private void GenerateRoads(bool[,] roads, RoadType[,] types, int width, int height)
        {
            if (arterialPrefabs == null && alleyPrefabs == null && arterialCrossAlleyPrefab == null) return;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!roads[x, y]) continue;

                    int mask = GetRoadMask(roads, x, y, width, height);
                    var type = types[x, y];
                    GameObject prefab;
                    Quaternion rotation;

                    if (type == RoadType.Arterial && arterialCrossAlleyPrefab != null
                        && IsArterialThroughAlleys(types, x, y, width, height, out bool alongX))
                    {
                        // 基準：幹道南北向、支道從東西進來；幹道是東西向就轉 90°
                        prefab = arterialCrossAlleyPrefab;
                        rotation = alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
                    }
                    else
                    {
                        var set = type == RoadType.Arterial ? arterialPrefabs : alleyPrefabs;
                        prefab = GetRoadPrefab(mask, set, out rotation);

                        // 支道那組還沒拉好就沿用幹道那一片，至少地上有路
                        if (prefab == null && type == RoadType.Alley)
                            prefab = GetRoadPrefab(mask, arterialPrefabs, out rotation);
                    }

                    if (prefab == null) continue;

                    var instance = InstantiateGenerated(prefab, GridToWorld(x, y, width, height), rotation, "Road");

                    // 佔位路面固定做成某個邊長（RoadTileInfo），這裡縮放成場景的 tileSize，
                    // 路面之間才不會有縫或重疊。只縮水平、不縮厚度。
                    // 美術的路面沒有掛 RoadTileInfo，原樣生成。
                    var info = instance.GetComponent<RoadTileInfo>();
                    if (info != null && info.designTileSize > 0.001f)
                    {
                        float k = tileSize / info.designTileSize;
                        instance.transform.localScale = Vector3.Scale(instance.transform.localScale,
                                                                      new Vector3(k, 1f, k));
                    }
                }
            }
        }

        private List<BlockRegion> FindBlocks(bool[,] roads, int width, int height)
        {
            var visited = new bool[width, height];
            var blocks = new List<BlockRegion>();

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (roads[x, y] || visited[x, y]) continue;

                    var region = new BlockRegion();
                    var pending = new Queue<Vector2Int>();
                    pending.Enqueue(new Vector2Int(x, y));
                    visited[x, y] = true;
                    bool touchesBoundary = false;

                    while (pending.Count > 0)
                    {
                        var cell = pending.Dequeue();
                        region.cells.Add(cell);
                        region.minX = Mathf.Min(region.minX, cell.x);
                        region.maxX = Mathf.Max(region.maxX, cell.x);
                        region.minY = Mathf.Min(region.minY, cell.y);
                        region.maxY = Mathf.Max(region.maxY, cell.y);
                        touchesBoundary |= cell.x == 0 || cell.x == width - 1 || cell.y == 0 || cell.y == height - 1;

                        foreach (var direction in CardinalDirections)
                        {
                            var next = cell + direction;
                            if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height)
                            {
                                touchesBoundary = true;
                                continue;
                            }

                            if (!roads[next.x, next.y] && !visited[next.x, next.y])
                            {
                                visited[next.x, next.y] = true;
                                pending.Enqueue(next);
                            }
                        }
                    }

                    // 原本碰到邊界的區域一律丟掉。但迷宮沒打通的牆會一路連到外圍，
                    // 這種區域常佔非道路面積的三到七成、而且深入地圖內部 ——
                    // 丟掉就是一大片沒建築的空地。現在由 fillBoundaryRegions 決定。
                    // （外圍那一圈會在 TrimBorderRing 扣掉，留給外牆。）
                    region.touchesBoundary = touchesBoundary;
                    if ((fillBoundaryRegions || !touchesBoundary) && region.cells.Count >= 1) blocks.Add(region);
                }
            }

            return blocks;
        }

        private void GenerateBlockContents(BlockRegion block, System.Random random, int width, int height)
        {
            if (block.cells.Count == 0) return;

            // **數量依面積放大。** 原本每個區域都是 min~max 棟，不管區域多大 ——
            // 一個幾百格的邊界區域跟一個 2×2 的小街區拿到一樣多的建築，大區域還是空的。
            //
            // 基準面積是「一根迷宮柱子周圍的街區」：邊長 2 × blockTileCount + 1 格，
            // 這是展開後最常見的街區大小。比它小或差不多的街區維持原本的數量（倍率 1），
            // 所以場景上調好的 min/max 對一般街區完全不變。上限 64 倍，免得生成時卡太久。
            int referenceSide = 2 * Mathf.Max(1, blockTileCount) + 1;
            int units = Mathf.Clamp(Mathf.RoundToInt(block.cells.Count / (float)(referenceSide * referenceSide)), 1, 64);

            int buildingCount = 0, smallObjectCount = 0;
            for (int u = 0; u < units; u++)
            {
                buildingCount += RandomRange(random, minBuildingsPerBlock, maxBuildingsPerBlock);
                smallObjectCount += RandomRange(random, minSmallObjectsPerBlock, maxSmallObjectsPerBlock);
            }
            var placedBounds = new List<Bounds>();

            for (int i = 0; i < buildingCount; i++)
                TryPlaceInBlock(buildingPrefabs, block, random, placedBounds, "Building", width, height);

            for (int i = 0; i < smallObjectCount; i++)
                TryPlaceInBlock(smallObjectPrefabs, block, random, placedBounds, "Small Object", width, height);
        }

        private void TryPlaceInBlock(GameObject[] prefabs, BlockRegion block, System.Random random,
                         List<Bounds> placedBounds, string category, int width, int height)
        {
            if (!HasPrefab(prefabs)) return;

            for (int attempt = 0; attempt < placementAttempts; attempt++)
            {
                var cell = block.cells[random.Next(block.cells.Count)];
                var position = GridToWorld(cell.x, cell.y, width, height);
                position += new Vector3(NextFloat(random, -tileSize * 0.4f, tileSize * 0.4f), 0f,
                                        NextFloat(random, -tileSize * 0.4f, tileSize * 0.4f));
                var rotation = Quaternion.Euler(0f, random.Next(0, 4) * 90f, 0f);
                var prefab = GetRandomPrefab(prefabs, random);
                if (prefab == null) return;

                var instance = InstantiateGenerated(prefab, position, rotation, category);
                float scale = GetScaleForCategory(category, random);
                instance.transform.localScale *= scale;
                var bounds = CalculateWorldBounds(instance);
                if (bounds.size == Vector3.zero) bounds = new Bounds(instance.transform.position, Vector3.one);
                instance.transform.position += Vector3.up * (groundY - bounds.min.y);
                bounds = CalculateWorldBounds(instance);
                bounds.Expand(clearance * 2f);

                if (!IsInsideBlock(bounds, block, width, height) || IntersectsAny(bounds, placedBounds))
                {
                    DestroyGenerated(instance);
                    continue;
                }

                placedBounds.Add(bounds);
                return;
            }
        }

        private float GetScaleForCategory(string category, System.Random random)
        {
            Vector2 range = category == "Building" ? buildingScaleRange : smallObjectScaleRange;
            float min = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
            float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
            return NextFloat(random, min, max);
        }

        private GameObject InstantiateGenerated(GameObject prefab, Vector3 position, Quaternion rotation, string category)
        {
            var instance = Instantiate(prefab, position, rotation);
            instance.name = category + " - " + prefab.name;
            instance.transform.SetParent(generatedRoot, true);
            return instance;
        }

        /// <summary>
        /// 這個包圍盒放得進這個街區嗎。
        ///
        /// **支道變窄就在這裡。** 支道只畫中間的車道、不鋪人行道；車道兩旁那一段
        /// 讓街區的建築與小物件可以伸進去，一直到路緣外側為止：
        ///   可以伸的量 = 格子寬 × (1 − 車道比例) / 2 − 路緣寬
        ///
        /// 規則分兩層：
        ///   1. 包圍盒往內縮掉那段量之後，必須完全在街區裡（就是原本的規則）
        ///   2. 完整的包圍盒碰到的格子，只能是這個街區或**支道**
        /// 所以伸出去的那一截只會進支道：幹道、廣場、外牆、別的街區都不行。
        ///
        /// 支道的路口（十字、T 字）四個角落本來就不是車道，建築的角伸進去也碰不到車道。
        /// </summary>
        private bool IsInsideBlock(Bounds bounds, BlockRegion block, int width, int height)
        {
            float encroach = _roadTypes == null ? 0f
                : Mathf.Max(0f, tileSize * ((1f - alleyLaneRatio) * 0.5f - CurbFraction));

            var core = bounds;
            if (encroach > 0f)
            {
                var size = core.size;
                size.x = Mathf.Max(0.01f, size.x - 2f * encroach);
                size.z = Mathf.Max(0.01f, size.z - 2f * encroach);
                core.size = size;   // 中心不變，四周各縮 encroach
            }

            if (!AllCells(core, width, height, (x, y) => block.Contains(x, y))) return false;
            if (encroach <= 0f) return true;

            return AllCells(bounds, width, height, (x, y) =>
                block.Contains(x, y)
                || (x >= 0 && y >= 0 && x < width && y < height && _roadTypes[x, y] == RoadType.Alley));
        }

        private bool AllCells(Bounds b, int width, int height, Func<int, int, bool> ok)
        {
            int minX = WorldToGridX(b.min.x, width);
            int maxX = WorldToGridX(b.max.x, width);
            int minY = WorldToGridZ(b.min.z, height);
            int maxY = WorldToGridZ(b.max.z, height);

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    if (!ok(x, y)) return false;
            return true;
        }

        private RoadType[,] _roadTypes;

        // 唯一的改動：「從哪一組拿」變成參數。mask -> 哪一片、轉幾度一行都沒動。
        private GameObject GetRoadPrefab(int mask, RoadPrefabSet set, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (set == null) return null;
            int count = CountBits(mask);

            if (count == 4) return set.crossroad;
            if (count == 3)
            {
                rotation = RotationForMask(mask, 0b0111);
                return set.tJunction;
            }

            if (count == 2)
            {
                if (mask == 0b0101) return set.vertical;
                if (mask == 0b1010) return set.horizontal;
                rotation = RotationForMask(mask, 0b1001);
                return set.corner;
            }

            if (count == 1)
            {
                rotation = RotationForMask(mask, 0b0001);
                return set.deadEnd;
            }

            return set.isolated;
        }

        private static Quaternion RotationForMask(int mask, int baseMask)
        {
            for (int quarterTurns = 0; quarterTurns < 4; quarterTurns++)
            {
                int rotated = ((baseMask << quarterTurns) | (baseMask >> (4 - quarterTurns))) & 0b1111;
                if (rotated == mask) return Quaternion.Euler(0f, quarterTurns * 90f, 0f);
            }

            return Quaternion.identity;
        }

        private static int GetRoadMask(bool[,] roads, int x, int y, int width, int height)
        {
            int mask = 0;
            if (y + 1 < height && roads[x, y + 1]) mask |= 1;
            if (x + 1 < width && roads[x + 1, y]) mask |= 2;
            if (y - 1 >= 0 && roads[x, y - 1]) mask |= 4;
            if (x - 1 >= 0 && roads[x - 1, y]) mask |= 8;
            return mask;
        }

        private static List<Vector2Int> ShuffledDirections(System.Random random)
        {
            var directions = new List<Vector2Int>(CardinalDirections);
            for (int i = directions.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                (directions[i], directions[other]) = (directions[other], directions[i]);
            }

            return directions;
        }

        private Vector3 GridToWorld(int x, int y, int width, int height)
        {
            float offsetX = (width - 1) * tileSize * 0.5f;
            float offsetZ = (height - 1) * tileSize * 0.5f;
            return transform.position + new Vector3(x * tileSize - offsetX, groundY, y * tileSize - offsetZ);
        }

        private int WorldToGridX(float coordinate, int width)
        {
            float local = coordinate - transform.position.x;
            float mapMin = -(width * tileSize) * 0.5f;
            return Mathf.FloorToInt((local - mapMin) / tileSize);
        }

        private int WorldToGridZ(float coordinate, int height)
        {
            float local = coordinate - transform.position.z;
            float mapMin = -(height * tileSize) * 0.5f;
            return Mathf.FloorToInt((local - mapMin) / tileSize);
        }

        private static int MakeOdd(int value) => value % 2 == 0 ? value + 1 : value;

        private static int CountBits(int value)
        {
            int count = 0;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }

            return count;
        }

        private static int RandomRange(System.Random random, int min, int max)
        {
            return random.Next(Mathf.Min(min, max), Mathf.Max(min, max) + 1);
        }

        private static float NextFloat(System.Random random, float min, float max)
        {
            return min + (float)random.NextDouble() * (max - min);
        }

        private static GameObject GetRandomPrefab(GameObject[] prefabs, System.Random random)
        {
            for (int i = 0; i < prefabs.Length; i++)
            {
                var prefab = prefabs[random.Next(prefabs.Length)];
                if (prefab != null) return prefab;
            }

            return null;
        }

        private static bool HasPrefab(GameObject[] prefabs)
        {
            if (prefabs == null) return false;
            for (int i = 0; i < prefabs.Length; i++)
                if (prefabs[i] != null) return true;
            return false;
        }

        private static bool IntersectsAny(Bounds candidate, List<Bounds> placedBounds)
        {
            foreach (var other in placedBounds)
            {
                if (candidate.min.x < other.max.x && candidate.max.x > other.min.x
                    && candidate.min.y < other.max.y && candidate.max.y > other.min.y
                    && candidate.min.z < other.max.z && candidate.max.z > other.min.z)
                    return true;
            }

            return false;
        }

        private static Bounds CalculateWorldBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            var colliders = instance.GetComponentsInChildren<Collider>(true);
            var bounds = new Bounds(instance.transform.position, Vector3.zero);
            bool hasBounds = false;

            foreach (var renderer in renderers)
            {
                if (!hasBounds) bounds = renderer.bounds;
                else bounds.Encapsulate(renderer.bounds);
                hasBounds = true;
            }

            foreach (var collider in colliders)
            {
                if (!hasBounds) bounds = collider.bounds;
                else bounds.Encapsulate(collider.bounds);
                hasBounds = true;
            }

            return bounds;
        }

        private void DestroyGenerated(GameObject instance)
        {
            if (Application.isPlaying)
            {
                // **Play 中的 Destroy 要等到這一幀結束才真的生效。**
                // 擺建築時每棟最多試 20 次位置，放不下的都在這裡被刪 —— 可是 NavMesh
                // 在同一幀就烤了，那兩萬多個「準備刪掉」的建築還在，全被當成障礙物烤進去，
                // 把路面蓋光（道路抽樣 0/20、障礙 20548 個就是這個）。
                // 先關掉、移出地圖，後面的步驟就看不到它了。
                instance.SetActive(false);
                instance.transform.SetParent(null, false);
                Destroy(instance);
            }
            else DestroyImmediate(instance);
        }

        // ================================================================ 地圖 A：外圈、廣場、外牆

        private bool InBorderRing(int x, int y, int width, int height)
        {
            int t = BorderThicknessClamped(width, height);
            return x < t || y < t || x >= width - t || y >= height - t;
        }

        private int BorderThicknessClamped(int width, int height)
            => Mathf.Clamp(borderThickness, 1, Mathf.Max(1, Mathf.Min(width, height) / 2 - 1));

        /// <summary>
        /// 把外牆那一圈格子從所有街區扣掉，外牆與街區內容才不會疊在一起。
        ///
        /// 道路永遠碰不到網格最外圈（迷宮的外框從來不會被打通，展開後外面
        /// 有 blockTileCount + 1 格厚的空帶），所以這一圈一定只屬於邊界區域。
        /// </summary>
        private List<BlockRegion> TrimBorderRing(List<BlockRegion> blocks, int width, int height)
        {
            var result = new List<BlockRegion>(blocks.Count);
            foreach (var block in blocks)
            {
                if (block.touchesBoundary) block.RemoveWhere(c => InBorderRing(c.x, c.y, width, height));
                if (block.cells.Count > 0) result.Add(block);
            }
            return result;
        }

        /// <summary>廣場邊長（格數）：剛好放得下襯布，四周再留 2 公尺走道。</summary>
        private int PlazaSizeTiles =>
            Mathf.Max(1, Mathf.CeilToInt((GameTuning.StallMatSize + 4f) / Mathf.Max(0.01f, tileSize) - 0.001f));

        /// <summary>
        /// 挑出 plazaCount 個廣場，把廣場的格子從街區扣掉（之後不會放任何東西），回傳所有廣場格。
        ///
        /// **不是整個街區清空**，是在街區裡挖一塊固定大小的正方形 ——
        /// 展開後的街區大多是 9×9 格以上，還有不少不規則的 L／T／樹枝形，
        /// 整塊清空的話會出現幾十公尺寬的空地。固定大小讓每個廣場都差不多大。
        ///
        /// 「放不放得下」用**實際形狀**判斷（整塊正方形的每一格都要在街區裡），
        /// 不是外框：不規則街區的外框很大，但中間可能是空的。
        ///
        /// 不需要另外登記擺攤點：擺攤的檢測本來就是幾何的（平坦度 + 淨空 + 不重疊），
        /// 一塊真正空的地自己就會通過。
        /// </summary>
        private HashSet<Vector2Int> PickPlazas(List<BlockRegion> blocks, bool[,] roads,
                                               System.Random random, int width, int height)
        {
            var plazaCells = new HashSet<Vector2Int>();
            int size = PlazaSizeTiles;

            // 每個街區找一個最好的位置當候選
            var candidates = new List<(BlockRegion block, RectInt rect)>();
            foreach (var block in blocks)
                if (TryFindPlazaSquare(block, roads, size, random, width, height, out var rect))
                    candidates.Add((block, rect));

            // 用傳進來的 random 洗牌（Fisher–Yates），同一個 seed 每次挑到同一組
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            // 依序挑，跳過離已選廣場太近的 —— 擠在一起的話玩家還是只有一個選擇
            float minDistance = Mathf.Max(width, height) * tileSize * plazaMinSpacing;
            var chosen = new List<(BlockRegion block, RectInt rect, Vector3 center)>();
            int rejectedForSpacing = 0;

            foreach (var c in candidates)
            {
                if (chosen.Count >= plazaCount) break;

                var center = RectCenterWorld(c.rect, width, height);
                bool tooClose = false;
                foreach (var other in chosen)
                {
                    var d = center - other.center;
                    d.y = 0f;
                    if (d.magnitude < minDistance) { tooClose = true; break; }
                }
                if (tooClose) { rejectedForSpacing++; continue; }

                chosen.Add((c.block, c.rect, center));
            }

            foreach (var c in chosen)
            {
                var rect = c.rect;
                _plazaCenters.Add(c.center);   // 地圖 B：給出生點、手提箱、之後的顧客系統查
                c.block.RemoveWhere(cell => rect.Contains(cell));
                for (int y = rect.yMin; y < rect.yMax; y++)
                    for (int x = rect.xMin; x < rect.xMax; x++)
                        plazaCells.Add(new Vector2Int(x, y));
            }

            if (chosen.Count < plazaCount)
            {
                float meters = size * tileSize;
                string reason;
                if (candidates.Count == 0)
                    reason = $"沒有任何街區放得下 {size}×{size} 格（{meters:0.#} 公尺）的廣場 —— 街區太小。" +
                             "調大 blockTileCount 或 tileSize。";
                else if (candidates.Count < plazaCount)
                    reason = $"只有 {candidates.Count} 個街區放得下 {meters:0.#} 公尺的廣場 —— 數量不夠。" +
                             "調大 mapWidth / mapHeight。";
                else
                    reason = $"放得下的街區有 {candidates.Count} 個，但有 {rejectedForSpacing} 個離其他廣場太近" +
                             $"（最小間距 {minDistance:0} 公尺）。調小 plazaMinSpacing 或調大地圖。";

                Debug.LogWarning($"[地圖] 只挑出 {chosen.Count} / {plazaCount} 個廣場：{reason}", this);
            }

            return plazaCells;
        }

        /// <summary>
        /// 在街區裡找一塊 size×size、每一格都屬於這個街區的正方形。
        ///
        /// 有多個位置可選時，**優先貼著道路的**（周圍一圈裡道路格越多越好）——
        /// 廣場要能從街上一眼看到「那裡是空的」，被建築包在街區深處就失去意義了。
        /// 同分的用 random 挑。
        /// </summary>
        private static bool TryFindPlazaSquare(BlockRegion block, bool[,] roads, int size,
                                               System.Random random, int width, int height, out RectInt rect)
        {
            rect = default;
            if (block.cells.Count < size * size) return false;
            if (block.maxX - block.minX + 1 < size || block.maxY - block.minY + 1 < size) return false;

            int bestScore = -1;
            var best = new List<RectInt>();

            for (int y0 = block.minY; y0 <= block.maxY - size + 1; y0++)
            {
                for (int x0 = block.minX; x0 <= block.maxX - size + 1; x0++)
                {
                    if (!SquareInside(block, x0, y0, size)) continue;

                    int score = RoadFrontage(roads, x0, y0, size, width, height);
                    if (score > bestScore) { bestScore = score; best.Clear(); }
                    if (score == bestScore) best.Add(new RectInt(x0, y0, size, size));
                }
            }

            if (best.Count == 0) return false;
            rect = best[random.Next(best.Count)];
            return true;
        }

        private static bool SquareInside(BlockRegion block, int x0, int y0, int size)
        {
            for (int y = y0; y < y0 + size; y++)
                for (int x = x0; x < x0 + size; x++)
                    if (!block.Contains(x, y)) return false;
            return true;
        }

        /// <summary>正方形外面緊貼的那一圈裡，有幾格是道路。</summary>
        private static int RoadFrontage(bool[,] roads, int x0, int y0, int size, int width, int height)
        {
            int count = 0;
            for (int i = 0; i < size; i++)
            {
                if (IsRoad(roads, x0 + i, y0 - 1, width, height)) count++;      // 南邊
                if (IsRoad(roads, x0 + i, y0 + size, width, height)) count++;   // 北邊
                if (IsRoad(roads, x0 - 1, y0 + i, width, height)) count++;      // 西邊
                if (IsRoad(roads, x0 + size, y0 + i, width, height)) count++;   // 東邊
            }
            return count;
        }

        private static bool IsRoad(bool[,] roads, int x, int y, int width, int height)
            => x >= 0 && y >= 0 && x < width && y < height && roads[x, y];

        private Vector3 RectCenterWorld(RectInt rect, int width, int height)
        {
            var a = GridToWorld(rect.xMin, rect.yMin, width, height);
            var b = GridToWorld(rect.xMax - 1, rect.yMax - 1, width, height);
            return (a + b) * 0.5f;
        }

        /// <summary>
        /// 外圍建築牆：沿網格外圈 borderThickness 格，逐格放建築。
        ///
        /// 每一棟都縮放到**蓋滿自己那一格再多一點**（borderOverlap），
        /// 相鄰建築會重疊、不留縫；高度至少 borderMinHeight，讀起來是牆不是障礙物。
        ///
        /// 沒有指定 borderPrefabs 就用 buildingPrefabs；兩個都空就生成方塊牆 ——
        /// **沒有美術也一定要封得起來**，不然玩家會從地圖邊緣掉下去。
        /// </summary>
        private void GenerateBorder(System.Random random, int width, int height)
        {
            var prefabs = HasPrefab(borderPrefabs) ? borderPrefabs : buildingPrefabs;
            bool usePrefabs = HasPrefab(prefabs);
            MaterialPropertyBlock fallbackTint = null;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!InBorderRing(x, y, width, height)) continue;

                    var cellCenter = GridToWorld(x, y, width, height);
                    GameObject wall;

                    if (usePrefabs)
                    {
                        var prefab = GetRandomPrefab(prefabs, random);
                        int quarterTurns = random.Next(0, 4);
                        wall = InstantiateGenerated(prefab, cellCenter, Quaternion.Euler(0f, quarterTurns * 90f, 0f), "Border");
                        FitToBorderCell(wall, cellCenter, quarterTurns % 2 == 1);
                    }
                    else
                    {
                        wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        wall.name = "Border - Wall";
                        float side = tileSize * borderOverlap;
                        // **先在世界空間定好大小與位置，再掛上去（worldPositionStays）。**
                        // 反過來做的話 localScale 會乘上父物件的縮放 —— 場景的 Tile 是 (60, 1.2, 60)，
                        // 11.5 公尺的方塊會變成 690 公尺寬，480 顆連成一整片蓋住整張地圖。
                        wall.transform.localScale = new Vector3(side, borderMinHeight, side);
                        wall.transform.position = cellCenter + Vector3.up * (borderMinHeight * 0.5f);
                        wall.transform.SetParent(generatedRoot, true);
                        fallbackTint ??= MakeTintBlock(new Color(0.55f, 0.52f, 0.5f));
                        wall.GetComponent<Renderer>().SetPropertyBlock(fallbackTint);
                    }

                    EnsureSolid(wall);
                }
            }
        }

        /// <summary>
        /// 把一棟外牆建築縮放、對位到它那一格。
        ///
        /// 水平兩軸各自縮放到 tileSize × borderOverlap（蓋滿再多一點），
        /// 高度跟著水平縮放的平均值走、但不低於 borderMinHeight。
        /// 轉了 90°／270° 的建築，世界的 X/Z 對應到它自己的 Z/X，要交換。
        /// </summary>
        private void FitToBorderCell(GameObject wall, Vector3 cellCenter, bool swapAxes)
        {
            var bounds = CalculateWorldBounds(wall);
            if (bounds.size.x < 0.01f || bounds.size.z < 0.01f || bounds.size.y < 0.01f) return;

            float target = tileSize * borderOverlap;
            float fx = target / bounds.size.x;
            float fz = target / bounds.size.z;
            float fy = (fx + fz) * 0.5f;
            if (bounds.size.y * fy < borderMinHeight) fy = borderMinHeight / bounds.size.y;

            var s = wall.transform.localScale;
            wall.transform.localScale = swapAxes
                ? new Vector3(s.x * fz, s.y * fy, s.z * fx)
                : new Vector3(s.x * fx, s.y * fy, s.z * fz);

            // 對位：包圍盒的水平中心對準格子中心，底部貼地（prefab 的 pivot 不一定在中心或底部）
            bounds = CalculateWorldBounds(wall);
            var offset = new Vector3(cellCenter.x - bounds.center.x, groundY - bounds.min.y, cellCenter.z - bounds.center.z);
            wall.transform.position += offset;
        }

        /// <summary>
        /// 外牆一定要擋得住人。有些裝飾用的建築 prefab 沒有碰撞體，
        /// 那樣的話它只是一張看得到、走得過去的皮 —— 補一個跟外觀一樣大的 BoxCollider。
        /// </summary>
        private static void EnsureSolid(GameObject wall)
        {
            if (wall.GetComponentInChildren<Collider>(true) != null) return;

            var renderers = wall.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            // 把每個 renderer 包圍盒的 8 個角轉到根物件的 local space，再包起來
            var t = wall.transform;
            bool has = false;
            var local = new Bounds();
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    var p = t.InverseTransformPoint(corner);
                    if (!has) { local = new Bounds(p, Vector3.zero); has = true; }
                    else local.Encapsulate(p);
                }
            }

            var box = wall.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
        }

        // ================================================================ 道路分級（幹道／支道）

        /// <summary>
        /// 1. 迷宮的走廊全部標成 Alley
        /// 2. 挑 arterialRows 條橫線 + arterialCols 條縱線，整條改成 Arterial
        /// 3. 清掉掛在幹道上、長度 ≤ 2 格的支道死路
        /// 幹道只會**加**路、第 3 步只剪葉子，所以連通性不會被破壞。
        /// </summary>
        private RoadType[,] ClassifyRoads(bool[,] roads, System.Random random,
                                          int mazeWidth, int mazeHeight, int spacing, int width, int height)
        {
            var types = new RoadType[width, height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    types[x, y] = roads[x, y] ? RoadType.Alley : RoadType.None;

            // 幹道一路延伸到外牆內側。停在最外面那條路的話，端點會變成
            // 「支道穿過、幹道從側面進來」的 T 字，選片會選反；延伸到牆，端點一定是死路。
            int t = Mathf.Min(BorderThicknessClamped(width, height), spacing);

            foreach (int k in PickArterialLines(mazeHeight, arterialRows, random, "橫向"))
                for (int x = t; x <= width - 1 - t; x++) types[x, k * spacing] = RoadType.Arterial;

            foreach (int k in PickArterialLines(mazeWidth, arterialCols, random, "縱向"))
                for (int y = t; y <= height - 1 - t; y++) types[k * spacing, y] = RoadType.Arterial;

            PruneStubs(types, width, height);
            return types;
        }

        /// <summary>
        /// 只從迷宮的「房間列」（奇數索引）挑 —— 沿著既有走廊走、不會從街區中間劈過去。
        /// 不挑最外面那兩列。同方向之間至少隔 arterialMinGap 個街區（索引差 2 × gap）。
        /// 洗幾次取挑得最多的那次：只洗一次的話，先挑到中間那列就可能把兩側都擋掉。
        /// </summary>
        private List<int> PickArterialLines(int mazeSize, int count, System.Random random, string label)
        {
            var result = new List<int>();
            if (count <= 0) return result;

            var candidates = new List<int>();
            for (int k = 3; k <= mazeSize - 4; k += 2) candidates.Add(k);
            int minDelta = 2 * Mathf.Max(1, arterialMinGap);

            for (int attempt = 0; attempt < 16 && result.Count < count; attempt++)
            {
                var order = new List<int>(candidates);
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }

                var picked = new List<int>();
                foreach (int k in order)
                {
                    if (picked.Count >= count) break;
                    bool ok = true;
                    foreach (int p in picked)
                        if (Mathf.Abs(k - p) < minDelta) { ok = false; break; }
                    if (ok) picked.Add(k);
                }
                if (picked.Count > result.Count) result = picked;
            }

            result.Sort();
            if (result.Count < count)
                Debug.LogWarning($"[地圖] {label}幹道只挑得出 {result.Count} / {count} 條（可選 {candidates.Count} 列、" +
                                 $"間距至少 {arterialMinGap} 個街區）。調大地圖或調小 arterialMinGap。", this);
            return result;
        }

        private static void PruneStubs(RoadType[,] types, int width, int height)
        {
            var remove = new List<Vector2Int>();
            var chain = new List<Vector2Int>(3);

            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (types[x, y] != RoadType.Alley || RoadDegree(types, x, y, width, height) != 1) continue;

                    chain.Clear();
                    var prev = new Vector2Int(-1, -1);
                    var cur = new Vector2Int(x, y);
                    while (true)
                    {
                        chain.Add(cur);
                        if (chain.Count > 2) break;
                        var next = NextAlong(types, cur, prev, width, height);
                        if (next == null) break;
                        prev = cur;
                        cur = next.Value;
                        if (types[cur.x, cur.y] == RoadType.Arterial) { remove.AddRange(chain); break; }
                        if (RoadDegree(types, cur.x, cur.y, width, height) != 2) break;
                    }
                }

            foreach (var c in remove) types[c.x, c.y] = RoadType.None;
        }

        private static int RoadDegree(RoadType[,] types, int x, int y, int width, int height)
        {
            int n = 0;
            foreach (var d in CardinalDirections)
            {
                int nx = x + d.x, ny = y + d.y;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height && types[nx, ny] != RoadType.None) n++;
            }
            return n;
        }

        private static Vector2Int? NextAlong(RoadType[,] types, Vector2Int cur, Vector2Int prev, int width, int height)
        {
            foreach (var d in CardinalDirections)
            {
                var n = cur + d;
                if (n == prev || n.x < 0 || n.y < 0 || n.x >= width || n.y >= height) continue;
                if (types[n.x, n.y] != RoadType.None) return n;
            }
            return null;
        }

        private static bool[,] ToRoadMask(RoadType[,] types, int width, int height)
        {
            var mask = new bool[width, height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    mask[x, y] = types[x, y] != RoadType.None;
            return mask;
        }

        /// <summary>幹道直直穿過、兩側都是支道的十字。alongX = 幹道東西向。幹道碰幹道回 false。</summary>
        private static bool IsArterialThroughAlleys(RoadType[,] types, int x, int y, int width, int height, out bool alongX)
        {
            alongX = false;
            RoadType At(int ax, int ay) =>
                ax >= 0 && ay >= 0 && ax < width && ay < height ? types[ax, ay] : RoadType.None;

            var n = At(x, y + 1); var s = At(x, y - 1);
            var e = At(x + 1, y); var w = At(x - 1, y);
            if (n == RoadType.None || s == RoadType.None || e == RoadType.None || w == RoadType.None) return false;

            bool ns = n == RoadType.Arterial && s == RoadType.Arterial;
            bool ew = e == RoadType.Arterial && w == RoadType.Arterial;
            if (ns == ew) return false;
            alongX = ew;
            return true;
        }

        /// <summary>每次生成印一行摘要：幹道／支道格數、兩組路面各填了幾片。</summary>
        private void LogSummary(RoadType[,] types, int width, int height)
        {
            int arterial = 0, alley = 0;
            foreach (var t in types)
            {
                if (t == RoadType.Arterial) arterial++;
                else if (t == RoadType.Alley) alley++;
            }

            string Set(RoadPrefabSet s)
            {
                if (s == null) return "0/7";
                int n = 0; string first = null;
                foreach (var p in new[] { s.horizontal, s.vertical, s.corner, s.tJunction, s.crossroad, s.deadEnd, s.isolated })
                    if (p != null) { n++; first ??= p.name; }
                return n == 0 ? "0/7" : $"{n}/7（{first}…）";
            }

            Debug.Log($"[地圖] 生成完成：網格 {width}×{height}（{width * tileSize:0} 公尺）、幹道 {arterial} 格、支道 {alley} 格。\n" +
                      $"路面：幹道組 {Set(arterialPrefabs)}、支道組 {Set(alleyPrefabs)}、" +
                      $"幹道穿過支道 {(arterialCrossAlleyPrefab != null ? arterialCrossAlleyPrefab.name : "未指定")}。", this);
        }

        // ================================================================ 地圖 B：生成時機、查詢、NavMesh

        /// <summary>
        /// 這一局還沒生成過就生成，生成過就跳過。
        ///
        /// **GameLauncher 在啟動 Runner 之前呼叫這一支。** 場景裡的 NPC 是 Fusion 的場景物件，
        /// Runner 一啟動就會 Spawned()；城市如果比牠們晚生成，牠們會在空中或建築裡開始走動。
        /// 先生成、先烤好 NavMesh，Runner 啟動時世界已經就緒，後面的落位就單純了。
        ///
        /// Start() 也會呼叫它（給沒有 GameLauncher 的場景），誰先跑都一樣，第二個會跳過。
        /// </summary>
        public void EnsureGenerated()
        {
            if (_generatedThisSession) return;
            GenerateMap();
        }

        /// <summary>這一局的廣場中心（世界座標）。生成之後才有值。</summary>
        public IReadOnlyList<Vector3> PlazaCenters => _plazaCenters;

        /// <summary>地圖的水平範圍（世界座標，y 高度為 0）。生成之後才有值。</summary>
        public Bounds MapBounds => _mapBounds;

        /// <summary>
        /// 地面的實際高度 = 這個物件的 y + groundY（跟 GridToWorld 一致）。
        /// 場景上 RandomMapBuilder 掛在 Tile 上，Tile 的 y 是 -0.1，不是 0。
        /// 取樣點要用這個高度，「落點不能比想去的點高太多」的判斷才準。
        /// </summary>
        public float FloorY => transform.position.y + groundY;

        /// <summary>某個世界座標離最近幹道多遠（公尺）。沒有幹道回 +∞。之後顧客系統會用。</summary>
        public float DistanceToNearestArterial(Vector3 worldPosition)
        {
            float best = float.PositiveInfinity;
            foreach (var p in _arterialCenters)
            {
                float dx = p.x - worldPosition.x, dz = p.z - worldPosition.z;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
            return float.IsPositiveInfinity(best) ? best : Mathf.Sqrt(best);
        }

        private void StoreArterialCenters(RoadType[,] types, int width, int height)
        {
            _arterialCenters.Clear();
            _roadCenters.Clear();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (types[x, y] == RoadType.None) continue;
                    var c = GridToWorld(x, y, width, height);
                    _roadCenters.Add(c);
                    if (types[x, y] == RoadType.Arterial) _arterialCenters.Add(c);
                }
        }

        /// <summary>
        /// 隨機一格道路上的點（落在 NavMesh 上）。
        ///
        /// **挑道路而不是隨便一個走得到的點**：道路保證彼此連通（迷宮本身就是連通的），
        /// 從路上出發、走到路上，路徑一定算得出來。隨便挑的話很容易挑到兩棟建築之間
        /// 的小空隙 —— 那塊跟街道不連通，從那裡出發到哪裡都走不到，NPC 就一直算不出路。
        ///
        /// maxDistance > 0 時只挑離 center 這麼近的道路（閒晃範圍）。
        /// </summary>
        public bool TryGetRoadPoint(Func<float, float, float> range, out Vector3 point,
                                    Vector3 center = default, float maxDistance = 0f, int attempts = 30)
        {
            point = default;
            if (_roadCenters.Count == 0) return false;

            float maxSqr = maxDistance * maxDistance;
            for (int i = 0; i < attempts; i++)
            {
                int idx = Mathf.Min(_roadCenters.Count - 1, Mathf.FloorToInt(range(0f, _roadCenters.Count)));
                var c = _roadCenters[idx];
                if (maxDistance > 0f)
                {
                    var d = c - center; d.y = 0f;
                    if (d.sqrMagnitude > maxSqr) continue;
                }
                if (NavUtil.SnapToNavMesh(c, tileSize * 0.5f, out point)) return true;
            }
            return false;
        }

        /// <summary>
        /// center 附近（距離 minDistance～maxDistance）一格道路上、而且周圍夠開闊的點。
        /// 大動物放在玩家出生點附近用。
        /// </summary>
        public bool TryGetOpenRoadPointNear(Vector3 center, float minDistance, float maxDistance,
                                            Func<float, float, float> range, out Vector3 point, int attempts = 60)
        {
            point = default;
            if (_roadCenters.Count == 0) return false;

            for (int i = 0; i < attempts; i++)
            {
                int idx = Mathf.Min(_roadCenters.Count - 1, Mathf.FloorToInt(range(0f, _roadCenters.Count)));
                var c = _roadCenters[idx];
                var d = c - center; d.y = 0f;
                float dist = d.magnitude;
                if (dist < minDistance || dist > maxDistance) continue;
                if (!NavUtil.SnapToNavMesh(c, tileSize * 0.5f, out var p)) continue;
                if (!NavUtil.IsOpenAround(p)) continue;
                point = p;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 幫沒有碰撞體的生成物補一個跟外觀一樣大的 BoxCollider。
        ///
        /// **Building Prefabs 裡有 9 種機庫完全沒有碰撞體**（Hangar_v2_1～7、outbuilding1、3）。
        /// 不補的話，玩家走得穿這些建築；而 NavMesh 不管從畫面網格還是碰撞體烤，
        /// 都會跟另一邊不一致 —— NPC 繞開、玩家穿過，或反過來顧客直接穿牆。
        /// 補上之後「看得到牆的地方就走不過去」對玩家與 NPC 都成立。
        ///
        /// 只處理根物件底下的**直接子物件**（每一棟建築、每一個小物件、每一片外牆），
        /// 地面、路面不動：地面本來就有碰撞體，路面刻意沒有（見 RoadPlaceholderBuilder）。
        /// </summary>
        private void EnsureCollidersOnGenerated()
        {
            if (generatedRoot == null) return;

            for (int i = 0; i < generatedRoot.childCount; i++)
            {
                var child = generatedRoot.GetChild(i).gameObject;
                if (child.activeSelf && IsObstacle(child.name)) EnsureSolid(child);
            }
        }

        private static bool IsObstacle(string name)
            => name.StartsWith("Building") || name.StartsWith("Small Object") || name.StartsWith("Border");

        /// <summary>
        /// 同步烤 NavMesh。**材料自己交給 NavMeshBuilder，不從場景物件收集。**
        ///
        /// 第一版用 NavMeshSurface 從 Generated Map 底下收集碰撞體來烤，結果地面幾乎沒有
        /// NavMesh（231 個三角形、道路抽樣 0/20）。地圖掛在一個被縮放成 (60, 0.2, 60) 的
        /// Tile 底下、又混著美術網格的碰撞體，收進去的東西與 agent type 對不對都很難驗證。
        ///
        /// 改成直接給兩種材料，結果只取決於「地圖多大、建築在哪」：
        ///   ・地面：整張地圖大小的一塊平面，頂面剛好在地面高度（可以走）
        ///   ・障礙：每棟建築、每個小物件、每段外牆，各一個跟外觀一樣大的方塊（Not Walkable）
        /// 障礙方塊壓在地面上，底下那塊地面就被挖掉，四周再依 agent 半徑往內縮。
        /// 屋頂不會有 NavMesh（Not Walkable 的頂面不算地面），不會再有浮在半空的小島。
        ///
        /// 烤跟查詢用同一個 agent 設定物件（NavUtil.Settings），不會對不上。
        /// </summary>
        private void BuildNavMesh()
        {
            if (generatedRoot == null) return;

            // 編輯模式按 Generate Map 只是看地圖長相，不烤（烤要建立執行期的 agent type）
            if (!Application.isPlaying) return;

            RemoveNavMesh();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var settings = NavUtil.Settings;

            int notWalkable = UnityEngine.AI.NavMesh.GetAreaFromName("Not Walkable");
            if (notWalkable < 0) notWalkable = 1;   // Unity 內建：0 Walkable、1 Not Walkable、2 Jump

            float floor = FloorY;
            var center = new Vector3(_mapBounds.center.x, floor, _mapBounds.center.z);
            var sources = new List<UnityEngine.AI.NavMeshBuildSource>();

            // 地面：頂面剛好在 floor
            sources.Add(new UnityEngine.AI.NavMeshBuildSource
            {
                shape = UnityEngine.AI.NavMeshBuildSourceShape.Box,
                size = new Vector3(_mapBounds.size.x, 0.2f, _mapBounds.size.z),
                transform = Matrix4x4.TRS(center + Vector3.down * 0.1f, Quaternion.identity, Vector3.one),
                area = 0,
            });

            // 障礙：用外觀的包圍盒（建築只轉 90° 的倍數，包圍盒就是它的佔地）
            int obstacles = 0;
            for (int i = 0; i < generatedRoot.childCount; i++)
            {
                var child = generatedRoot.GetChild(i).gameObject;
                if (!child.activeSelf || !IsObstacle(child.name)) continue;   // 已經被刪、還沒消失的不算

                var b = CalculateWorldBounds(child);
                if (b.size.x < 0.05f || b.size.z < 0.05f) continue;

                // 底部至少壓到地面以下一點，確保把底下的地面整塊蓋掉
                float bottom = Mathf.Min(b.min.y, floor - 0.2f);
                float top = Mathf.Max(b.max.y, floor + 0.5f);
                var size = new Vector3(b.size.x, top - bottom, b.size.z);
                var mid = new Vector3(b.center.x, (top + bottom) * 0.5f, b.center.z);

                sources.Add(new UnityEngine.AI.NavMeshBuildSource
                {
                    shape = UnityEngine.AI.NavMeshBuildSourceShape.Box,
                    size = size,
                    transform = Matrix4x4.TRS(mid, Quaternion.identity, Vector3.one),
                    area = notWalkable,
                });
                obstacles++;
            }

            var bakeBounds = new Bounds(center, new Vector3(_mapBounds.size.x + 10f, 80f, _mapBounds.size.z + 10f));
            var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, bakeBounds,
                                                                     Vector3.zero, Quaternion.identity);
            if (data != null) _navInstance = UnityEngine.AI.NavMesh.AddNavMeshData(data);
            sw.Stop();

            NavUtil.HasNavMesh = _navInstance.valid;

            // 診斷：抽 20 格道路看有幾格在 NavMesh 上。NPC 不動的時候先看這一行。
            var tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            int samples = Mathf.Min(20, _roadCenters.Count), hits = 0;
            for (int i = 0; i < samples; i++)
            {
                var c = _roadCenters[i * _roadCenters.Count / Mathf.Max(1, samples)];
                if (NavUtil.IsOnNavMesh(c, 1.5f)) hits++;
            }
            Debug.Log($"[地圖] NavMesh 烤好了（{sw.ElapsedMilliseconds} 毫秒，agent 半徑 {NavUtil.AgentRadius}，" +
                      $"障礙 {obstacles} 個）：{tri.indices.Length / 3} 個三角形，道路抽樣 {hits}/{samples} 格在 NavMesh 上。", this);
            if (samples > 0 && hits == 0)
                Debug.LogError("[地圖] 道路上完全沒有 NavMesh —— NPC 會找不到路。請把這一行貼給我。", this);
        }

        /// <summary>拿掉這張地圖的 NavMesh（重新生成、清除、物件被刪除時）。</summary>
        private void RemoveNavMesh()
        {
            if (_navInstance.valid) _navInstance.Remove();
            _navInstance = default;
            NavUtil.HasNavMesh = false;
        }

        private UnityEngine.AI.NavMeshDataInstance _navInstance;

        /// <summary>
        /// 地圖上隨機一個走得到的點（落在 NavMesh 上）。WoolNpc 散佈用。
        /// random 由呼叫端提供（狀態權威上跑，結果透過 Teleport / [Networked] 同步出去）。
        /// </summary>
        public bool TryGetRandomWalkablePoint(Func<float, float, float> range, out Vector3 point, int attempts = 40)
        {
            point = default;
            if (!NavUtil.HasNavMesh) return false;

            var b = _mapBounds;
            for (int i = 0; i < attempts; i++)
            {
                var wish = new Vector3(range(b.min.x, b.max.x), FloorY, range(b.min.z, b.max.z));
                if (NavUtil.SnapToNavMesh(wish, tileSize * 0.5f, out point)) return true;
            }
            return false;
        }

        /// <summary>
        /// 離 from 最遠、而且周圍夠開闊的走得到的點。大動物落位用。
        ///
        /// 「開闊」= 以 5 公尺為間距往 8 個方向取樣，至少 5 個方向在 NavMesh 上。
        /// 只挑最遠的話很容易挑到死巷底，大動物的方向取樣會全部被淘汰、原地不動。
        /// </summary>
        public bool TryGetFarOpenPoint(Vector3 from, Func<float, float, float> range, out Vector3 point, int samples = 150)
        {
            point = default;
            if (!NavUtil.HasNavMesh) return false;

            var b = _mapBounds;
            float bestDist = -1f;
            for (int i = 0; i < samples; i++)
            {
                var wish = new Vector3(range(b.min.x, b.max.x), FloorY, range(b.min.z, b.max.z));
                if (!NavUtil.SnapToNavMesh(wish, tileSize * 0.5f, out var p)) continue;
                if (!NavUtil.IsOpenAround(p)) continue;

                var d = p - from; d.y = 0f;
                float dist = d.sqrMagnitude;
                if (dist <= bestDist) continue;
                bestDist = dist;
                point = p;
            }
            return bestDist >= 0f;
        }

        private bool _generatedThisSession;
        private Bounds _mapBounds;
        private readonly List<Vector3> _plazaCenters = new();
        private readonly List<Vector3> _arterialCenters = new();
        private readonly List<Vector3> _roadCenters = new();

        // private variables ---

        private static readonly Vector2Int[] CardinalDirections =
        {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
        };

        [SerializeField, HideInInspector] private Transform generatedRoot;

        private sealed class BlockRegion
        {
            public readonly List<Vector2Int> cells = new List<Vector2Int>();
            public int minX = int.MaxValue;
            public int maxX = int.MinValue;
            public int minY = int.MaxValue;
            public int maxY = int.MinValue;

            /// <summary>這個區域碰不碰得到地圖邊界。碰得到的會被扣掉外牆那一圈。</summary>
            public bool touchesBoundary;

            // 查詢用的索引。邊界區域可能有上千格，而 Contains 會在放置迴圈裡被呼叫
            // 成千上萬次 —— List.Contains 是線性搜尋，會讓生成卡好幾秒。
            // 用「格數對不上就重建」的方式維護，不用改動 FindBlocks 填 cells 的寫法。
            private HashSet<Vector2Int> _index;

            public bool Contains(int x, int y)
            {
                if (_index == null || _index.Count != cells.Count) _index = new HashSet<Vector2Int>(cells);
                return _index.Contains(new Vector2Int(x, y));
            }

            /// <summary>移除符合條件的格子，並重算外框。</summary>
            public void RemoveWhere(Predicate<Vector2Int> match)
            {
                cells.RemoveAll(match);
                _index = null;

                minX = minY = int.MaxValue;
                maxX = maxY = int.MinValue;
                foreach (var c in cells)
                {
                    if (c.x < minX) minX = c.x;
                    if (c.x > maxX) maxX = c.x;
                    if (c.y < minY) minY = c.y;
                    if (c.y > maxY) maxY = c.y;
                }
            }
        }
    }
}
