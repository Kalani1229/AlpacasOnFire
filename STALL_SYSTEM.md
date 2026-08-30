# 擺攤系統 — 施工結果

本批把「固定工坊 + 三分鐘關卡」改成「機台裝在手提箱裡，可以在任何地方架起攤位」。
顧客 NPC 是下一批的事，本批不做，但架構留得住（見文末）。

---

## 一、你要先做的兩件事（我沒辦法代勞）

### 1. 重建佔位資產

選單 `羊駝很忙 / 1. 建置佔位資產（材質 + Prefab + Catalog）`

這一步會新增手提箱、交貨窗口、輸送帶的 prefab，並且幫縫紉機／果汁機／人偶補上
`DeployHandle` 子物件。**沒有跑這步，手提箱裡會是空的。**

### 2. 重建 Fusion prefab 表

選單 `Tools > Fusion > Rebuild Prefab Table`

（或 `Tools > Fusion > Network Project Config` 開啟設定，Inspector 裡按 `Rebuild Prefab Table`，
再按下方的 `Apply`。兩個入口做的是同一件事。）

機台現在是**執行期用 `Runner.Spawn()` 生出來的**，沒有登錄在 prefab 表裡的話，
放置會失敗並在 Console 印出這件事。這是本批唯一一個新增的必要手動步驟。

**順序很重要**：你裝的 Fusion 2.0.12 是靠 AssetDatabase label 標記 prefab 的
（`Fusion.Unity.Editor.cs` 的 `RebuildPrefabTable()`），所以一定要**先**跑完上面的
「1. 建置佔位資產」讓新 prefab 存在，再 Rebuild —— 反過來的話會掃不到新東西。

### 3. 建置測試場景

選單 `羊駝很忙 / 擺攤 / 1. 建置擺攤測試場景`

驗證用：`羊駝很忙 / 擺攤 / 3. 檢查擺攤設置（診斷用）`
重跑用：`羊駝很忙 / 擺攤 / 2. 一鍵重置擺攤測試場景`

---

## 二、怎麼玩（一輪完整流程）

1. 出生點旁邊有一個手提箱，走過去按 **Space** 撿起來
2. 找一塊空地，面向前方按 **Space** → 襯布攤開成 8×8，箱子落在正中央
3. 對著箱子按 **Space** → 攤位選單
4. 選一台機台 → 幽靈模型跟著準心投影在襯布上
   - **滾輪** 旋轉 90 度
   - **Space** 放置
   - **Q** 取消
5. 對著已放好的機台按 **Space** → 拿起來重新擺放
6. 擺好之後回到箱子選單，房主按 **開張** → 開始計時 180 秒，切換成營業模式
7. 營業中 Space 恢復成正常操作機台；做好衣服拿到**交貨窗口**按 Space 出貨
8. 時間到跳出結算：本場營業額、成交筆數、錯過的訂單數，累加到資本額
9. 按繼續 → 回到探索狀態，攤位還在地上，可以繼續搬機台或從選單**收攤**換地方

開張的前提是襯布上至少有 **一台交貨窗口 + 一台縫紉機**，否則按鈕會是灰的並說明原因。

---

## 三、我設定的實際數值（全部在 `Core/GameTuning.cs`）

### 手提箱與襯布

| 項目 | 常數 | 值 |
|---|---|---|
| 襯布尺寸 | `StallMatSize` | **8 × 8 公尺** |
| 襯布近邊離玩家 | `StallMatDeployDistance` | 1.2 公尺 |
| 襯布貼地抬升 | `StallMatHeightOffset` | 0.02 公尺 |
| 四角檢測往內縮 | `StallDeployProbeInset` | 0.35 公尺 |
| 平坦度射線起點高 | `StallGroundProbeHeight` | 3.0 公尺 |
| 平坦度射線長度 | `StallGroundProbeLength` | 6.0 公尺 |
| **地面平坦度容許角** | `StallMaxGroundAngle` | **12 度** |
| 四角高低差容許 | `StallMaxGroundStep` | 0.45 公尺 |

### 機台放置

| 項目 | 常數 | 值 |
|---|---|---|
| **機台之間最小間距** | `StallMinDeviceSpacing` | **1.8 公尺**（中心距離） |
| 機台離襯布邊緣最小距離 | `StallDeviceEdgeMargin` | 0.7 公尺 |
| 準心投影最遠距離 | `StallPlaceMaxDistance` | 12 公尺 |
| 滾輪一格轉幾度 | `StallRotationStep` | 90 度 |
| **擺放／收回耗時** | `StallDeployDuration` | **0 秒（瞬間，先不做長按）** |
| 收攤清除範圍外擴 | `StallCollectRadius` | 0.6 公尺 |

### 輸送帶

| 項目 | 常數 | 值 |
|---|---|---|
| **輸送帶速度** | `ConveyorSpeed` | **1.6 m/s** |
| 帶面長度 | `ConveyorLength` | 3.0 公尺 |
| 帶面寬度 | `ConveyorWidth` | 0.9 公尺 |
| 帶面高度 | `ConveyorHeight` | 0.55 公尺 |
| 捕捉高度 | `ConveyorCaptureHeight` | 0.75 公尺 |

### 交貨窗口

| 項目 | 常數 | 值 |
|---|---|---|
| 檯面高度 | `DeliveryCounterHeight` | 1.3 公尺 |
| 排隊錨點離窗口 | `CustomerQueueDistance` | 1.6 公尺（下一批 NPC 用） |

### 計時與經濟

| 項目 | 常數 | 值 |
|---|---|---|
| **一場營業時長** | `StallDurationSeconds` | **180 秒** |
| 資本額起始值 | `StallStartingCapital` | 0 |

**既有數值一項都沒有改動。** 縫紉機 6 秒、果汁機 4 秒、噴槍容量 100、超時扣 40、
錯誤出貨扣 25、訂單 75 秒倒數等等全部維持原樣。

### 互動優先權（新的全域排序）

| 優先權 | 對象 |
|---|---|
| 0 | 地面雜物 |
| 1 | 機台（縫紉機／果汁機／人偶／交貨窗口） |
| 2 | 隊友 |
| **4** | **DeployableDevice（佈置模式拿起機台）** |
| **5** | **手提箱** |
| **99** | **PlacementTarget（放置預覽期間的 Space 捕捉器）** |

---

## 四、改動了哪些既有檔案

### 只有「新增」、沒有修改既有行為

| 檔案 | 改了什麼 |
|---|---|
| `Core/GameEnums.cs` | 新增 `ItemKind.Suitcase = 10`；`LevelElementType` 往後加 `DeliveryCounter=18 / Conveyor=19 / SuitcaseSpawn=20`；新增 `StallState`、`PlacementResult` 兩個列舉。既有項目的數值沒有動，存檔相容。 |
| `Core/GameTuning.cs` | 檔案末尾加一整個「擺攤系統」區塊。**既有數值一個字都沒改。** |
| `Core/GameAudio.cs` | `SfxId` 往後加 7 個事件 + 對應的合成音配方。 |
| `Orders/OrderBoard.cs` | 加兩個 public 方法 `ClearAllOrders()`、`ResetSpawnSchedule()`。既有邏輯沒動。 |
| `Editor/PlaceholderAssetBuilder.cs` | 建置流程加一行 `Step("Stall", ...)`。 |

### 有修改既有行為（都是為了讓擺攤模式能接上，且都有開關）

| 檔案 | 改了什麼 | 對舊場景的影響 |
|---|---|---|
| `Orders/LevelDirector.cs` | 加 `_stallMode` 開關與 `BeginStallRound()`。勾選時：`Spawned()` 不自動開始計時；時間到不做星級結算、不設 `Finished`。 | **無** —— 開關預設 `false`，舊場景行為完全不變。 |
| `UI/ResultsScreen.cs` | `Show()` 開頭加一個 early return：擺攤模式不顯示星級結算。 | **無** —— 只在 `StallMode == true` 時生效，星級結算的程式碼整段保留。 |
| `UI/PauseMenu.cs` | Esc 的判斷多兩行，讓擺攤面板優先關自己。 | **無** —— 只是多兩個 null-safe 判斷。 |

### 絕對沒有碰的檔案（你點名要保護的）

`GameLauncher.cs`、`NetInput.cs`、`SpawnPointRegistry.cs`、
`PlayerController.cs`、`PlayerCameraRig.cs`、`LocalInputProvider.cs`、
`PlayerCarry.cs`、`PlayerInteractor.cs`、`CarriableItem.cs`、
`IInteractable.cs`、`ItemInterfaces.cs`、
`SewingMachine.cs`、`Juicer.cs`、`Mannequin.cs`、`SprayGunTool.cs`、`ShearsTool.cs`、
`Mailbox.cs`、`BoxItem.cs`、`GarmentItem.cs`、`AccessoryItem.cs`、`MachineBase.cs`、
`DyeSourceNode.cs`、`BoxDispenser.cs`、`AccessoryDispenser.cs`、`RecyclingMachine.cs`、
`LevelDefinition.cs`、`LevelElementRecord.cs`、`LevelBuilder.cs`、`LevelEditorWindow.cs`、
`LevelSceneBuilder.cs`、`GameHud.cs`、`GameUIRoot.cs`、`PatternSelectPanel.cs`、`UIFactory.cs`

角色移動仍然走 `NetworkCharacterController`，本批沒有任何地方直接寫 `transform.position`
去搬玩家。

### 新增的檔案

```
Scripts/Stall/
  StallGeometry.cs      幾何與地面檢測（純函式，本機預覽與權威放置共用同一套）
  StallCatalog.cs       手提箱裡有什麼、怎麼解析成 prefab
  StallManager.cs       狀態機 / 襯布 / 放置驗證 / 開張 / 結算 / 資本額
  StallMatVisual.cs     襯布視覺（本機，非網路物件）
  SuitcaseItem.cs       手提箱（CarriableItem 子類）
  DeployableDevice.cs   讓既有機台可擺放／可收回
  PlayerStallAgent.cs   每個玩家的放置狀態 + 滾輪／Q
  PlacementTarget.cs    放置預覽期間的 Space 捕捉器
  PlacementGhost.cs     幽靈模型（本機視覺）
  DeliveryCounter.cs    交貨窗口
  Conveyor.cs           輸送帶
  StallUIRoot.cs        擺攤 UI 的 Canvas
  SuitcasePanel.cs      攤位選單（裝備 / 開張 / 收攤）
  StallHud.cs           模式標籤 / 資本額 / 提示
  StallResultsPanel.cs  本場結算

Editor/
  StallAssetBuilder.cs  手提箱／交貨窗口／輸送帶 prefab + DeployHandle
  StallSceneBuilder.cs  擺攤測試場景 + 一鍵重置 + 診斷
```

---

## 五、幾個設計判斷，先講清楚免得你之後看到覺得奇怪

### 1. 「Space 在兩種模式意義不同」是怎麼做到不改既有程式碼的

Space 是由既有的 `PlayerInteractor` 分派給準心前方的 `IInteractable` 的，
而它是照 `InteractionPriority` 挑最高的那個。所以我沒有加任何模式判斷，
而是讓新元件用優先權去搶：

- 佈置模式：`DeployableDevice`（4）壓過機台本體（1）→ Space = 拿起機台
- 營業模式：`DeployableDevice.CanInteract` 回 false → Space 落回機台本體 = 正常操作
- 放置預覽中：`PlacementTarget`（99）壓過所有東西 → Space = 放下

`DeployableDevice` 一定要掛在機台 prefab 的**子物件** `DeployHandle` 上，不能掛在根物件。
因為 `PlayerInteractor` 是用 `GetComponentInParent<IInteractable>()` 收集候選人，
同一個物件上有兩個 `IInteractable` 時只會拿到其中一個、順序還不保證。
分成兩個子物件、各自有碰撞體，兩個都會進候選清單，才輪得到優先權決定。

### 2. 手提箱拿在手上為什麼還選得到

`CarriableItem.UpdateColliders()` 只關掉「非 trigger」的碰撞體。
手提箱 prefab 上多掛了一顆 `HeldProbe` trigger 碰撞體，拿在手上時仍然有效，
而 `PlayerInteractor` 的查詢是 `QueryTriggerInteraction.Collide` —— 所以「拿著箱子按 Space 開箱」
不需要動 `PlayerCarry` 或新增按鍵。

### 3. 幽靈模型的位置為什麼一定跟實際放置的位置一致

`StallGeometry` 全部是純函式，輸入只有頭部位置、瞄準方向、襯布中心，
而這三個都是從 `[Networked]` 狀態推導的。本機預覽與狀態權威呼叫的是同一支函式，
所以看到綠色就一定放得下去。用戶端傳來的座標**完全不採信**，權威端一律重算。

### 4. 收攤會把襯布上的散裝物品一起收走

不只機台，襯布範圍內沒被拿在手上的物品（羊毛、半成品、染劑罐）也會一併消失。
這是刻意的：收攤 = 整攤打包。拿在手上的東西不受影響。

### 5. 星級結算沒有刪，只是關掉

`ResultsScreen` 整支保留，只在 `LevelDirector.StallMode == true` 時不觸發。
把 `[GameSystems]` 上 LevelDirector 的「擺攤模式」勾掉，星級結算立刻回來。

---

## 六、已知的粗糙處（不影響驗收，但你會遇到）

1. **襯布只能有一組。** v1 假設全隊共用一個手提箱。多個手提箱同時開箱的行為沒有定義
   （第二個會被 `MatDeployed` 擋掉，但不會有好的提示）。

2. **放置預覽中如果準心掃過素材點／飾品點，Space 只會放機台，不會誤觸** —— 這個有處理。
   但如果你在預覽中走到襯布外，`PlacementTarget` 仍然存在，只是驗證一律不通過並顯示原因。

3. **輸送帶不會把東西送進機台的輸入口。** 規格書寫明 v1 不做，我沿用。
   東西會被送到 `OutputAnchor` 然後掉在地上。

4. **結算畫面每個人各自關。** 任何一個人按繼續就會把整組狀態推回 Exploring，
   其他人的結算畫面會停在畫面上直到自己也按繼續。多人時要不要改成等所有人，留給下一批決定。

---

## 七、給下一批（顧客 NPC）留的接口

- `DeliveryCounter.CustomerQueueAnchor` —— 排隊位置，已經是空物件 + Gizmo
- `DeliveryCounter.WindowForward` —— 窗口朝外的方向
- `OrderBoard.TryDeliver(GarmentSpec)` —— 顧客成交時直接呼叫同一支，計分邏輯不用重寫
- `StallManager.State` —— NPC 只在 `Open` 時該出現
- `StallManager.RoundDeliveries` / `RoundMissed` —— 結算統計已經在累計，NPC 只要接上去
- `LevelElementType` 已經預留往後加的空間（下一個可用值是 21）
