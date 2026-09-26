# 施工指令：地圖 A —— 廣場、外圍牆、路面

> 貼給新對話即可。三件事都改在 `RandomMapBuilder` 的生成流程裡。
> NavMesh 是下一批，這一批不要碰。

---

## 0. 前置

先 `git pull`（`origin/main` 比本機多 12 個 commit，是乾淨的 fast-forward）。
這一批要動的 `Assets/AlpacasOnFire/Scripts/Map/RandomMapBuilder.cs` 在那些 commit 裡。

---

## 0.1 紅線

- 不要改 `BuildMaze` / `ExpandRoadMap` / `GetRoadMask` / `GetRoadPrefab` / `RotationForMask`。
  **連通性與選片的邏輯是對的**，這一批只在它後面加 pass
- 所有隨機都要用**傳進來的那個 `System.Random random`**，不要用 `UnityEngine.Random`。
  之後要同步種子時才不用重寫
- 不要動擺攤系統、不要動 NPC、不要動 `GameTuning` 既有數值

---

## 1. 廣場街區（最重要，不做就玩不了）

### 問題

襯布是 **12×12 公尺**（`StallGridCells 8 × StallCellSize 1.5`），
而現在街道 1 格寬 = 4 公尺、街區 4×4 格 = 16×16 公尺、裡面還塞了 1–3 棟建築和 2–8 個小物件。
**整張地圖可能一個攤位都擺不出來。**

### 做法

在 `GenerateMap()` 裡 `FindBlocks` 之後、`GenerateBlockContents` 之前插一段：
挑出幾個街區**完全淨空**當廣場，這些街區跳過 `GenerateBlockContents`。

```csharp
[Header("Plazas")]
[Tooltip("保留幾個完全淨空的街區當擺攤廣場。")]
[Min(1)] public int plazaCount = 4;
[Tooltip("廣場地面用的 prefab。留空就沿用一般地面。")]
public GameObject plazaFloorPrefab;
```

挑選規則：

- 只從**放得下襯布**的街區裡挑 —— 街區的 bounding box 兩軸都要
  ≥ `GameTuning.StallMatSize + 4f`（12 + 4 = 16 公尺，剛好是一個標準街區）
- 用 `random` 洗牌後取前 `plazaCount` 個
- **廣場之間要分散**：挑的時候跳過離已選廣場太近的（例如中心距離 < 地圖邊長的 1/5），
  不然四個廣場擠在一起，玩家還是只有一個選擇
- 挑不滿就挑幾個算幾個，並 `Debug.LogWarning` 說明原因（街區太小 / 數量不夠）

**不需要另外登記擺攤點。** 擺攤的檢測本來就是幾何的（平坦度 + 淨空 + 不重疊），
一塊真正空的地自己就會通過。

### 廣場要看得出來

這點不要省 —— 否則「哪裡擺得出攤位」會變成玩家試錯。
廣場的地面換一個明顯不同的材質或顏色（`plazaFloorPrefab`，留空就退回一般地面但換色）。
玩家要能從街上一眼看到「那裡是空的」。

---

## 2. 外圍建築牆

現在 `FindBlocks` 會跳過碰到邊界的街區（`touchesBoundary`），
所以地圖最外圈是一圈空地板，玩家可以一路走到地板邊緣然後掉下去。

### 做法

`GenerateMap()` 最後加一個 pass，沿網格外圈放建築：

```csharp
[Header("Border")]
[Tooltip("外圍用來擋住玩家的建築。留空就用 buildingPrefabs。")]
public GameObject[] borderPrefabs;
[Min(1)] public int borderThickness = 1;   // 外圍幾格厚
```

- 沿著 `x == 0`、`x == width-1`、`y == 0`、`y == height-1`（往內 `borderThickness` 格）
  逐格放建築，隨機挑 prefab、隨機轉 90° 的倍數
- **不要擋住道路的出口**：如果那一格在 `roads` 陣列裡是 true，
  還是要放（迷宮的邊界本來就該封死），否則會留一個走得出去的缺口
- 建築之間刻意重疊一點，不要留縫

外牆的高度要**明顯高於羊駝**（1.8 公尺），至少 4–5 公尺，讓它讀起來是牆不是障礙物。

---

## 3. 路面 prefab（純資產，邏輯不用動）

`RoadPrefabSet` 的七個欄位是空的，所以 `GenerateRoads` 裡
`if (prefab != null)` 全部跳過 —— 地上什麼都沒有。連通、轉角、T 字、十字的判斷**都已經寫好了**。

### 3.1 產七個佔位路面

在 `PlaceholderAssetBuilder` 末端追加（既有段落一行都不要動），
產出 `Road_Horizontal` / `Road_Vertical` / `Road_Corner` / `Road_TJunction` /
`Road_Crossroad` / `Road_DeadEnd` / `Road_Isolated`。

**每一片都必須可拼接**，這是最重要的約束：

- 尺寸剛好 `tileSize`（目前 4 公尺），路面邊緣切在 ±tileSize/2
- **七片的路面寬度、路緣高度完全一致**，不然轉角接直線會有錯位
- pivot 在磚塊正中央
- 厚度很薄（0.05）、比一般地面**略高一點點**（y = 0.03）避免 Z-fighting
- 顏色上用深灰路面 + 淺色標線，七種的標線不同（直線一條中線、十字四向、死路一個封口），
  這樣一眼看得出選片邏輯有沒有出錯

`tileSize` 是 inspector 上的值，所以佔位 prefab 要用一個常數對齊 ——
在 `PlaceholderAssetBuilder` 裡寫死 4 公尺，並在註解說明「改 `tileSize` 要同步改這裡」。

### 3.2 自動指派

美術還沒交之前要能看到路，所以建置完要把七個 prefab 塞進場景裡
`RandomMapBuilder` 的 `roadPrefabs` 欄位。

沿用專案既有的 `SerializedObject` 寫法（`VillageSceneBuilder` 裡的 `SetBool` / `SetVector3` 那種），
新增一個選單項：

```
羊駝很忙/v6 羊駝村/4. 指派佔位路面到場景的 RandomMapBuilder
```

找不到 `RandomMapBuilder` 就 `Debug.LogError` 說明，不要靜默失敗。

---

## 4. 建議的 inspector 值（不是程式改動）

跑起來之後試試這組，看空間感對不對：

- `tileSize` 4 → **6**（路 6 公尺才讀得像街道，4 公尺像巷子）
- `mapWidth` / `mapHeight` 25 → **11**

這組會得到：地圖約 306 公尺（走到底約 60 秒）、街區 24×24、廣場放得下襯布還有餘裕。

**這些是 inspector 上的數字，不要寫死進程式。**

---

## 5. 驗收

1. 重新生成地圖，地上**看得到路**，而且轉角、T 字、十字都接得起來沒有錯位
2. 沿著路一直走，走得到地圖的每一個角落（連通性沒被破壞）
3. 場上有 `plazaCount` 個明顯空曠、地面顏色不同的廣場
4. **每個廣場都開得出手提箱**（這是這一批的核心驗收）
5. 廣場彼此分散，不是擠在同一角
6. 走到地圖邊緣會被建築擋住，**走不出去、也掉不下去**
7. 外圍沒有可以穿出去的缺口（特別是迷宮道路撞到邊界的地方）
8. 同一個 `seed` 重新生成兩次，結果**完全一樣**（沒有混用 `UnityEngine.Random`）
9. 把 `tileSize` 改成 6、`mapWidth` 改成 11 重新生成，一切仍然正常
10. `Stall_Test` 沒有被影響

---

## 6. 這一批不要做的

- **不要做 NavMesh**（下一批，而且它要等地形定案）
- 不要動 `useRandomSeed`（多人的事之後再處理）
- 不要做道路層級（主幹道 vs 巷子）—— 那要換一套生成方式，先感覺看看均質的城市

---

## 7. 開工前

有不確定的先問我。特別是第 1 節的廣場挑選 ——
如果 `FindBlocks` 回傳的區塊形狀不規則（`cycleChance` 可能讓幾個街區連成一塊），
「放不放得下 12×12」的判斷要用實際形狀而不是 bounding box，那要先跟我確認怎麼處理。
