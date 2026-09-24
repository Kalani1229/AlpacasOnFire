# 施工指令：v6「羊駝村」測試場景

> 把底下整份貼給下一輪對話即可。
> 對應設計文件：`GAME_DESIGN.md`（v6，2026/09/04）第六節「最小可測範圍」。

---

## 0. 這一批的紅線

**不准動到現有的擺攤測試場景。** `Stall_Test` 是目前唯一能跑通的東西，
它是這一批做壞掉時的對照組與退路。具體來說：

- **不要刪除** `Juicer.cs`、`DyeCanisterTool.cs`、`Mannequin.cs`、
  `GarmentPaintSurface.cs`、`ToolRack.cs`、`DyeSourceNode.cs`。
  設計文件寫「移除」是指**未來的正式版**；這一批一律只做「新增」與「不使用」。
- **不要修改** `StallSceneBuilder.cs`、`Level_StallTest.asset`、`Scenes/Stall_Test.unity`。
- **不要改動** `StallCatalog.Devices` 與 `StallCatalog.DefaultLayout` 的內容
  （做法見第 3 節，要用「兩套 loadout」而不是改舊的那一套）。
- **不要刪除或重排** `ItemKind`、`LevelElementType`、`DyeColorType` 既有的列舉值。
  新增一律往後接，維持存檔相容。
- 對既有檔案的修改只能是**加法**：新增欄位、新增分支、新增可選參數（要有預設值）。
  任何會讓 `Stall_Test` 行為改變的修改，都要先停下來問我。

驗收的第一條就是：**做完之後 `Stall_Test` 打開按 Play 仍然照舊能跑完一輪。**

---

## 1. 目標

新開一個場景 `Village_Test`，驗證 v6 的核心假設：

> 「素材長在會走動的 NPC 身上、顏色是撿來的不是做出來的」，玩起來有沒有比較有趣。

這一批要做的五件事，其他一律不做：

1. 會走動、身上有不同顏色毛的 NPC，可以剃，被剃會逃跑
2. 全隊共用的大背包（先不管上限）
3. 素材箱：佈置時從背包擺出來，互動一次取出一份羊毛
4. 織布機：吃 1–2 份毛 → 主色 + 點綴色的衣服
5. 顧客：上門、要一件做得出來的衣服、交貨拿錢

**明確不做**：飾品機、區域解鎖、稀有度分級、版型切換、牆面塗鴉、品質維度、
染色與塗抹（v6 沒有這條線）、人偶（先擱置，見第 8 節）。

---

## 2. 資料層改動（加法）

### 2.1 `Core/GameEnums.cs`

往後追加，不要動既有值：

```csharp
// LevelElementType 末端追加
MaterialCrate  = 24,  // 素材箱：佈置時從背包擺出，互動一次跳一份羊毛
WeavingMachine = 25,  // 織布機：吃 1-2 份毛 -> 主色 + 點綴色的衣服
WoolNpc        = 26,  // 會走動、可剃毛、也會來當顧客的 NPC
```

`ItemKind` 這一批不用新增（羊毛與衣服都已經有了）。

### 2.2 `Core/GarmentSpec.cs`

加一個 `AccentColorRaw` 欄位與 `AccentColor` 存取子。三件事要注意：

- `Create(...)` 的 accent 參數放在**最後面並給預設值** `DyeColorType.White`，
  這樣所有既有呼叫點（`SewingMachine`、`Juicer`、`OrderBoard`）都不用改。
- `Matches()` 要納入 accent 比對。
- `Describe()` 在 accent 不等於主色時才附加，例如「紅底藍紋 T 恤」；
  相同時維持舊的敘述格式，`Stall_Test` 的顯示才不會變。

### 2.3 `Core/GameTuning.cs`

在檔案**最末端**開一個 `// ===== v6 羊駝村 =====` 區塊，既有數值一個都不要改。
下面這些值我先定了，實作時直接用，不要再問：

```
// NPC
NpcWanderSpeed        = 1.6f
NpcWanderRadius       = 18f    // 以出生點為圓心
NpcWanderPauseMin/Max = 1.5f / 4f
NpcFleeSpeed          = 4.5f
NpcFleeSeconds        = 4f
NpcFleeceMax          = 3      // 身上最多 3 份毛（沿用既有觀念，但獨立一個值）
NpcFleeceRegenSeconds = 8f
NpcShearRange         = 2.2f

// 共同背包
StashCapacityPerColor = 20

// 素材箱
CrateUnitsPerCrate    = 6      // 一箱 6 份
CrateFootprint        = 1x1

// 織布機
WeaveSingleSeconds    = 4f     // 單色衣服
WeaveDoubleSeconds    = 7f     // 雙色衣服（一定要大於單色，這是玩法前提）
WeaveMaxWool          = 2

// 顧客
CustomerMaxConcurrent = 3
CustomerPatienceSeconds = 45f
CustomerIntervalSeconds = 12f
CustomerWalkSpeed     = 2.2f
CustomerLeavePenalty  = 30     // 等太久走掉，扣這麼多

// 羊毛價格（主色 + 點綴色相加；只有主色時就只算一份）
WoolPrice: White 10 / Yellow 15 / Green 20 / Blue 30 / Red 45
```

價格用一個 `public static int WoolPrice(DyeColorType c)` 提供，不要散在各處。

---

## 3. 把手提箱內容拆成兩套 loadout

`StallCatalog.Devices` / `DefaultLayout` 現在是全域唯一的一套，
`Stall_Test` 和 `Village_Test` 需要不同的內容，所以要拆。

做法：新增 `Stall/StallLoadout.cs`，內含兩個 `static readonly` 設定物件

- `StallLoadout.Classic` — 內容**原封不動**照抄目前 `StallCatalog.Devices`
  與 `DefaultLayout`（含剃毛器架、果汁機、人偶）
- `StallLoadout.Village` — v6 的內容：

```
裝備：織布機 x1、交貨窗口 x1、輸送帶 x2、素材箱 x3
（沒有剃毛器架 —— 剃毛器改成預設工具；沒有果汁機、沒有人偶）

預設佈局（8x8，(0,0) 在左後角，z 往前 = 顧客那一側）
  z=6            [交貨窗口]
  z=5            [輸送帶↑]
  z=4            [輸送帶↑]
  z=3            [織布機]
  z=2   [素材箱] [素材箱] [素材箱]
        x=2      x=3      x=4
```

`StallCatalog` 保留現有 API，內部改成讀 `StallCatalog.Active`，
**預設值必須是 `StallLoadout.Classic`** —— 這樣什麼都不設定時舊場景行為不變。
`StallManager` 加一個 `[SerializeField] bool _villageLoadout = false`，
在 `Spawned()` 裡設定 `StallCatalog.Active`。只有 `Village_Test` 的場景建置器會把它打勾。

`ValidateStallSetup` 那類檢查要對兩套 loadout 各跑一次。

---

## 4. 五個新系統

### 4.1 `Npc/WoolNpc.cs` — 會走動、可剃毛的 NPC

`NetworkBehaviour` + `IInteractable`（照 `NetworkInteractable` 的既有慣例做）。

- `[Networked] int ColorRaw`（身上羊毛的顏色）、`[Networked] int Fleece`、
  `[Networked] TickTimer RegenTimer`、`[Networked] TickTimer FleeTimer`
- 移動用 `NetworkCharacterController`，理由跟玩家一樣：
  **裸 `CharacterController` + `NetworkTransform` 撐不過 Fusion 的 resimulation。**
  這點已經在玩家身上踩過一次坑了，不要重蹈。
- 狀態機只有三個狀態：`Wander`（走向隨機目標點）、`Pause`（站著）、`Flee`（背對玩家跑）
- 剃毛：手上拿著 `ShearsTool` 時互動一次 → `Fleece--`、
  **羊毛直接進共同背包**（不生成掉在地上的物品）、進入 `Flee`、播音效
- `Fleece == 0` 時互動要給提示「牠身上的毛剃光了」而不是無反應
- 外觀：身體用既有的 placeholder 幾何，**毛的部分**用 `MaterialPropertyBlock`
  染成 `PlaceholderPalette.Dye(ColorRaw)`，剃光時轉成灰白 —— 玩家要能一眼看出誰還有毛

### 4.2 `Stall/TeamStash.cs` — 全隊共用大背包

掛在 `[GameSystems]` 上的 `NetworkBehaviour`，單例（照 `OrderBoard.Instance` 的寫法）。

- `[Networked, Capacity(8)] NetworkArray<int> Wool`（索引 = `DyeColorType`，容量留餘裕）
- `bool TryAdd(DyeColorType, int)`、`bool TryTake(DyeColorType, int)`、`int Count(DyeColorType)`
- `static event Action OnStashChanged`，HUD 靠它更新
- 上限先用 `StashCapacityPerColor`，滿了就拒收並跳提示（設計文件說「先不管上限」，
  但值本身要接好，之後只要調數字就能開）
- HUD：畫面左下角一排色塊 + 數字。用既有的 `UIFactory` 做，不要引新的 UI 套件

### 4.3 `Machines/MaterialCrate.cs` — 素材箱

- 是一台 `DeployableDevice`，佔 1 格，走既有的網格擺放流程（不用另外寫放置邏輯）
- `[Networked] int ColorRaw`、`[Networked] int Remaining`
- **佈置模式**：互動一次 = 按箱子上的實體按鈕，**循環切換顏色**
  （只在背包裡有那個顏色的毛之間循環，沒有的顏色跳過）。不跳 UI，狀態顯示在箱體顏色上。
- **開張的瞬間**：從背包扣掉 `min(CrateUnitsPerCrate, 背包存量)`，寫進 `Remaining`，**此後鎖定**
- **營業模式**：空手互動一次 → `Remaining--`，生成一份該色羊毛到手上
- `Remaining == 0` 時箱體轉灰、提示「空了」
- 收攤時把 `Remaining` 剩下的**退回背包**（不然玩家會不敢多擺）

### 4.4 `Machines/WeavingMachine.cs` — 織布機

> **如果 `PROMPT_織布機.md` 已經執行過，這一節整節跳過**（含 `GarmentSpec` 的 accent
> 與 `MachineBase.RetargetProcess`），改成把織布機加進 Village loadout 就好。

繼承 `MachineBase`，實作 `IThrownItemReceiver`。沿用「做好留在機台、手動取貨、
旁邊有輸送帶就自動出貨」那一整套，不要另外發明。

**沒有開始鈕。** 羊毛放下去那一刻就開始織。這台機器的玩法是一道**時間壓力下的選擇**：

```
放入第一份毛（主色）
   │  立刻開始織，目標時間 = 單色 4 秒
   │
   ├─ 4 秒內沒有第二份毛 ────────────→ 產出單色衣服
   │
   └─ 4 秒內放入第二份毛（點綴色）
          目標時間改成雙色 7 秒
          **已經過的時間算數**：第 3 秒放入 → 還要 4 秒，不是重新跑 7 秒
                              ────────→ 產出雙色衣服
```

也就是說：想做雙色，第二份毛必須在單色織完之前送到。這正好讓丟接與輸送帶有了意義。

- `[Networked] int WoolCount`、`[Networked] int MainColorRaw`、`[Networked] int AccentColorRaw`
- 放入第一份毛 → 主色、`BeginProcess(WeaveSingleSeconds)`
- 織製中放入第二份毛 → 點綴色、把目標時間改成 `WeaveDoubleSeconds`，**保留已經過的時間**
- 已經有兩份、或已經織完（`HasOutput`）時再放要拒絕並提示
- 產出 `GarmentSpec`：`Pattern = TShirt`、`Color = 主色`、`AccentColor = 點綴色`
  （只放一份時 accent = 主色）
- 空手互動 = 取出成品，沒有別的意思

**`MachineBase` 要加的東西**（加法，既有行為不變）：

```csharp
/// <summary>換一個目標總時長，但已經過的時間算數。只在 StateAuthority 呼叫。</summary>
protected void RetargetProcess(float newTotalSeconds)
```

實作要點：`elapsed = ProcessDuration - ProcessTimer.RemainingTime(Runner)`，
新的 `remaining = Mathf.Max(0.2f, newTotalSeconds - elapsed)`，
然後重設 `ProcessDuration = newTotalSeconds` 與 `ProcessTimer`。
下限 0.2 秒是為了避免「剛好在最後一瞬間塞進第二份毛」變成瞬間完成 ——
玩家會看不到那件衣服是怎麼變成雙色的。

**`CanAcceptThrown` 的條件變了。** 舊機台一律 `!Processing && !HasOutput`；
織布機必須**在織製中也接受**丟進來的第二份羊毛，否則「丟毛趕上雙色」這個玩法直接不成立。
條件改成 `!HasOutput && WoolCount < WeaveMaxWool`。
這是這一批唯一一處對 `IThrownItemReceiver` 慣例的偏離，**只改織布機，不要動縫紉機和果汁機。**

**進度條的畫法（重要，不要照抄 `MachineBase` 的預設）**

`Progress01` 是 `已經過 / 目標時長`。目標時長從 4 變成 7 的那一刻，
分母變大，條子會**往回縮**（3/4 = 75% → 3/7 = 43%）。
使用者要的是「跑到一半，而不是從頭開始」，但往回縮看起來還是像 bug。

所以織布機的進度條要**永遠以 `WeaveDoubleSeconds` 為滿格**，並在 `4/7` 的位置畫一道刻度線：

```
放入第一份毛：  [████░░░│░░░░░░]   ← 填色前進，終點在刻度線
放入第二份毛：  [████░░░│░░░░░░]   ← 填色一格都不動，只有終點往後移到滿格
```

這樣「已經過的時間算數」是**看得見的** —— 填色從頭到尾單調前進，
第二份毛的代價直接表現成「終點被推遠了」。覆寫 `Render()` 自己畫，不要動 `MachineBase` 的預設。

- 機體外觀還是要能看出裝了什麼：兩個小色塊，第一格主色、第二格點綴色

### 4.5 顧客：`Npc/CustomerQueue.cs` + `Npc/Customer.cs`

**不要改寫 `OrderBoard`**，另外做一套，理由是 `Stall_Test` 還在用它。

- `CustomerQueue`：掛在 `[GameSystems]`，單例，只在 `StallState.Open` 期間運作
  - 每 `CustomerIntervalSeconds` 找一隻**場上的 `WoolNpc`** 轉成顧客（同一群，符合設計），
    場上不夠就生一隻新的
  - 同時最多 `CustomerMaxConcurrent` 位
  - **需求從「已擺出的素材箱做得出來的組合」抽**：
    掃描場上 `Remaining > 0` 的 `MaterialCrate`，取顏色集合，
    隨機挑一個主色、再挑一個點綴色（有機率只挑主色 = 低價位訂單）。
    **這是「永遠不會有無解訂單」的保證，實作時不要偷懶改成從固定池子抽。**
  - 價格 = `WoolPrice(主色) + WoolPrice(點綴色)`（只有主色時就只算一份）
- `Customer`：走到 `DeliveryCounter.CustomerQueueAnchor` 附近排隊，
  頭上浮一個色塊看板顯示想要什麼，`CustomerPatienceSeconds` 倒數，
  逾時 → 轉身走掉 + `LevelDirector.AddMoney(-CustomerLeavePenalty, "顧客等太久")`
- `DeliveryCounter` 的交貨判定：**先問 `CustomerQueue.Instance`，沒有才 fallback 到 `OrderBoard`**。
  這一行 fallback 就是舊場景不會壞的關鍵，不要省。

---

## 5. 佔位資產與場景建置器

### 5.1 `PlaceholderAssetBuilder.cs`

在既有流程**末端追加**新 prefab 的建置（`WoolNpc` / `MaterialCrate` /
`WeavingMachine` / `Customer`），既有的建置段落一行都不要動。
每台裝備 prefab 記得掛 `NetworkObject` 與 `DeployableDevice`，
不然執行期生成或收攤會失敗 —— `ValidateStallSetup` 已經在檢查這兩項了。

做完提醒我跑 `Tools > Fusion > Rebuild Prefab Table`。

### 5.2 新檔案 `Editor/VillageSceneBuilder.cs`

**照抄 `StallSceneBuilder` 已驗證的兩段式流程**：先把骨架存成真正的場景檔，
重新 `OpenScene` 之後才放置關卡元件，而且**換過場景一定要重新
`AssetDatabase.LoadAssetAtPath` 載入 `LevelDefinition`** ——
換場景會讓 ScriptableObject 參考變成 Unity 的「假 null」，
這個坑之前花了三輪才找到，不要再踩一次。

- 資產：`Levels/Level_VillageTest.asset`，場景 `Scenes/Village_Test.unity`
- 內容：
  - 60x60 平地（一定要有，不然玩家一直往下掉）
  - 2 個玩家出生點、1 個手提箱生成點
  - **8–10 隻 `WoolNpc`** 散佈在場上，顏色平均分配到 5 色
  - `OrderBoardAnchor`（HUD 錨點沿用）
  - **不要**放染料點、不要放斜坡（那是舊場景在驗證平坦度檢測用的）
- `[GameSystems]`：`LevelDirector.stallMode = true`、掛 `StallManager`
  並勾選 `_villageLoadout = true`、掛 `TeamStash`、掛 `CustomerQueue`
- 比照 `VerifyStallScene` 寫一份 `VerifyVillageScene`，至少檢查：
  `TeamStash` / `CustomerQueue` / `StallManager` / `LevelDirector`(stallMode) /
  出生點 / `[Level]` 底下的物件數 == `def.elements.Count` / 有 FloorTile /
  場上 `WoolNpc` 數量正確
- 沿用 `BuildReport`，建完寫報告檔

### 5.3 選單

新開一個群組，**不要動 `擺攤/` 底下任何一項**：

```
羊駝很忙/v6 羊駝村/1. 建置羊駝村測試場景
羊駝很忙/v6 羊駝村/2. 一鍵重置羊駝村測試場景
羊駝很忙/v6 羊駝村/3. 檢查羊駝村設置（診斷用）
```

`0. 一鍵重建` **維持現在的行為**（重建資產 + `Stall_Test`），先不要改它的落點——
兩個場景並存的期間，我要自己決定停在哪一個。

---

## 6. 剃毛器變成預設工具

- `PlayerCarry` 加 `ToggleDefaultTool()`：按 **`E`** 生成一把 `ShearsTool` 到手上，
  再按一次收起來（despawn，不是丟在地上）
- `LocalInputProvider` 加對應的按鍵位元。既有按鍵一個都不要改
  （WASD/方向鍵移動、Space 或左鍵互動、Q 丟接、右鍵持續使用工具）
- 手上有別的東西時按 `E` → 提示「先空出手」，不要靜默失敗
- **不要移除 `ToolRack.cs`**，`Classic` loadout 還在用它

---

## 7. 驗收清單

做完請自己逐條走過，並在回報裡逐條回答。

**回歸（最重要）**

1. 打開 `Stall_Test` 按 Play：開箱 → 擺攤 → 敲鈴開張 → 剃毛 → 縫紉機 →
   輸送帶 → 交貨，整條線跟這一批之前完全一樣
2. `Stall_Test` 的果汁機、染劑罐、人偶塗抹仍然能用
3. `檢查擺攤設置` 沒有新的錯誤

**新場景**

4. 打開 `Village_Test` 按 Play，`[Level]` 底下物件數正確、有地板、不會往下掉
5. 按 `E` 拿出剃毛器，走近 NPC 互動 → 掉一份毛進背包、NPC 逃跑、
   HUD 對應顏色 +1、NPC 身上的毛少一撮
6. 剃到 0 之後互動有提示、過 8 秒長回一份
7. 開箱之後場上有：織布機 x1、交貨窗口 x1、輸送帶 x2、素材箱 x3，
   **沒有**剃毛器架、果汁機、人偶
8. 佈置模式下互動素材箱可以循環切換顏色，且只在背包有存量的顏色之間循環
9. 敲鈴開張 → 素材箱從背包扣款、鎖定、營業中不能再切顏色
10. 素材箱互動一次拿到一份對應顏色的羊毛
11. 織布機放 1 份毛 → **立刻開始織**，4 秒後產出單色衣服（全程不用按任何開始鈕）
12. 織到第 2 秒左右放入第二份毛 → 變成雙色衣服，**再過約 5 秒**完成（總共 7 秒），
    不是從頭跑 7 秒。用碼表量一次，誤差不該超過 0.3 秒
13. 上一條的過程中，進度條的填色**只前進、不倒退**，刻度線在 4/7 的位置，
    放入第二份毛時填色不動、終點往後移
14. 織製中把羊毛**丟**進織布機也算數（不是只有手放才行）
15. 兩份毛交換放入順序會得到不同的衣服（紅底藍紋 ≠ 藍底紅紋）
16. 單色織完的瞬間才放第二份毛 → 應該被拒絕並提示，不會憑空變成雙色
17. 織布機旁有輸送帶時成品自動出貨，兩條輸送帶會串接
18. 顧客會走到窗口排隊、頭上顯示想要什麼，且**想要的一定是場上素材箱做得出來的**
19. 交出正確的衣服 → 加錢、顧客離開；交錯 → 扣錢
20. 顧客等超過 45 秒 → 走掉並扣 30
21. 收攤時素材箱剩下的毛會退回背包

**連線**

22. 用兩個 client 跑一次：NPC 的位置與剩餘毛量、背包數量、素材箱顏色與存量、
    織布機的兩格色塊與進度條、顧客的需求與排隊位置，兩端一致
23. **client 端**在織製中丟一份毛進織布機，主機端也要看到它變成雙色、
    進度條的終點跟著往後移（`RetargetProcess` 要撐得過 resimulation）
24. client 端剃毛、拿素材、放毛、交貨都不會出現位置抖動或瞬移

---

## 8. 兩件先擱置的事，不要自己決定

- **人偶**：v6 沒有塗抹之後它就沒有功能了。這一批的做法是
  **留在 `Classic` loadout 裡不動、`Village` loadout 不放它**，
  等我玩過 `Village_Test` 再決定要移除還是改成「展示成品吸引顧客」。
- **計時 vs 素材雙重限制**：設計文件裡有註記兩個都緊會讓玩家無所適從。
  這一批先把計時給得很寬（沿用 180 秒），**讓素材成為真正的限制**。
  如果測起來反而是計時先到，回報給我，不要自己調數值。

---

## 9. 開工前

先把上面看完，**有任何不確定或覺得會撞到既有系統的地方，先問我再動手**。
特別是第 3 節的 loadout 拆分和第 4.5 節的 `DeliveryCounter` fallback，
這兩處是「新場景不會弄壞舊場景」的關鍵接縫。
