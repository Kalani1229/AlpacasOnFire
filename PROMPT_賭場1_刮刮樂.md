# 施工指令：遜咖賭場（一）—— 小遊戲框架 + 刮刮樂

> 貼給新對話即可。這一批同時建立**之後所有小遊戲都會用到的框架**，
> 以及 `Capital` 的第一條扣款路徑（升級系統之後也會用同一條）。

---

## 0. 紅線

- 只在 `_runMode == true` 的羊駝村生效，`Stall_Test` 完全不受影響
- **刮刮樂只在夜晚能玩**（`StallState.Night`）
- **亂數一定在狀態權威端跑。** client 只送「我要買哪幾張」，絕對不要讓 client 回報分數
- 不做美術（全部佔位），不做彩帶
- 不要改 `StallManager` 的狀態機、不要改大哥與床

---

## 1. 三層框架（之後的烏龜賽跑、夾娃娃機都會繼承）

| 層 | 檔案 | 性質 |
|---|---|---|
| 機台 | `Scripts/Casino/MinigameMachine.cs` | `NetworkInteractable`，有 `[Networked] NetworkId Occupant` 佔用鎖 |
| 面板基底 | `Scripts/Casino/MinigamePanel.cs` | **純本機** UI：開關、鎖游標、Esc、擋輸入 |
| 各遊戲 | `Scripts/Casino/ScratchCardPanel.cs` 等 | 繼承面板基底 |

照 `Stall/SuitcaseColorPanel.cs` 的模式做 —— 那支已經驗證過
「只在觸發互動的那個 client 打開、解鎖游標、結果走 RPC 回報」。

### 1.1 Esc 的優先序

`PauseMenu.Update()` 已經有一串守衛（`ResultsScreen` / `StallResultsPanel` /
`SuitcaseColorPanel` 開著時就 return）。**把小遊戲面板加進那串守衛**，
這樣 Esc 只關面板、不會同時跳出暫停選單。

### 1.2 佔用鎖

一次只能一個人玩。別人走過去時次要提示寫「有人在玩」。
玩家離開面板、斷線、或進入白天時都要釋放。

---

## 2. `Capital` 的扣款路徑（新增，之後會重用）

`StallManager.Capital` 目前只進不出。新增：

```csharp
/// <summary>花錢。不夠就整筆失敗並回傳 false。只在 StateAuthority 呼叫。</summary>
public bool TrySpendCapital(int amount, string reason);

/// <summary>收錢（獎金）。只在 StateAuthority 呼叫。</summary>
public void AddCapital(int amount, string reason);
```

**注意這會直接影響生死** —— 大哥查的就是 `Capital`，所以賭輸了可能當晚就過不了關。
這是刻意的。

---

## 3. `Scripts/Casino/CasinoState.cs`（新增，掛在 `[GameSystems]`）

每日共用狀態要有個不會被夜晚生滅影響的家，所以放在 `[GameSystems]` 上，
不要放在櫃檯上（櫃檯跟著夜晚生滅，狀態會丟）。

```csharp
public class CasinoState : NetworkBehaviour
{
    public static CasinoState Instance { get; private set; }

    public const int ScratchCardCount = 10;

    /// <summary>今天這一副牌，哪幾張已經被買走了。全隊共用。</summary>
    [Networked, Capacity(ScratchCardCount)]
    public NetworkArray<NetworkBool> CardTaken { get; }

    public int CardsLeft { get; }        // 走訪算出來就好
    public void ResetDay();              // 全部設回 false
}
```

`StallManager.BeginDay()` 裡呼叫 `CasinoState.Instance?.ResetDay()`。
**漏掉的話隔天會沒有牌可以買。**

---

## 4. 櫃檯

`Scripts/Casino/ScratchCardCounter.cs`，繼承 `MinigameMachine`。

- **位置**：出生廣場、床的旁邊（跟床一樣從 `SpawnPointRegistry` 的第一個出生點算）。
  賭場建築之後再做，這一批只要一個佔位櫃檯
- **只在 `StallState.Night` 存在**：跟 `Bed` 一樣，本機物件、跟著同步的 `State` 生滅
- **右鍵進入**（`ISecondaryInteractable`）—— 跟手提箱選色面板同一套規則：
  左鍵是即時動作、右鍵是開面板
- 左鍵要給提示而不是靜默無反應：`[右鍵] 玩刮刮樂（剩 7 張）`
- 櫃檯上要寫 **「頭獎 500」**。不要印完整賠率表
- 外觀佔位：一個矮櫃加一塊招牌就好

---

## 5. 獎項表

一張 **10 元**。放在 `GameTuning` 或 `CasinoState` 的常數裡，用**倍率**不要寫死金額。

| 倍率 | 機率 | 10 元時 |
|---|---|---|
| 0（沒中） | 67% | 0 |
| 1（回本） | 20% | 10 |
| 2 | 9% | 20 |
| 5 | 3% | 50 |
| 20 | 0.9% | 200 |
| **50（頭獎）** | **0.1%** | **500** |

期望值 0.76 —— **這是刻意的負期望值**，賭場是絕望閥不是收入來源。不要調成正的。

---

## 6. 購買流程

```
client：選好要買哪幾張（bitmask）→ RPC 給狀態權威
主機：  驗證（不是已被買走的、`Capital` 夠不夠）
        TrySpendCapital(張數 × 10)
        一次抽完所有張的結果
        標記 CardTaken
        AddCapital(總獎金)              ← 立刻入帳
        把結果一次送回給**買的那個玩家**
client：開始播刮開的演出
```

**兩個重點：**

**錢在主機抽完的那一刻就全部結清。** 刮開的動畫純粹是演出，不承擔經濟責任 ——
斷線、關面板、崩潰都不會吃掉獎金。

**結果一次送回，不要每張一次 RPC。** 用 `INetworkStruct` 包十張、或把
10 × 3 bits 打包進一個 int 都可以，選你有把握的那個做法。

買不起的張數要在面板上**灰掉**，不要讓玩家選了才說錢不夠。

---

## 7. 面板

### 7.1 不要全螢幕

面板佔畫面約八成，周圍把 3D 世界**壓暗但看得見**。
全螢幕會讓玩家「離開」世界，在合作遊戲裡那等於消失。

### 7.2 流程

1. 十張**有編號**的卡攤開（`HorizontalLayoutGroup`）。已被買走的顯示成空位
2. 點選要買的（可多選），顯示總價。按「購買」
3. **沒買的那幾張往上飄走並淡出**（`Lerp` 位置 + alpha，不需要 Timeline）
4. 買到的疊在**右邊**待刮
5. 刮完一張 → 立刻顯示該張結果 → 下一張**從右邊疊到中間**
6. 全部刮完 → 顯示總結（花了多少、拿回多少）

### 7.3 刮開的互動

- **拖曳滑鼠刮開**，刮到 **40%** 就自動全部揭曉
- 技術上沿用 `GarmentPaintSurface` 那套：把筆觸畫進 `Texture2D`、
  餵給卡片上層 `Image` 的材質，刮到的地方 alpha 設 0。
  **2D 版更簡單** —— 不用同步、不用 UV 投影
- 一顆 **「自動刮開」** 按鈕：自動一張一張快速刮完，保留每張的結果演出
  （之後有彩帶時也會播）。**不要做「全部刮開」**
- Esc / 關閉：**把還沒刮的直接揭曉**，不要讓玩家以為獎金沒了

### 7.4 資本額顯示要凍結

錢是在購買那一刻就入帳的，所以 HUD 的資本額**會在玩家刮開之前就跳上去** —— 直接劇透。

**面板開著時凍結 `StallHud` 的資本額顯示**（顯示打開前的數字），關閉時才更新成真實值。
**只改顯示，不要改入帳邏輯。**

---

## 8. 中大獎要廣播全隊

20 倍以上時發一則 `StallManager.LocalNotice`：

```
阿明刮中 200！
阿明刮中頭獎 500！！
```

這是這台機器唯一的社交價值，而成本接近零。

---

## 9. 驗收

**回歸**

1. `Stall_Test` 完全不變，沒有櫃檯
2. 白天看不到櫃檯，進不去刮刮樂

**單人**

3. 夜晚在床旁邊看得到櫃檯，上面寫「頭獎 500」
4. 左鍵有提示「[右鍵] 玩刮刮樂（剩 10 張）」，右鍵才開面板
5. 面板不是全螢幕，後面看得到壓暗的世界
6. 十張有編號，可多選，顯示總價
7. 錢不夠的張數是灰的，選不了
8. 購買後沒買的往上飄走，買到的疊在右邊
9. 拖曳刮開，刮到約 40% 自動揭曉
10. 「自動刮開」會一張一張快速刮完
11. **面板開著時 HUD 的資本額不會變**，關閉後才跳到正確值
12. Esc 只關面板，**不會跳出暫停選單**
13. 還沒刮完就 Esc → 剩下的直接揭曉，獎金沒有消失
14. 中 20 倍以上會跳全隊通知
15. 隔天櫃檯又是十張（`ResetDay` 有被呼叫）

**多人**

16. A 買掉 3 張，B 走過去只看到剩 7 張（**共用一副牌**）
17. A 在玩的時候，B 的次要提示寫「有人在玩」
18. A 中了獎，B 看得到通知
19. A 斷線 → 佔用鎖釋放，B 可以玩

**不能壞掉的**

20. 賭輸到 `Capital` 低於今晚的目標 → 天黑時照樣判定失敗（這是刻意的）
21. 沒有任何地方由 client 決定獎金

---

## 10. 這一批不要做的

- 不做美術（等跟美術討論完）
- **不做彩帶** —— 之後再加，介面要先留好掛載點
- 不做烏龜賽跑、夾娃娃機
- 不做賭場建築（櫃檯先借出生廣場）
- 不做二、三號房
- 不調獎項機率

---

## 11. 開工前

有不確定的先問我。特別是第 6 節 ——
「主機抽完立刻結清、動畫只是演出」是這個設計的關鍵，
如果改成每刮一張才入帳，斷線就會吃掉玩家的獎金。
