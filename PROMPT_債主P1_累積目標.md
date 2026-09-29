# 施工指令：債主 P1 —— 累積目標與失敗判定

> 貼給新對話即可。前提：「一天一個時鐘」與「夜晚階段」都已完成。
> 大哥本人與全員睡覺是 P2，這一批**不要做任何角色**。

---

## 0. 紅線

- 所有新行為只在 `_runMode == true` 生效。`Stall_Test`（`_runMode == false`）
  的判定與流程**完全不變**
- 不要動 `BeginDay()` 的時鐘邏輯、不要動 `LevelDirector`
- 不要新增 `StallState`
- 不要改 `NightMarker`（P2 才換成床）
- 數值是暫定的，**不要花時間調**

---

## 1. 最大的改動：門檻從「當天賺的」變成「累積的資本額」

**現在**：`Settle()` 判定 `RoundRevenue >= RoundTarget`（今天賺的 vs 今天門檻）。

**要變成**：判定 `Capital >= TargetFor(day)`（**總資本** vs **累積目標**）。

資本額**只進不出，不扣款** —— 大哥是來查帳的，不是來收錢的。
所以目標必須是累積值而且一路往上爬，否則過一次就永遠過。

這帶來一個刻意保留的性質：**好日子可以補壞日子。** 今天賺很少但之前有存，照樣過得去。

---

## 2. 判定時機要搬到「天黑那一刻」

失敗**不進夜晚**，天黑當下直接結束。所以判定要從 `Settle()` 搬到 `EndDay()`。

```
EndDay()                          ← 天黑
  取消待放置、請走顧客、定格 RoundRevenue   （現況，不動）
  Capital += RoundRevenue                  ← 從 Settle() 搬過來
  達到 TargetFor(CurrentRound)？
      ├─ 是 → SetState(Night)              （現況）
      └─ 否 → SetState(RunOver)            ← 不進夜晚，直接結束

Settle()                          ← 夜晚結束（玩家互動 NightMarker）時才呼叫
  不再判定通過與否（天黑時已經判完了）
  RoundsCompleted++、CurrentRound++
  SetState(Settling)、發 RPC
```

`Settle()` 裡的 `Capital += revenue` 與 `passed` 判定都要移除 ——
那兩件事現在屬於 `EndDay()`。

`DebugForceNight()`（N 鍵）走的是同一支 `EndDay()`，所以自動跟著改，不用另外處理。

---

## 3. 新檔案 `Scripts/Orders/CreditorSchedule.cs`

純靜態，沒有狀態，最好測。

```csharp
public static class CreditorSchedule
{
    /// <summary>每幾天一次大額。</summary>
    public const int BalloonEveryDays = 3;

    public static bool IsBalloonDay(int day) => day > 0 && day % BalloonEveryDays == 0;

    /// <summary>第 day 天結束時，資本額至少要有多少（累積值）。</summary>
    public static int TargetFor(int day)
    {
        // 每天累加一筆「當日增量」，大額日的增量特別大。
        // 累加而不是直接給公式，是為了保證一定單調遞增。
        double total = 0;
        for (int d = 1; d <= day; d++) total += DailyIncrement(d);
        return Mathf.RoundToInt((float)total);
    }

    /// <summary>第 day 天的增量。HUD 想顯示「今天要多賺多少」時也用得到。</summary>
    public static double DailyIncrement(int day)
    {
        double baseline = GameTuning.DayTargetBase
                        * System.Math.Pow(GameTuning.DayTargetGrowth, day - 1);
        return IsBalloonDay(day) ? baseline * GameTuning.BalloonMultiplier : baseline;
    }
}
```

### `Core/GameTuning.cs`（檔案最末端）

```csharp
// ---------- 債主的目標（數值暫定，等實際產能量過再調）----------
public const float DayTargetBase       = 300f;  // 第 1 天的增量
public const float DayTargetGrowth     = 1.35f; // 平常日每天乘這個
public const float BalloonMultiplier   = 2.0f;  // 大額日的增量再乘這個
```

既有的 `StallTargetFor()` 之後就沒有呼叫者了 —— **留著不要動**，
免得動到 `Stall_Test` 那條路。

---

## 4. `StallManager`

- `BeginDay()` 裡 `RoundTarget = _runMode ? CreditorSchedule.TargetFor(CurrentRound) : 0;`
  （欄位名維持 `RoundTarget`，改名牽動太多地方；它現在的意義是「今天的累積目標」）
- 新增唯讀屬性方便 HUD 用：

```csharp
/// <summary>含今天還沒入帳的收入 —— 玩家真正在意的是這個數字。</summary>
public int ProjectedCapital => Capital + (DayActive ? CurrentRevenue : 0);

public bool TodayIsBalloon => _runMode && CreditorSchedule.IsBalloonDay(CurrentRound);
```

- `RoundRemaining` 改成用 `ProjectedCapital` 算：`Mathf.Max(0, RoundTarget - ProjectedCapital)`
- `RPC_RoundSettled` 已經帶 `capital` 與 `target`，如果簽章不夠用就加參數，不要改既有參數的意義

---

## 5. HUD（`Stall/StallHud.cs`）

### 5.1 用詞統一成「目標」

不要出現「應繳」「門檻」等其他說法。

### 5.2 白天那一行

主要比較的是**資本額**，不是今天賺的：

```
第 2 天　剩 3:12　目標 705　資本 520（今天 +180）　還差 185
```

- 「資本」用 `ProjectedCapital`
- 括號裡是今天的即時營收，讓玩家知道今天的貢獻
- 達標瞬間變色 + 顯示「已達標！」

### 5.3 大額日換顏色

`TodayIsBalloon` 為真時，整行（或至少「目標 XXX」那一段）用**明顯不同的顏色**。
不要用文字說「今天是大額日」—— 顏色就是那個訊號。

### 5.4 未來三天

主行下面加一排小字，顯示接下來三天的目標，大額日同樣用那個顏色：

```
第 3 天 1,340　第 4 天 1,750　第 5 天 2,230
```

**這是玩家唯一的預警系統**（大哥平常不出現），所以一定要做 ——
沒有它，第 3 天的大額就是驚喜死亡。

### 5.5 夜晚那一行

維持現有結構，把「今天賺了 X 目標 Y」改成資本額的講法：

```
第 2 天　夜晚　資本 700　目標 705　還差 5
```

---

## 6. 驗收

**回歸**

1. `Stall_Test` 按 Play：判定仍然是「今天賺的 vs 180 秒的門檻」，跟這一批之前一樣
2. `Stall_Test` 的 HUD 沒有多出資本／未來三天那些

**羊駝村**

3. 第 1 天 HUD 顯示「目標 300」，資本從 0 開始
4. 賣東西時「資本」跟著漲（`Capital + 今天的收入`），「還差」跟著減
5. 達標後變色顯示「已達標！」
6. **第 3、6、9 天的目標用不同顏色**，未來三天那一排也一樣
7. 天黑時資本 ≥ 目標 → 進夜晚，跟這一批之前一樣
8. **天黑時資本 < 目標 → 直接 `RunOver`，不進夜晚**（天不會變黑再亮）
9. 存款有效：第 2 天賺得很少，但第 1 天存很多 → 照樣過關
10. 用 M 鍵加錢可以把當天補過，流程正確
11. 用 N 鍵強制天黑，判定跟時間到完全一樣
12. 過關那天：夜晚 → 互動 `NightMarker` → 結算 → 隔天目標變大

**連線**

13. 兩個 client 的資本、目標、大額日顏色、未來三天一致
14. `RunOver` 兩邊同時發生

---

## 7. 這一批不要做的

- **不做大哥這個角色**（P2）
- 不做全員睡覺、不改 `NightMarker`（P2）
- 不做失敗畫面的演出
- 不做升級系統、不動 `Capital` 的消耗
- **不調數值** —— 白天 300 秒的實際產能還沒量過，現在調是猜的

---

## 8. 開工前

有不確定的先問我。特別是第 2 節的判定搬家 ——
`Capital += revenue` 與 `passed` 從 `Settle()` 移到 `EndDay()` 是這一批的核心，
要確認 `RunOver` 那條路不會漏掉結算畫面該顯示的資料。
