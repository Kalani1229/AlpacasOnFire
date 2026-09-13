# 施工指令：run 循環（上升門檻 + 會結束）

> 貼給新對話即可。這一批只加一件事：**每輪有營收門檻，沒達標 run 就結束。**

---

## 0. 紅線

`StallManager` 是 `Stall_Test` 和 `Village_Test` 共用的。這一批的判定**只能在羊駝村生效**。

- `StallManager` 加 `[SerializeField] private bool _runMode = false;`，**預設 false**
- 只有 `VillageSceneBuilder` 會把它設成 true（照 `_villageLoadout` 既有的做法）
- `_runMode == false` 時，`Settle()` 的行為必須跟現在**一模一樣**
- 不要刪除或重排任何既有列舉值，新增一律往後接
- 做完 `Stall_Test` 按 Play 要能無限輪次玩下去，跟現在完全一樣

---

## 1. 目標

```
Exploring → Deploying → Open（3 分鐘）→ Settling
                                           │
                          RoundRevenue >= 門檻？
                                     ├─ 是 → 回 Exploring，輪數 +1，門檻上升
                                     └─ 否 → RunOver，顯示結算，這局結束
```

**兩個數字不要混在一起**（程式裡已經分開了，不用改結構）：

- `RoundRevenue` — 本輪營收，**用來比對門檻**，每輪歸零
- `Capital` — 累積資本，**只進不出**，之後給升級用

---

## 2. `Core/GameTuning.cs`

檔案最末端加一個區塊，既有數值一個都不要動：

```csharp
// ---------- run 循環（v6）----------
public const int   StallTargetBase   = 120;   // 第 1 輪的門檻
public const float StallTargetGrowth = 1.45f; // 每輪乘這個倍率

/// <summary>第 round 輪（從 1 開始）的營收門檻。</summary>
public static int StallTargetFor(int round)
    => Mathf.RoundToInt(StallTargetBase * Mathf.Pow(StallTargetGrowth, Mathf.Max(0, round - 1)));
```

用這兩個值算出來的曲線（給你對照，不用寫死）：

| 輪 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|
| 門檻 | 120 | 174 | 252 | 366 | 530 | 769 | 1115 |

一件衣服值 20–90（主色 + 點綴色），3 分鐘大概做得完 5–10 件，
所以一輪的合理營收是 150–600 —— 第 6、7 輪會超過物理產能，
一個 run 落在 30–45 分鐘。**這兩個數字一定會再調，所以務必留成常數。**

---

## 3. `Core/GameEnums.cs`

`StallState` 末端追加：

```csharp
RunOver = 4,   // 沒達標，這一局結束
```

---

## 4. `Stall/StallManager.cs`

### 4.1 新的同步狀態

```csharp
[Networked] public int CurrentRound { get; set; }   // 從 1 開始
[Networked] public int RoundTarget  { get; set; }   // 本輪門檻，開張時寫入
```

`Spawned()` 裡跟 `Capital` / `RoundsCompleted` 一起初始化：`CurrentRound = 1`。

### 4.2 `OpenForBusiness()`

現有的 `RoundRevenue = 0;` 旁邊加一行：

```csharp
RoundTarget = _runMode ? GameTuning.StallTargetFor(CurrentRound) : 0;
```

`_runMode == false` 時 `RoundTarget = 0`，代表「沒有門檻」，後面的判定一律放行。

### 4.3 `Settle()`

在 `RoundsCompleted++;` 之後、`SetState(StallState.Settling);` 之前插入判定：

```csharp
bool passed = !_runMode || revenue >= RoundTarget;
if (passed) CurrentRound++;
SetState(passed ? StallState.Settling : StallState.RunOver);
```

**注意**：`Capital += revenue` 照舊執行 —— 就算這輪沒達標，賺到的錢還是算數，
結算畫面才能誠實顯示「你差了多少」。

### 4.4 事件要帶上結果

現有的：

```csharp
public static event Action<int, int, int, int> OnRoundSettled;   // revenue, deals, missed, capital
```

改成多帶兩個參數（`target`, `passed`）。`RPC_RoundSettled` 跟著改。
`StallResultsPanel.Show()` 是唯一的訂閱者，一起改簽章即可。

### 4.5 `RunOver` 的行為

- `DismissSettlement()` 在 `RunOver` 時**不要回到 Exploring**，改成停在原地
- 這一批**不做重開一局**。玩家想再來一次就重開場景 —— 選單留給下一批

---

## 5. HUD（`Stall/StallHud.cs`）

營業中隨時看得到三個數字，這是整個機制唯一的壓力來源，看不到就等於不存在：

```
第 3 輪　目標 252　已賺 180　還差 72
```

- 只在 `_runMode` 且 `State == Open` 時顯示
- **達標的瞬間變色**（例如轉綠）並顯示「已達標！」—— 這個回饋很重要，
  它把後半輪從「還在焦慮」變成「多賺多賺」
- 沿用既有的 `UIFactory`，不要引新的 UI 套件

---

## 6. 結算畫面（`Stall/StallResultsPanel.cs`）

roguelite 的記憶點有一半在這裡，不要只做淡出。

**達標時**：照現在的內容，多一行「第 N 輪達標，下一輪目標 XXX」。

**沒達標時（`RunOver`）**：換一套版面，至少要有

- 標題：`撐過了 N 輪`
- `本輪目標 XXX / 實際賺到 YYY` —— **差多少要很明顯**
- 累積資本、總共賣出幾件
- 這一局最貴的一件衣服是什麼（`GarmentSpec.Describe()`）

差 40 塊跟差 400 塊，玩家的反應完全不同，所以那個數字要大。
最貴的那件需要在 `StallManager` 存一個 `[Networked] GarmentSpec BestSale` 與
`[Networked] int BestSalePrice`，交貨成功時比對更新。

---

## 7. 這一批刻意不做

一次只加一個變數，不然第一場試玩讀不出訊號是哪個造成的。

- **不要動 `NpcFleeceRegenSeconds`** —— 毛先維持會長回來
- 不做區域、不做船票、不做魔王、不做隨機 loadout
- 不做重開一局的選單
- 不改探索階段（維持無時限）

---

## 8. 驗收

**回歸**

1. `Stall_Test` 按 Play：可以無限輪次開張、結算、再開張，跟這一批之前完全一樣
2. `Stall_Test` 的 HUD 沒有多出目標／還差多少那一行
3. `檢查擺攤設置` 沒有新的錯誤

**羊駝村**

4. `Village_Test` 第 1 輪開張時，HUD 顯示「第 1 輪　目標 120」
5. 賣東西時「已賺」跟著增加、「還差」跟著減少
6. 達標瞬間變色並顯示已達標
7. 達標結算 → 回到 Exploring，下一輪目標變成 174
8. 故意不達標 → 進入 `RunOver`，結算畫面顯示「撐過了 N 輪」與差額
9. `RunOver` 之後按關閉不會回到 Exploring
10. 沒達標那一輪賺到的錢仍然計入 `Capital`
11. 結算畫面的「最貴的一件」正確

**連線**

12. 兩個 client：輪數、目標、已賺、達標與否兩端一致
13. client 端也看得到 `RunOver` 的結算畫面

---

## 9. 開工前

有不確定的地方先問我再動手。特別是第 0 節的 `_runMode` 開關 ——
那是 `Stall_Test` 不會被這批弄壞的唯一保障。
