# 施工指令：一天一個時鐘

> 貼給新對話即可。把計時從「只涵蓋營業」改成「涵蓋整個白天」。
> 夜晚階段與債主是下一批，這一批不要做。

---

## 0. 紅線

**一、所有新行為只在 `_runMode == true` 生效。**

`StallManager._runMode` 預設 false，`Stall_Test` 走的是 false 那條路。
`_runMode == false` 時計時的起點、結算的觸發**必須跟現在完全一樣**
（開張才開始跑、180 秒、只有 `Open` 期間會到期）。

**二、不要改 `LevelDirector.BeginStallRound()` 的實作**，只改「誰在什麼時候呼叫它」。

**三、不要新增 `StallState`。** 夜晚是下一批的事，這一批時間到還是進 `Settling`。

**四**、不要動門檻公式 `StallTargetFor()`、不要動 `StallTargetBase / Growth`。

---

## 1. 現在的樣子 vs 要變成的樣子

```
現在
  Exploring（無時限）→ Deploying（無時限）→ 敲鈴 → Open（180 秒開始跑）→ Settling

要變成
  ┌─────────── 白天：300 秒，從這裡就開始跑 ───────────┐
  Exploring ──→ Deploying ──→ 敲鈴 ──→ Open ──────────┘→ Settling
                  ↑ 計時全程不重置、不暫停
  時間到：不管你在哪個階段，一律強制結算
```

核心是讓「**什麼時候停止採集、開始賣**」變成玩家的決策 —— 每多採一秒就少賣一秒。

---

## 2. 時鐘搬家

`BeginStallRound()` 目前在 `OpenForBusiness()`（約第 919 行）被呼叫。
把它搬到**一天的開始**，也就是進入 `Exploring` 的那兩個地方：

- `Spawned()` 裡初始化的那次（第一天）
- `DismissSettlement()` 之後回到 `Exploring` 的那次（第二天以後）

建議抽一支 `private void BeginDay()` 統一處理，兩邊都呼叫它：

```csharp
private void BeginDay()
{
    if (!HasStateAuthority) return;

    RoundRevenue = 0;
    RoundDeliveries = 0;
    RoundMissed = 0;
    RoundTarget = _runMode ? GameTuning.StallTargetFor(CurrentRound) : 0;

    SetState(StallState.Exploring);

    if (_runMode)
        LevelDirector.Instance?.BeginStallRound(GameTuning.DayDurationSeconds);
}
```

`OpenForBusiness()` 裡要**移除** `BeginStallRound(...)` 的呼叫（run 模式下），
但 `_runMode == false` 時仍然要照舊呼叫 `BeginStallRound(StallDurationSeconds)`。

---

## 3. 門檻要在一天開始就寫入（重要）

現在 `RoundTarget` 是在 `OpenForBusiness()` 才算的。這樣玩家在採集階段**不知道今天要賺多少**，
那「該採多少才夠」就無從判斷 —— 整個決策會消失。

所以 `RoundTarget` 必須在 `BeginDay()` 就寫好（上面已經包含），
而且 HUD 從探索階段就要顯示它。

---

## 4. 時間到要能從任何狀態結算

`StallManager` 目前只在 `State == StallState.Open` 時檢查 `LevelDirector.Running`
（約第 248 行 `if (State != StallState.Open) return;`）。

改成：**`_runMode` 且 `Running` 由真變假時，不管在 `Exploring` / `Deploying` / `Open` 都進入結算。**

三個邊緣狀況要處理：

- **從沒開張過** → 營收 0，`Settle()` 照算（一定不達標）。
  結算畫面要說清楚「今天沒有開張」，不要只顯示差額
- **正在放置裝備**（`PlayerStallAgent` 手上有待放的機台）→ 先取消放置再結算，
  不要讓幽靈預覽留在畫面上
- **襯布還沒展開** → 照樣結算，不要當成錯誤

`Settling` 期間計時不跑（`Running` 已經是 false），不用特別處理。

---

## 5. `Core/GameTuning.cs`

檔案最末端新增，**既有的 `StallDurationSeconds = 180` 一個字都不要改**
（`_runMode == false` 與 `LevelDefinition` 還在用它）：

```csharp
// ---------- 一天的長度（run 模式）----------
/// <summary>一整個白天：採集 + 佈置 + 販售全包。</summary>
public const float DayDurationSeconds = 300f;

/// <summary>剩這麼多秒還沒開張就開始警告。</summary>
public const float DayNotOpenWarnSeconds = 60f;
```

---

## 6. HUD（`Stall/StallHud.cs`）

**全程顯示**，不是只有營業中：

```
第 3 天　剩 2:14　目標 252　已賺 180　還差 72
```

- 剩餘時間用 `LevelDirector.RemainingSeconds`
- 只在 `_runMode` 時顯示這一行
- **達標的瞬間變色**並顯示「已達標！」—— 後半天就從焦慮變成多賺多賺
- **還沒開張的警告**：`State != Open` 且剩餘 < `DayNotOpenWarnSeconds` 時，
  明顯地提示「還沒開張！」（變紅／閃爍）。這是新玩家最容易犯的錯
- 顯示文字用「**第 N 天**」而不是「第 N 輪」。
  `CurrentRound` 這個欄位名稱**不要改**（改名會牽動太多地方），只改顯示字串

---

## 7. 驗收

**回歸**

1. `Stall_Test` 按 Play：計時仍然是**敲鈴才開始**、180 秒、可以無限輪次玩下去
2. `Stall_Test` 的 HUD 沒有多出「剩餘時間／目標」那一行

**羊駝村**

3. 一進場（第 1 天）HUD 就顯示「第 1 天　剩 5:00　目標 120」
4. 在城市裡採集時，時間**持續在跑**
5. 開箱佈置時，時間**持續在跑**，敲鈴**不會重置**
6. 敲鈴開張後繼續跑同一個時鐘，沒有跳動
7. 賣東西時「已賺」增加、「還差」減少，達標瞬間變色
8. **時間到但還在探索** → 強制結算，結算畫面說明「今天沒有開張」
9. **時間到但還在佈置** → 強制結算，待放置的幽靈預覽會被取消
10. 時間到時在營業中 → 照舊結算
11. 達標 → 隔天目標變成 174，時鐘重新從 5:00 開始
12. 沒達標 → 進 `RunOver`，行為跟這一批之前一樣
13. 剩 60 秒還沒開張 → HUD 明顯警告

**連線**

14. 兩個 client 的剩餘時間、天數、目標、已賺一致
15. client 端也看得到「還沒開張」的警告

---

## 8. 這一批不要做的

- **不做夜晚階段**（`StallState.Night`）—— 下一批
- **不做債主** —— 下一批
- 不做動物睡覺
- 不調 `StallTargetBase / Growth`。300 秒的白天會讓產能下降，門檻確實需要重校，
  但要等我玩過拿到實際數字，現在改是猜的
- 不做升級系統

---

## 9. 開工前

有不確定的先問我。特別是第 4 節的「從任何狀態結算」——
要確認取消待放置、襯布沒展開這兩種情況不會留下壞掉的狀態。
