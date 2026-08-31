# 擺攤系統 — 網格版（PlateUp! 式擺放）

手提箱、襯布、DeployableDevice、佈置／營業模式、交貨窗口、輸送帶、狀態機、
計時與結算、資本額 —— 整體結構都保留。這一版只換掉「擺放方式」。

---

## 一、你要先做的三件事

順序不能換：

1. 選單 `羊駝很忙 / 1. 建置佔位資產` —— 產生工具架等新 prefab
2. 選單 `Tools > Fusion > Rebuild Prefab Table`
3. 選單 `羊駝很忙 / 擺攤 / 1. 建置擺攤測試場景`

驗證：`羊駝很忙 / 擺攤 / 3. 檢查擺攤設置（診斷用）`
現在除了檢查 prefab 完整性，還會檢查**襯布放不放得下全部裝備**、
以及**預設佈局本身有沒有格子衝突**。

---

## 二、怎麼玩（改動後的流程）

1. 撿起手提箱 → 找空地按 **Space**
2. 襯布展開成 6×6 網格，**七台裝備一次全部彈出**到各自的格子上（有彈出動畫與音效）
   — 沒有選單，不用一台一台選
3. 面向任一裝備按 **Space** → 拿起來，變成跟著準心投影在網格上的幽靈
   - **滾輪** 轉 90°
   - **Space** 放進目前高亮的格子
   - **Q** 取消，裝備回到原本那一格
4. 擺好後走到襯布邊上的 **開張鈴**，按 Space 敲下去（房主限定）
   — 鈴鐺被敲扁一下然後縮起來消失，下一場要開張時會再出現
5. 營業 180 秒 → 結算 → 繼續
6. 對著手提箱按 **Space** → **收攤**（先把目前佈局寫回箱子，再收走所有裝備）
7. 換個地方重新開箱 → **裝備彈回你上次調好的位置**

---

## 三、我設定的實際數值

### 網格（本次新增／變更）

| 項目 | 常數 | 值 |
|---|---|---|
| **網格單格邊長** | `StallCellSize` | **1.5 公尺** |
| **網格格數** | `StallGridCells` | **6 × 6** |
| **襯布邊長** | `StallMatSize` | **9.0 公尺**（= 6 × 1.5，自動推算） |
| 網格線寬度 | `StallGridLineWidth` | 0.05 公尺 |
| 手提箱擺在襯布背緣外 | `StallSuitcaseBackOffset` | 0.9 公尺（不佔格子） |
| 開張鈴在背緣往右偏 | `StallBellSideOffset` | 1.8 公尺（不佔格子） |
| 開張鈴檯面高度 | `StallBellHeight` | 1.0 公尺 |
| 敲完縮起來的時間 | `StallBellShrinkDuration` | 0.45 秒 |

> `StallMatSize` 現在是 `StallGridCells * StallCellSize` 的常數運算式 ——
> 改格數或格子大小，襯布尺寸會自動跟著變，不會忘記同步。

### 開箱時的地面／淨空檢測（**只在開箱做一次**）

| 項目 | 常數 | 值 |
|---|---|---|
| 四角檢測往內縮 | `StallDeployProbeInset` | 0.35 公尺 |
| 射線起點高度 | `StallGroundProbeHeight` | 3.0 公尺 |
| 射線長度 | `StallGroundProbeLength` | 6.0 公尺 |
| **地面容許坡度** | `StallMaxGroundAngle` | **12 度** |
| **四角高低差容許** | `StallMaxGroundStep` | **0.45 公尺** |
| **上方淨空高度** | `StallClearanceHeight` | **2.2 公尺** |
| 淨空檢測往內縮 | `StallClearanceInset` | 0.25 公尺 |

### 裝備放置

| 項目 | 常數 | 值 |
|---|---|---|
| 準心投影最遠距離 | `StallPlaceMaxDistance` | 12 公尺 |
| 擺放／收回耗時 | `StallDeployDuration` | 0 秒（瞬間） |
| 收攤清除範圍外擴 | `StallCollectRadius` | 1.2 公尺 |

### 彈出動畫（純本機視覺，不同步）

| 項目 | 常數 | 值 |
|---|---|---|
| 單台彈出時間 | `StallPopDuration` | 0.35 秒 |
| 每台錯開 | `StallPopStagger` | 0.06 秒 |
| 拋起高度 | `StallPopHeight` | 0.9 公尺 |
| 縮放回彈幅度 | `StallPopOvershoot` | 1.12 |

> 七台裝備 × 0.06 秒錯開 + 0.35 秒動畫 ≈ **0.75 秒**跑完，不會拖節奏。

### 輸送帶（尺寸改小以放進一格）

| 項目 | 常數 | 值 | 變更 |
|---|---|---|---|
| 速度 | `ConveyorSpeed` | 1.6 m/s | 不變 |
| **帶面長度** | `ConveyorLength` | **1.35 公尺** | 從 3.0 縮短 |
| **帶面寬度** | `ConveyorWidth` | **0.85 公尺** | 從 0.9 微調 |
| 帶面高度 | `ConveyorHeight` | 0.55 公尺 | 不變 |
| 捕捉高度 | `ConveyorCaptureHeight` | 0.75 公尺 | 不變 |

> 原本 3.0 公尺的輸送帶塞不進 1.5 公尺的格子。之後把輸送帶的佔地改成 1×2，
> 就可以把長度拉回 2.8 左右 —— `StallCatalog.Footprint()` 改一個值就好。

### 沒有變動的數值

計時 180 秒、資本額起始 0、交貨窗口高度 1.3、排隊錨點距離 1.6、
以及 Phase 1 的全部數值（機台處理秒數、噴槍容量、扣款金額、攝影機參數）都沒有動。

### 移除的數值（改用格子後不再需要）

- `StallMinDeviceSpacing`（機台間距）→ 由格子佔用表取代
- `StallDeviceEdgeMargin`（離邊緣距離）→ 由格子界限取代
- `StallRotationStep`（旋轉角度）→ 由 `StallFacing` 四向列舉取代

### 預設佈局（第一次開箱用）

```
z=5              [交貨窗口]              ← 顧客站襯布外
z=4              [輸送帶↑]
z=3   [果汁機]   [縫紉機]
z=2   [噴槍架]   [人偶]
z=1   [剃毛器架]
      x=4        x=2/x=3
```

剃毛 → 縫紉機 → 輸送帶 → 交貨窗口是一條直線；
染色支線（果汁機 → 噴槍架 → 人偶）掛在旁邊不擋主線。

---

## 四、改動了哪些檔案

### 新增（4 支 runtime）

| 檔案 | 作用 |
|---|---|
| `Scripts/Stall/StallGrid.cs` | 格子座標換算、四向朝向、佔地旋轉、**格子佔用表**（36 格塞進一個 ulong 的位元表） |
| `Scripts/Stall/StallSlotRecord.cs` | 佈局的最小單位：裝備種類 + 格子座標 + 朝向，全整數的 `INetworkStruct` |
| `Scripts/Stall/ToolRack.cs` | 工具架 —— 讓剃毛器與噴槍也能佔一格、被記進佈局 |
| `Scripts/Stall/ServiceBell.cs` | 開張鈴 —— 敲了就開張，然後自己縮起來消失 |
（Editor 端沒有新檔案，只在既有的 `StallAssetBuilder.cs` 裡加了兩個建置函式）

### 大幅改寫（7 支）

| 檔案 | 改了什麼 |
|---|---|
| `Scripts/Stall/StallManager.cs` | 開箱改成 `PopOutAllDevices()` 一次生成全部；`ValidatePlacement(Vector3…)` → `ValidateCell(type, cx, cz, facing)`；新增 `BuildOccupancy()`；收攤先 `SaveLayoutTo(suitcase)`；地面檢測從放置移到開箱 |
| `Scripts/Stall/DeployableDevice.cs` | 位置改成 `[Networked] CellX / CellZ / FacingRaw`，世界座標由 `ApplyGridTransform()` 推算；加入彈出動畫；`MarkDeployed()` 簽章改成吃格子座標 |
| `Scripts/Stall/SuitcaseItem.cs` | **新增 `[Networked] NetworkArray<StallSlotRecord> Layout` 佈局記憶**；Space 從「開選單」改成「收攤」 |
| `Scripts/Stall/PlayerStallAgent.cs` | 待放置狀態從 `PendingYaw`（float）改成 `PendingFacing`（0–3 整數）+ 原格記錄；`TryResolveCell()` 回傳整數格 |
| `Scripts/Stall/PlacementGhost.cs` | 加上**目標格高亮**方塊；`Apply()` 改吃格中心 + 佔地格數 |
| `Scripts/Stall/StallMatVisual.cs` | 加上**網格線**（佈置模式顯示、營業模式隱藏） |
| `Scripts/Stall/StallHud.cs` | 移除所有可點按鈕，只留「還缺什麼才能開張」的文字提示 |

### 小幅修改（6 支）

| 檔案 | 改了什麼 |
|---|---|
| `Scripts/Core/GameEnums.cs` | 新增 `ToolRackShears=21`、`ToolRackSprayGun=22`、`ServiceBell=23`；`PlacementResult` 的 `Overlapping` 改名 `CellOccupied`、新增 `Obstructed`；新增 `StallFacing` 列舉 |
| `Scripts/Core/GameTuning.cs` | 網格與彈出動畫區塊；移除三個不再使用的常數；輸送帶尺寸縮小 |
| `Scripts/Core/GameAudio.cs` | 新增 `SfxId.StallPopOut`、`SfxId.BellRing` 與其合成音配方 |
| `Scripts/Stall/StallCatalog.cs` | `Devices` 改用工具架；新增 `Footprint()`、`DefaultLayout`、`MatFitsAllDevices()`、`FacingMeaning()` |
| `Scripts/Stall/StallGeometry.cs` | 新增 `CheckDeployArea()`（地面 + 淨空一起檢查）與 `CheckClearance()`；移除 `InsideMat()`；`CheckGround()` 多回傳 detail 字串供記錄 |
| `Scripts/Stall/StallUIRoot.cs` | 不再建立 `SuitcasePanel` |
| `Scripts/UI/PauseMenu.cs` | 移除對 `SuitcasePanel` 的 Esc 判斷 |
| `Editor/StallSceneBuilder.cs` | `ValidateStallSetup()` 改成檢查工具架、襯布容量、預設佈局衝突 |
| `Editor/StallAssetBuilder.cs` | 新增工具架與開張鈴的 prefab 建置 |

### 刪除（1 支 + 其 meta）

| 檔案 | 理由 |
|---|---|
| `Scripts/Stall/SuitcasePanel.cs` | 裝備選單整個不需要了。開張改成敲鈴、收攤改成對手提箱按 Space，沒有留任何死碼 |
| `Scripts/Stall/SuitcasePanel.cs.meta` | 一併刪除，避免 Unity 留下孤兒 meta |

### 修 bug 而動到的既有檔案（1 支）

| 檔案 | 改了什麼 |
|---|---|
| `Scripts/Player/PlayerInteractor.cs` | `Collect()` 加一行守衛，跳過還沒 `Spawned()` 或已 `Despawned` 的 `NetworkBehaviour` |

**為什麼一定要動它**：Fusion 的生成是延遲的 —— `Runner.Spawn()` 之後 GameObject 與碰撞體先存在，
`Spawned()` 要等到模擬迴圈才跑。在那個空窗期讀 `[Networked]` 屬性會直接丟
`InvalidOperationException`，而 `GameHud.Update()` 每一幀都呼叫
`FindTarget() -> CanInteract()`，一定會撞上。

這個洞在 Phase 1 一直存在，只是那時候機台是**場景物件**（載入場景時就 Spawn 完），
沒有空窗期所以從來沒發作。機台改成執行期生成之後才暴露出來 ——
錯誤堆疊裡同時有 `MachineBase.Processing`（既有程式碼）與 `ServiceBell.Rung`（新程式碼），
就是這個原因。

修在 `PlayerInteractor.Collect()` 一處，所有 `IInteractable` 實作都不必各自寫防呆，
以後新增的互動類型也自動受保護。若改成逐一在 `SewingMachine` / `Juicer` / `Mannequin` /
`DeliveryCounter` / `ToolRack` / `CarriableItem` … 裡加守衛，會散落十幾處而且新增類型時必漏。

同時也把三個 `Runner.TryFindObject()` 的結果補上 `IsValid` 檢查
（`StallManager.Bell`、`StallManager.ActiveSuitcase`、`ToolRack.ToolAlive`），
避免讀到「已 Despawn 但還沒清掉」的物件。

### 完全沒有碰的檔案

`GameLauncher.cs`、`NetInput.cs`、`SpawnPointRegistry.cs`、
`PlayerController.cs`、`PlayerCameraRig.cs`、`LocalInputProvider.cs`、
`PlayerCarry.cs`、`CarriableItem.cs`、
`IInteractable.cs`、`ItemInterfaces.cs`、
`SewingMachine.cs`、`Juicer.cs`、`Mannequin.cs`、`SprayGunTool.cs`、`ShearsTool.cs`、
`MachineBase.cs`、`Mailbox.cs`、`BoxItem.cs`、`GarmentItem.cs`、`AccessoryItem.cs`、
`DyeSourceNode.cs`、`BoxDispenser.cs`、`AccessoryDispenser.cs`、`RecyclingMachine.cs`、
`Conveyor.cs`、`DeliveryCounter.cs`、`PlacementTarget.cs`、`StallResultsPanel.cs`、`StallUIRoot.cs`、
`LevelDefinition.cs`、`LevelBuilder.cs`、`LevelEditorWindow.cs`、`LevelSceneBuilder.cs`、
`GameHud.cs`、`GameUIRoot.cs`、`PatternSelectPanel.cs`、`UIFactory.cs`、
`LevelDirector.cs`、`OrderBoard.cs`、`ResultsScreen.cs`

角色移動仍然走 `NetworkCharacterController`，本批沒有任何地方直接寫 `transform.position` 搬玩家。

---

## 五、幾個關鍵設計判斷

### 1. 為什麼多了「工具架」

剃毛器與噴槍是手持工具，本來沒辦法「站在格子上」。但網格佈局要成立，
每一台裝備都必須佔一格、能被記進佈局、能用同一套方式重擺。
所以給它們一個架子：**架子是 DeployableDevice**（進網格、能重擺、會被記住），
**架上的工具仍是一般 CarriableItem**（照舊可以撿起、丟出、接住）。

工具掉進地形縫隙整攤就廢了，所以架子留了一個保底：
空手對著空架子按 Space 可以再拿一支。

### 2. 位置為什麼一定兩端一致

裝備身上**沒有 NetworkTransform**。同步的只有 `CellX / CellZ / FacingRaw` 三個整數，
世界座標由 `StallGrid.CellToWorld()` 在每一端各自算。整數不會有浮點誤差，
所以「兩端算出來的位置一定是同一個點」是結構上保證的，不是靠精度湊出來的。

彈出動畫是純本機視覺（只改 `transform` 的暫時偏移與縮放），不佔任何頻寬。

### 3. 重疊判定為什麼不用碰撞體

碰撞體判定會受模型外框影響，而且兩端可能算出不同結果。
改成 `StallGrid.Occupancy` —— 6×6 = 36 格剛好塞進一個 `ulong` 的位元表，
`Check()` 是純位元運算，完全確定性，本機預覽與權威端呼叫的是同一支。

### 4. 朝向是四向列舉，不是角度

`StallFacing`：0=+Z(前)、1=+X(右)、2=−Z(後)、3=−X(左)。
朝向有實際功能：**+Z 是正面** ——
輸送帶往正面送、交貨窗口的窗口朝正面（顧客站那邊）、機台的操作面在背面。
幽靈模型上那根白色的「鼻子」指的就是正面。

### 5. 開張與收攤搬家了

選單刪掉之後這兩個動作需要新家：
- **開張** → 走到襯布邊上敲**開張鈴**（世界物件，Space 互動，房主限定）
- **收攤** → 對著手提箱按 Space（跟開箱對稱）

**為什麼不是 HUD 按鈕**：這個專案的游標從頭到尾是鎖住的，而且
`LocalInputProvider` 把 `LookEnabled` 直接綁在游標鎖上 ——
解鎖游標的同時滑鼠視角與 WASD 會一起停掉。所以常駐的 HUD 按鈕在這套控制模型下
根本點不到，可點的 UI 一律得是會解鎖游標的模態面板。做成世界物件就完全繞開這件事。

**鈴鐺的生命週期**由 `StallManager.EnsureBell()` 每個 tick 維護：
進佈置模式就生一顆、敲掉或收攤就沒了、一場營業結束回到佈置模式時再生一顆。
它**沒有 DeployHandle** —— 是控制不是裝備，不進網格、不佔格子、搬不走。

### 6. 診斷記錄分兩邊

`BuildReport` 在 `Assets/AlpacasOnFire/Editor/` 底下，屬於 Editor 組件，
**runtime 程式碼參照不到它**。所以：
- Editor 端（資產建置、場景建置、設置檢查）→ 用 `BuildReport`，會寫進 `AlpacasOnFire_BuildReport.txt`
- Runtime 端（開箱地面檢查結果、佈局讀寫、放置驗證失敗原因）→ 用 `Debug.Log`，進 Console

兩邊都有記錄，只是落點不同。

---

## 六、已知的粗糙處

1. **襯布只能有一組。** 多個手提箱同時開箱沒有定義（第二個會被 `MatDeployed` 擋掉）。

2. **佈局記在「那一個手提箱」身上。** 換一個手提箱就是一份新的空佈局。
   目前場上只有一個箱子，所以不成問題。

3. **裝備間距固定 1.5 公尺，機台本體 1.2 公尺寬**，相鄰兩格之間只剩 0.3 公尺，
   玩家（半徑 0.35）鑽不過去。這是刻意的 PlateUp 感，但如果你覺得太擠，
   把 `StallCellSize` 調到 1.8 就會鬆很多。

4. **輸送帶變短了**（1.35 公尺），視覺上比較像一小段滑道而不是輸送帶。
   要改回長的就把佔地改成 1×2。

5. **結算畫面每個人各自關。** 任一人按繼續就會把狀態推回佈置模式。
