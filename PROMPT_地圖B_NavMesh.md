# 施工指令：地圖 B —— NavMesh 與生成點

> 貼給新對話即可。前提：地圖 A（廣場／外圍牆／路面）與 A2（幹道／支道）都已完成。

---

## 0. 已經查過的前提（不用再問我）

- **`com.unity.ai.navigation` 2.0.9 已安裝** —— 可以直接用 `NavMeshSurface`
- **NPC 與大動物是編輯期就放在場景裡的**（`VillageSceneBuilder` 用
  `PrefabUtility.InstantiatePrefab` 放在寫死的座標），而城市是**執行期**生成
  → 所以第 2 節的重新落位是**必做**，不是備案
- **廣場清單目前沒有被暴露出來**（A2 的 prompt 有要求但沒實作），這一批要補
- 路障／死路那一批**還沒做**。只要 bake 保持在 `GenerateMap()` 的最後一步，
  之後加進來會自動被烤進去，這一批不用為它預留什麼

---

## 1. 紅線

**一、不要用 `NavMeshAgent`。**

它會直接寫 `transform`，跟 Fusion 的重模擬打架 —— 這正是這個專案在裸
`CharacterController` 和 `NetworkTransform` 上已經踩過**兩次**的坑（症狀是幾秒後
開始抖動並瞬間大步移動）。

**NavMesh 只拿來做路徑查詢**（`NavMesh.CalculatePath` / `NavMesh.SamplePosition`），
路徑點餵進現有的 `NetworkCharacterController`。
`WoolNpc` / `WildBeast` / `Customer` 的移動程式一行都不用換，
只是「目標點」的來源變了。

**二、路徑只在 `HasStateAuthority` 且 `Runner.IsForward` 的 tick 算。**
重模擬期間重算既浪費又可能發散。路徑存在**普通 C# 欄位**，不要加 `[Networked]` ——
位置本來就由 NCC 同步，路徑是過程不是狀態。

**三、不要改地圖生成的邏輯。** A 與 A2 定案了，這一批只在後面加。

---

## 2. 執行順序（這一節最容易出事，先讀）

目前 `RandomMapBuilder.generateOnStart = true`，在 `Start()` 生成城市。
而場景裡的 NPC 是 **Fusion 的 scene object**，`NetworkRunner` 啟動時就會被 spawn。

**兩者的順序沒有保證。** 如果 NPC 先活起來、城市後生成，牠們會在空中或建築裡開始走動。

### 要改成這個順序

```
1. GameLauncher.Start()
     → RandomMapBuilder.GenerateMap()        （同步，含外圍牆、廣場）
     → NavMeshSurface.BuildNavMesh()         （同步，最後一步）
2. 然後才 await StartGame(mode)              （啟動 Runner）
3. NPC / 大動物 / 玩家的 Spawned() 裡重新落位
```

把地圖生成從 `RandomMapBuilder.Start()` 移到 `GameLauncher` 啟動 Runner **之前**，
並把 `generateOnStart` 改成 false（或留著但加一個「已經生成過就跳過」的旗標）。

這樣 Runner 一啟動，世界已經有地形也有 NavMesh，後面全部單純。

---

## 3. 重新落位（比 NavMesh 本身更急）

編輯期寫死的座標在程序生成的城市裡全部失效：
`SpawnNpcs()` 的 10 隻羊、`SpawnBeasts()` 的大動物、`PlayerSpawn`、`_suitcaseSpawnPosition`。

### 3.1 先補廣場查詢 API

`RandomMapBuilder` 上加：

```csharp
/// <summary>這一局的廣場中心（世界座標）。生成之後才有值。</summary>
public IReadOnlyList<Vector3> PlazaCenters { get; }

/// <summary>某個世界座標離最近幹道多遠（公尺）。之後顧客系統會用。</summary>
public float DistanceToNearestArterial(Vector3 worldPosition);
```

第二支這一批不會用到，但 A2 已經答應要留，順手補上比之後重算便宜。

### 3.2 落位規則

```csharp
static bool SnapToNavMesh(Vector3 wish, float radius, out Vector3 result)
```
內部用 `NavMesh.SamplePosition(wish, out hit, radius, NavMesh.AllAreas)`。

- **玩家出生點**：落在 `PlazaCenters[0]` 附近（四個玩家散開一點）
- **手提箱**：同一個廣場
- **WoolNpc**：散佈在道路與街區上的合法點，`HomePoint` 也要跟著更新
  （`Configure(color, home)` 已經存在，直接用）
- **大動物**：找一個離玩家出生廣場**最遠**的合法點。
  但不是隨便一個合法點 —— **牠的領域內要有足夠的開闊可行走區域**，
  否則方向取樣會一直全部淘汰，牠會原地不動。
  做法：候選點周圍以 5 公尺為間距取樣 8 個方向，至少 5 個方向要在 NavMesh 上才採用

### 3.3 移動的方式：一定要用 `Teleport()`

**不要寫 `transform.position = ...`。**

`NetworkCharacterController` 會快取自己的位置，直接改 transform 它看不見 ——
這個坑跟當初 client 端抖動是同一個成因。所有落位都走
`NetworkCharacterController.Teleport(position)`，而且只在 `HasStateAuthority` 上做。

---

## 4. NavMesh 的建置

在 `GenerateMap()` 的**最後一步**：

- 在 `Generated Map` 根物件上掛 `NavMeshSurface`
- `collectObjects = Children`（只收生成出來的東西）
- `BuildNavMesh()`

**Agent 半徑用 0.7**（羊駝半徑 0.35，大動物是兩倍）。只烤一份 ——
大動物過得去的地方小 NPC 一定過得去。兩種 agent type 要烤兩次，這一批不值得。

bake 是同步的，306 公尺的地圖可能要數百毫秒。放在 Runner 啟動前，玩家還在載入時就做完。

---

## 5. 共用的路徑追蹤

新增 `Scripts/Npc/NavPathFollower.cs`（純 C# class，不是 NetworkBehaviour）：

```csharp
public class NavPathFollower
{
    public bool HasPath { get; }
    public Vector3 CurrentWaypoint { get; }

    /// <summary>重算路徑。只在 StateAuthority + IsForward 呼叫。</summary>
    public bool Recalculate(Vector3 from, Vector3 to);

    /// <summary>這一步該往哪走的單位向量。走完回傳 Vector3.zero。</summary>
    public Vector3 Steer(Vector3 currentPosition, float arriveRadius);

    public void Clear();
}
```

- 內部用 `NavMeshPath` + `NavMesh.CalculatePath`
- **不要每個 tick 重算。** 目標沒變就沿用；變了、或每 0.5 秒才重算
- 算失敗（`PathPartial` / `PathInvalid`）回傳 false，呼叫端退回原本的直線行為，
  不要讓 NPC 整個停住

---

## 6. 三隻各自怎麼改

### 6.1 `Customer`（收益最大，先做這隻）

顧客要從城市各處走到 `CustomerQueueAnchor`。現在是直線移動 ——
在城市裡會直接撞牆，整個顧客系統會癱瘓。

- `Recruit()` 時算一次路徑到 `QueueSpot`
- 每個 tick 用 `Steer()` 取方向餵進 NCC
- 路徑算不出來（被建築圍死）→ **放棄這位顧客**，`CustomerQueue` 換一隻。
  不要讓他卡在牆邊耗完耐心然後扣你錢

### 6.2 `WoolNpc`

`EnterWander()` 挑完隨機目標點之後先 `SamplePosition` 移到合法點，
再用 `NavPathFollower` 走過去；`TickWander()` 的移動方向改成 `Steer()`。

**`Flee` 狀態維持現況**（背對玩家直線跑）—— 逃跑就該慌不擇路，
撞牆再轉向反而自然。只要確保不會卡死。

### 6.3 `WildBeast`（改最少）

**不要改成路徑追蹤。** 牠的靈魂是「往離最近玩家最遠的方向跑」的方向取樣，
換成 A* 會變成另一種東西，批 1 的手感會消失。

只加一條：方向取樣評分時，除了現有的領域檢查與 `BeastObstacleProbe` 射線，
再要求**預測點必須在 NavMesh 上**（`SamplePosition`，容差 1 公尺），不在就淘汰。

這樣牠不會往建築裡鑽，但「被逼到角落原地打轉」那個核心瞬間完全保留。

---

## 7. 驗收

**順序與落位**

1. 進場景時城市已經生成、NavMesh 已經烤好，NPC 才開始動
2. 玩家出生在廣場上，不在建築物裡、不在空中
3. 手提箱在同一個廣場、拿得到
4. 十隻 `WoolNpc` 散在街上與街區，沒有卡在牆裡或浮空
5. 大動物在離出生廣場很遠的地方，而且**牠的領域內走得通**（不會原地不動）
6. 落位全部用 `Teleport()` —— client 端看不到瞬移抖動

**路徑**

7. 顧客**沿著街道**走到攤位，不撞牆、不卡在建築角落
8. 攤位擺在不同廣場，顧客都找得到路
9. 顧客被圍死時會被放棄並換一隻，不會卡著耗完耐心扣錢
10. `WoolNpc` 閒晃會繞過建築，不會貼牆磨
11. 大動物不往建築裡跑，但**兩個人還是夾得到**（批 1 手感沒壞）

**不能壞掉的**

12. 沒有任何 `NavMeshAgent`
13. 沒有新增 `[Networked]` 欄位
14. 單人跑一整輪：探索 → 擺攤 → 開張 → 交貨 → 結算，全程沒有 NPC 卡住
15. 換一個 `seed` 重新生成，以上全部仍然成立
16. `Stall_Test` 沒有被影響

---

## 8. 這一批不要做的

- 不做兩種 agent type
- 不做動態障礙（`NavMeshObstacle`）—— 攤位擺在廣場上會擋路，但大家繞得過去
- 不做顧客的幹道偏好（下一批，但 3.1 的 API 要留好）
- 不碰多人的 bake 一致性與種子同步
- 不做路障／死路（另一批）

---

## 9. 開工前

第 2 節的執行順序改動會動到 `GameLauncher` 的啟動流程 ——
如果你覺得那裡有別的風險，先問我再動。
