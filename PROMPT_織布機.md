# 施工指令：織布機（可單獨執行）

> 貼給新對話即可。這一份只做一台機器，不碰 v6 的其他系統。
> 完整版在 `PROMPT_v6_羊駝村.md`；那一份的第 4.4 節就是這一份，**兩份不要重複執行**。

---

## 0. 紅線

**不准弄壞現有的 `Stall_Test` 場景。** 它是目前唯一能跑通的東西。

- 對既有檔案只能做**加法**：新增欄位、新增分支、新增有預設值的可選參數
- 不要刪除或重排 `ItemKind` / `LevelElementType` / `DyeColorType` 既有的列舉值，新增一律往後接
- 不要修改 `SewingMachine.cs`、`Juicer.cs` 的行為
- 不要修改 `StallSceneBuilder.cs`、`StallCatalog.Devices`、`Level_StallTest.asset`
- 做完 `Stall_Test` 按 Play 要跟現在完全一樣

---

## 1. 這一批只做一件事

新增一台**織布機**：吃 1–2 份羊毛，產出「主色 + 點綴色」的衣服。
它取代 v6 裡縫紉機的位置，但**這一批不刪縫紉機**，兩台並存。

---

## 2. 行為規格（這是重點，其他都是配合它）

**沒有開始鈕。羊毛放下去那一刻就開始織。**

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

設計意圖：這台機器是一道**限時決策**。想做雙色，第二份毛必須在單色織完之前送到 ——
所以丟接和輸送帶第一次有了真正的用途，不只是省腳程。
實作時任何會削弱「時間壓力」的簡化（例如放了第一份就無限等待）都不要做。

---

## 3. `Core/GarmentSpec.cs` — 加點綴色

加一個 `AccentColorRaw` 欄位與 `AccentColor` 存取子。三件事要注意：

- `Create(...)` 的 accent 參數放在**最後面並給預設值** `DyeColorType.White`，
  這樣既有呼叫點（`SewingMachine`、`Juicer`、`OrderBoard`）一行都不用改
- `Matches()` 要納入 accent 比對
- `Describe()` 只在 accent 不等於主色時才附加（例如「紅底藍紋 T 恤」）；
  相同時維持舊格式，`Stall_Test` 的顯示才不會變

---

## 4. `Core/GameTuning.cs` — 在檔案最末端加一個區塊

既有數值一個都不要改。

```
WeaveSingleSeconds = 4f    // 單色衣服
WeaveDoubleSeconds = 7f    // 雙色衣服（一定要大於單色，這是玩法前提）
WeaveMaxWool       = 2
WeaveRetargetFloor = 0.2f  // 見第 5 節
```

---

## 5. `Machines/MachineBase.cs` — 加一個方法

```csharp
/// <summary>換一個目標總時長，但已經過的時間算數。只在 StateAuthority 呼叫。</summary>
protected void RetargetProcess(float newTotalSeconds)
```

實作要點：

```
elapsed   = ProcessDuration - (ProcessTimer.RemainingTime(Runner) ?? 0f)
remaining = Mathf.Max(WeaveRetargetFloor, newTotalSeconds - elapsed)
ProcessDuration = newTotalSeconds
ProcessTimer    = TickTimer.CreateFromSeconds(Runner, remaining)
```

下限 `WeaveRetargetFloor` 是為了避免「剛好在最後一瞬間塞進第二份毛」變成瞬間完成 ——
玩家會看不到那件衣服是怎麼變成雙色的。

這個方法是**純新增**，沒有任何既有機台會呼叫它。

---

## 6. `Machines/WeavingMachine.cs`

繼承 `MachineBase`，實作 `IThrownItemReceiver`。
沿用「做好留在機台、手動取貨、旁邊有朝外送的輸送帶就自動出貨」那一整套，不要另外發明。

- `[Networked] int WoolCount`、`[Networked] int MainColorRaw`、`[Networked] int AccentColorRaw`
- 放入第一份毛 → 記主色、`BeginProcess(WeaveSingleSeconds)`
- 織製中放入第二份毛 → 記點綴色、`RetargetProcess(WeaveDoubleSeconds)`
- 已經有兩份、或 `HasOutput` 為真時再放要**拒絕並給提示**，不要靜默失敗
- `BuildOutputSpec()`：`Pattern = TShirt`、`Color = 主色`、`AccentColor = 點綴色`
  （只放一份時 accent = 主色）
- 完成後 `WoolCount` / 兩個顏色欄位要歸零，不然下一批會沿用上一件的顏色
- 空手互動 = 取出成品，沒有別的意思
- 機體上兩個小色塊顯示目前裝了什麼：第一格主色、第二格點綴色

### 6.1 `CanAcceptThrown` 的條件變了

舊機台一律 `!Processing && !HasOutput`。
織布機必須**在織製中也接受**丟進來的第二份羊毛，否則「丟毛趕上雙色」這個玩法直接不成立。

```csharp
public bool CanAcceptThrown(CarriableItem item)
    => item != null && item.Kind == ItemKind.Wool
       && !HasOutput && WoolCount < GameTuning.WeaveMaxWool;
```

這是唯一一處對 `IThrownItemReceiver` 慣例的偏離。**只改織布機，不要動縫紉機和果汁機。**

### 6.2 進度條的畫法（不要照抄 `MachineBase` 的預設）

`Progress01` 是 `已經過 / 目標時長`。目標時長從 4 變成 7 的那一刻分母變大，
條子會**往回縮**（3/4 = 75% → 3/7 = 43%）。
邏輯上完全正確，但看起來像 bug。

所以織布機的進度條要**永遠以 `WeaveDoubleSeconds` 為滿格**，
並在 `4/7` 的位置畫一道刻度線：

```
放入第一份毛：  [████░░░│░░░░░░]   ← 填色前進，終點在刻度線
放入第二份毛：  [████░░░│░░░░░░]   ← 填色一格都不動，只有終點往後移到滿格
```

這樣「已經過的時間算數」是**看得見的** —— 填色從頭到尾單調前進，
第二份毛的代價直接表現成「終點被推遠了」。

覆寫 `Render()` 自己畫，**不要動 `MachineBase.Render()` 的預設行為**（其他機台還在用）。

---

## 7. 要怎麼在場景裡摸到它

### 7.1 列舉與 prefab

- `LevelElementType` 末端追加 `WeavingMachine = 25`
- `PlaceholderAssetBuilder` 在既有流程**末端追加**織布機的佔位 prefab
  （既有段落一行都不要動），記得掛 `NetworkObject` 與 `DeployableDevice`
- 做完提醒我跑 `Tools > Fusion > Rebuild Prefab Table`

### 7.2 用除錯鍵測手感，不要改場景

**不要**把織布機加進 `StallCatalog.Devices`（那會改到 `Stall_Test` 的開箱內容）。
改成擴充既有的 `Debug/DebugItemSpawner.cs`：

- `2` → 在腳邊生成一台織布機
- `3` / `4` / `5` → 生成紅 / 藍 / 黃羊毛（現有的 `1` 維持白羊毛不動）
- 維持既有的 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 包裹

理由：這樣在**任何場景**都能測，不用等 v6 的新場景做好，也不會動到舊場景的佈局。
之後 `Village_Test` 做出來時再把它放進 Village loadout。

> 我用 Mac，F 鍵都被系統佔走了，所以一律用數字鍵。

---

## 8. 驗收清單

做完請自己逐條走過，並在回報裡逐條回答。

**回歸**

1. `Stall_Test` 按 Play：開箱 → 擺攤 → 開張 → 剃毛 → 縫紉機 → 輸送帶 → 交貨，
   跟這一批之前完全一樣
2. 縫紉機和果汁機在織製中仍然**不接受**丟進來的東西（沒有被 6.1 波及）
3. `檢查擺攤設置` 沒有新的錯誤

**織布機**

4. 按 `2` 生出織布機，按 `3` 生出紅羊毛
5. 放 1 份毛 → **立刻開始織**（不用按任何開始鈕），4 秒後產出單色衣服
6. 織到第 2 秒左右放入第二份毛 → 變成雙色，**再過約 5 秒**完成（總共 7 秒）。
   用碼表量一次，誤差不該超過 0.3 秒
7. 上一條的過程中進度條的填色**只前進、不倒退**；刻度線在 4/7；
   放入第二份毛時填色不動、只有終點往後移
8. 織製中把羊毛**丟**進織布機也算數（不是只有手放才行）
9. 交換兩份毛的放入順序會得到不同的衣服（紅底藍紋 ≠ 藍底紅紋）
10. 單色織完的瞬間才放第二份毛 → 被拒絕並提示，不會憑空變成雙色
11. 剩不到 0.2 秒時放入第二份毛 → 至少還看得到 0.2 秒的織製過程才完成
12. 連續做兩件：第二件不會沿用第一件的顏色
13. 織布機旁擺一條朝外送的輸送帶 → 成品自動出貨

**連線**

14. 兩個 client 跑一次：織布機的兩格色塊、進度條、成品顏色兩端一致
15. **client 端**在織製中丟一份毛進去，主機端也要看到它變成雙色、
    終點跟著往後移（`RetargetProcess` 要撐得過 Fusion 的 resimulation）

---

## 9. 開工前

有任何不確定、或覺得會撞到既有系統的地方，**先問我再動手**。
特別是 6.1（丟接條件破例）和 6.2（進度條自己畫），
這兩處是「新機台不會弄壞舊機台」的關鍵接縫。
