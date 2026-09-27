# 施工指令：夜晚階段（空的，但可以走動）

> 貼給新對話即可。前提：「一天一個時鐘」已完成（`BeginDay` / `EndDay` / `DayActive` 都在）。
> 債主是下一批，這一批只做「夜晚存在、動物睡了、做不了事」。

---

## 0. 紅線

- 所有新行為只在 `_runMode == true` 生效。`Stall_Test`（`_runMode == false`）
  的流程必須**完全不變**：時間到直接 `Settle()`，沒有夜晚
- `StallState` **往後追加**，不重排
- 不要動 `BeginDay()` / `LevelDirector` 的計時邏輯
- 不要動門檻公式與 `StallTargetBase / Growth`
- 夜晚**不計時**

---

## 1. 夜晚插在哪裡（重要）

```
現在
  白天時間到 → EndDay() → DayActive = false → Settle() → Settling → DismissSettlement → BeginDay

要變成
  白天時間到 → EndDay() → DayActive = false → Night
                                                 │ 玩家去找「夜晚的那個東西」互動
                                                 ▼
                                              Settle() → Settling → DismissSettlement → BeginDay
```

**刻意把夜晚放在結算之前**：下一批的債主就是「夜晚的那個東西」，
到時候只是把佔位物換成他，狀態機一行都不用改。

`EndDay()` 現在最後兩行是 `DayActive = false; Settle();` ——
改成 `DayActive = false; SetState(StallState.Night);`（`_runMode` 時），
`Settle()` 延後到玩家結束夜晚才呼叫。

---

## 2. 資料

### `Core/GameEnums.cs`

```csharp
// StallState 末端追加
Night = 5,   // 夜晚：動物睡了，做不了生意；玩家可以走動
```

### `Core/GameTuning.cs`（檔案最末端）

```csharp
// ---------- 夜晚 ----------
/// <summary>天黑到全暗的過渡時間（純視覺）。</summary>
public const float NightFadeSeconds = 3f;
```

### `StallManager`

- `IsNight => State == StallState.Night`
- `IsBusinessMode` / `IsArrangeMode` 在 `Night` 都要是 **false**
  （`IsArrangeMode` 目前是 `MatDeployed && State != Open && State != Settling`，
  要把 `Night` 也排除，否則夜裡還能搬機台）

---

## 3. 夜晚能做什麼、不能做什麼

**可以**：自由走動（跟探索一樣，不受襯布邊界限制）、看自己的攤位、看 HUD

**不可以**：
- 剃毛（動物都睡了，見第 4 節）
- 敲鈴開張 —— `CanOpenForBusiness()` 要拒絕，理由字串寫「天黑了，今天不能再開張」
- 搬動／放置機台（`IsArrangeMode` 為 false 就自然擋掉了）
- **收攤** —— 這一批直接禁止，避免夜裡收攤造成的狀態交錯。
  `SuitcaseItem` 的收攤互動在 `Night` 回拒絕提示
- 顧客上門 —— `CustomerQueue` 已經 gate 在 `IsBusinessMode`，所以自動成立

### 3.1 白天結束時還在排隊的顧客要清掉

`EndDay()` 裡要把場上所有 `Active` 的顧客結束掉（走人、不扣錢 —— 是打烊不是失約）。
不清的話他們會在夜裡站著把耐心耗完、然後扣你錢。

---

## 4. 動物睡覺

這是「做不了事」唯一的視覺訊號，比任何 UI 都清楚，不要省。

### 4.1 `WoolNpc`

`NpcState` 末端追加 `Sleep = 3`。

- `StallManager.Instance.IsNight` 為真時進入 `Sleep`；`BeginDay()` 之後回到 `Wander`
- `Sleep` 期間：不移動、不長毛（`TickRegen` 跳過）
- `CanInteract` 仍然回 true（要能顯示提示），但 `GetPrompt` 回
  **「牠睡著了」**，`TryShear` 一律拒絕
- 視覺：停下來、身體稍微下沉（或換一個明顯的睡覺姿態），
  有動畫的話播睡覺 clip；沒有就先降低身體高度並調暗

### 4.2 `WildBeast` 也要睡

不睡的話夜裡會變成「唯一能做的事就是獵大動物」，
而且睡著又剃得到會變成無腦農場。所以大動物同樣：

- 夜晚停止移動與方向取樣
- 剃毛拒絕，提示「牠睡著了」

> 之後（第 6 步）要把大動物改成**只在夜晚出現**，那時候會把這段反過來。
> 現在先讓夜晚真的是空的。

---

## 5. 結束夜晚的互動物（下一批會變成債主）

新增一個很簡單的互動物，例如 `Scripts/Stall/NightMarker.cs`
（`NetworkInteractable`，`InteractionPriority` 跟鈴鐺同級）。

- 生成位置照 `EnsureBell()` 的做法：襯布展開就放在襯布邊上，
  沒展開就放在玩家出生點附近。**進入 `Night` 才生成，離開 `Night` 就 despawn**
- 提示字：`[左鍵] 收工，進入隔天`
- 互動 → 呼叫 `StallManager` 的一支新公開方法 `EndNight()`，
  裡面做 `Settle()`
- 只有**房主**能互動（跟開張鈴一致），其他人看到「等房主收工」

外觀用佔位幾何（一根發光的柱子之類），夠顯眼就好 —— 玩家要找得到它。

---

## 6. 夜晚的光照（讓這個階段看得出來）

純本機視覺，**不要新增任何 `[Networked]` 欄位** ——
從 `StallManager.State` 推導就好，那個值本來就同步。

新增 `Scripts/Core/DayNightLighting.cs`（`MonoBehaviour`，掛在場景燈光上或自己找）：

- 白天（`Exploring` / `Deploying` / `Open`）：現有的光照，不動
- `Night` / `Settling` / `RunOver`：太陽降到地平線附近、強度降低、
  顏色偏藍、環境光壓暗
- 切換用 `NightFadeSeconds` 平滑過渡，不要瞬間切（瞬切看起來像 bug）

做最小版本就好：一個 `Light` 的角度／強度／顏色 + `RenderSettings` 的環境光三色。
不要做天空盒、不要做星星、不要做路燈 —— 那些是之後美術的事。

---

## 7. HUD（`Stall/StallHud.cs`）

`Night` 期間：

- 白天那一行（剩餘時間／目標／已賺）換成 **「夜晚」** 加一句
  **「動物都睡了。去找發光的柱子收工。」**
- **今天的成績要看得到**：已賺多少、目標多少、達標了沒 ——
  玩家要能在收工前知道自己過不過得了
- 天數顯示維持「第 N 天」

---

## 8. 驗收

**回歸**

1. `Stall_Test` 按 Play：時間到**直接跳結算**，沒有夜晚，跟這一批之前一樣
2. `Stall_Test` 的動物不會睡覺

**羊駝村**

3. 白天時間到 → 天色變暗（平滑，不是瞬切）→ 進入夜晚
4. 夜晚可以自由走動，不受襯布邊界限制
5. **所有 `WoolNpc` 都睡了**，互動提示「牠睡著了」，剃不到
6. **大動物也睡了**，剃不到、不移動
7. 敲鈴被拒絕，提示「天黑了，今天不能再開張」
8. 夜裡搬不動機台、收不了攤
9. 白天結束時還在排隊的顧客會離開，**不會在夜裡扣錢**
10. HUD 顯示「夜晚」與今天的成績（已賺／目標／達標與否）
11. 找到發光的柱子互動 → 結算畫面跳出 → 關掉 → 隔天早上，天亮、動物醒來、時鐘從 5:00 重新跑
12. 沒達標的那天，收工之後進 `RunOver`，行為跟這一批之前一樣
13. 夜晚**沒有計時**，待多久都不會被強制推進

**連線**

14. 兩個 client 的天色、夜晚狀態、動物睡覺一致
15. 非房主看到「等房主收工」，按了沒反應
16. 房主收工，兩邊同時跳結算

---

## 9. 這一批不要做的

- **不做債主**（下一批把 `NightMarker` 換成他）
- 不做夜晚的內容（店面、自己家、道具點、夜間大動物）
- 不做天空盒、星空、路燈
- 不做升級系統
- 不調 `StallTargetBase / Growth`

---

## 10. 開工前

有不確定的先問我。特別是第 3 節那張「夜晚不能做什麼」的清單 ——
要確認 `IsArrangeMode`、`CanOpenForBusiness`、收攤這三處都擋乾淨了，
不然會出現「夜裡把攤位收起來然後隔天沒東西可擺」這種壞狀態。
