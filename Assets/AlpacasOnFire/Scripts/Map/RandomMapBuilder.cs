using System;
using System.Collections.Generic;
using AlpacasOnFire.Core;
using UnityEngine;

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
        public RoadPrefabSet roadPrefabs;
        [Min(0f)] public float groundY;

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
            bool[,] roads = ExpandRoadMap(mazeRoads, spacing, width, height);

            // 街區與廣場要在鋪地面之前算出來 —— 廣場的地面跟一般地面不同。
            // （FindBlocks 不用亂數，所以提前呼叫不會改變亂數序列；
            //   廣場挑選插在街區內容之前，同一個 seed 仍然每次都得到同一張圖。）
            var blocks = FindBlocks(roads, width, height);
            blocks = TrimBorderRing(blocks, width, height);
            var plazaCells = PickPlazas(blocks, roads, random, width, height);

            GenerateFloor(width, height, plazaCells);
            GenerateRoads(roads, width, height);

            foreach (var block in blocks)
                GenerateBlockContents(block, random, width, height);

            GenerateBorder(random, width, height);

            if (blocks.Count == 0)
                Debug.LogWarning("RandomMapBuilder found no enclosed blocks. Increase map size or cycle chance.", this);
        }

        [ContextMenu("Clear Generated Map")]
        public void ClearGeneratedMap()
        {
            if (generatedRoot == null) return;

            if (Application.isPlaying) Destroy(generatedRoot.gameObject);
            else DestroyImmediate(generatedRoot.gameObject);

            generatedRoot = null;
        }

        // life cycle ---

        private void Start()
        {
            if (Application.isPlaying && generateOnStart) GenerateMap();
        }

        private void OnDestroy()
        {
            if (!Application.isPlaying) ClearGeneratedMap();
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

        private void GenerateRoads(bool[,] roads, int width, int height)
        {
            if (roadPrefabs == null) return;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!roads[x, y]) continue;

                    int mask = GetRoadMask(roads, x, y, width, height);
                    var prefab = GetRoadPrefab(mask, out Quaternion rotation);
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

        private bool IsInsideBlock(Bounds bounds, BlockRegion block, int width, int height)
        {
            int minX = WorldToGridX(bounds.min.x, width);
            int maxX = WorldToGridX(bounds.max.x, width);
            int minY = WorldToGridZ(bounds.min.z, height);
            int maxY = WorldToGridZ(bounds.max.z, height);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (!block.Contains(x, y)) return false;
                }
            }

            return true;
        }

        private GameObject GetRoadPrefab(int mask, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            int count = CountBits(mask);

            if (count == 4) return roadPrefabs.crossroad;
            if (count == 3)
            {
                rotation = RotationForMask(mask, 0b0111);
                return roadPrefabs.tJunction;
            }

            if (count == 2)
            {
                if (mask == 0b0101) return roadPrefabs.vertical;
                if (mask == 0b1010) return roadPrefabs.horizontal;
                rotation = RotationForMask(mask, 0b1001);
                return roadPrefabs.corner;
            }

            if (count == 1)
            {
                rotation = RotationForMask(mask, 0b0001);
                return roadPrefabs.deadEnd;
            }

            return roadPrefabs.isolated;
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
            if (Application.isPlaying) Destroy(instance);
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
                        wall.transform.SetParent(generatedRoot, true);
                        float side = tileSize * borderOverlap;
                        wall.transform.localScale = new Vector3(side, borderMinHeight, side);
                        wall.transform.position = cellCenter + Vector3.up * (borderMinHeight * 0.5f);
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
