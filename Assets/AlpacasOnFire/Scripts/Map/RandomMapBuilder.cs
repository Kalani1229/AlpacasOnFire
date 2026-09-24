using System;
using System.Collections.Generic;
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
            GenerateFloor(width, height);
            GenerateRoads(roads, width, height);

            var blocks = FindBlocks(roads, width, height);
            foreach (var block in blocks)
                GenerateBlockContents(block, random, width, height);

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

        private void GenerateFloor(int width, int height)
        {
            if (floorPrefab == null)
            {
                Debug.LogWarning("RandomMapBuilder needs a floorPrefab to create ground under roads and buildings.", this);
                return;
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                    InstantiateGenerated(floorPrefab, GridToWorld(x, y, width, height), Quaternion.identity, "Floor");
            }
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
                    if (prefab != null)
                        InstantiateGenerated(prefab, GridToWorld(x, y, width, height), rotation, "Road");
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

                    if (!touchesBoundary && region.cells.Count >= 1) blocks.Add(region);
                }
            }

            return blocks;
        }

        private void GenerateBlockContents(BlockRegion block, System.Random random, int width, int height)
        {
            int buildingCount = RandomRange(random, minBuildingsPerBlock, maxBuildingsPerBlock);
            int smallObjectCount = RandomRange(random, minSmallObjectsPerBlock, maxSmallObjectsPerBlock);
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

            public bool Contains(int x, int y)
            {
                return cells.Contains(new Vector2Int(x, y));
            }
        }
    }
}
