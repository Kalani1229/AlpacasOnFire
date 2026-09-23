# 施工指令：紡線機（羊毛 → 絲線）

> 貼給新對話即可。生產鏈從兩段變三段：**素材箱 → 紡線機 → 織布機 → 交貨窗口**。

---

## 0. 紅線

- `Stall_Test` 與 Classic loadout 不受影響：`StallLoadout.Classic` 一行都不要改
- 不要動 `MachineBase.cs`（紡線機是獨立元件，不繼承它，理由見第 4 節）
- 不要刪除或重排既有列舉值，新增一律往後接
- 不要改既有按鍵的行為

---

## 1. 這台機器特別在哪

**它是第一台「人必須待在原地」的機器。** 其他機台全是射後不理 ——
放進去、走開、它自己做完。紡線是反過來的：**有人被釘在那裡**。

三個由此而來的設計要求，實作時不要簡化掉：

- **進度保留。** 放開按鍵或走開，進度停在原地不歸零、不衰退
- **可以接手。** A 紡到一半被叫走，B 走過去按住就能接著紡完。
  進度存在機台上、不存在玩家身上，所以這個自然成立 —— 但**進度必須是 `[Networked]`**，
  否則接手的人看到的條是錯的
- **一次一份。** 放一份毛、按完、拿走、再放一份

### 1.1 核心原則：**投料可以遠距，操作必須親臨**

羊毛**丟得進去**（跟其他機台一樣），但**丟進去不會讓它開始動**。
機器要有人真的走過去按住才會轉。

這條規則長出來的分工正是這台機器存在的理由：

```
玩家 A —— 站在紡線機前按住不放，專心紡
玩家 B —— 在素材箱那邊一份一份把羊毛丟過來
```

A 全程不用離開，B 也不用走到機台前。**兩個人各自待在自己的位置，靠丟接串起來** ——
這是你的丟接系統第一次有「持續性」的用途，而不只是偶爾省一趟腳程。

---

## 2. 資料層（加法）

### `Core/GameEnums.cs`

```csharp
// ItemKind 末端追加
Thread = 14,        // 絲線：紡線機的產出，織布機唯一的原料

// LevelElementType 末端追加
SpinningMachine = 29,
```

### `Core/GameTuning.cs`（檔案最末端新區塊）

```csharp
// ---------- 紡線機 ----------
public const float SpinSeconds      = 3.5f;  // 按住多久紡完一份
public const float SpinHoldRange    = 2.5f;  // 超過這個距離就中斷（＝ InteractRange）
```

**3.5 秒是刻意比織布的 7 秒短的。** 主動按著等比放著不管難熬得多，
體感上按住 3.5 秒 ≈ 放著等 7 秒。兩個都設 7 秒的話紡線會非常煩。

---

## 3. 新介面：`Interaction/IInteractable.cs` 加一個

目前沒有任何「按住」打到世界物件的路徑 —— `IHoldTool` 是給手上的道具用的，
`PlayerController` 對互動物只送「按下的那一刻」。要新增：

```csharp
/// <summary>
/// 可以「按住左鍵持續操作」的互動物（紡線機）。
/// 跟 IHoldTool 不同：那個是手上的道具，這個是世界上的機台。
/// </summary>
public interface IHoldInteractable
{
    bool CanHold(in InteractionContext ctx);
    string GetHoldPrompt(in InteractionContext ctx);
    /// <summary>按住的每一個 tick 呼叫。只在 StateAuthority。</summary>
    void HoldTick(in InteractionContext ctx, float deltaTime);
}
```

### 3.1 `PlayerController` 的接線

在既有的「左鍵／Space：對前面的目標做事」那一段**後面**加，不要改既有的分支：

```csharp
// ---- 按住左鍵：持續操作前面的機台（紡線機）----
// 手上的東西把左鍵拿去蓄力時（卡車）整個讓開，跟上面的單擊分支一致。
if (!chargeOnPrimary && input.Buttons.IsSet(GameButton.Interact))
{
    var target = _interactor.FindTarget(out var holdCtx);
    if (target is IHoldInteractable hold && hold.CanHold(in holdCtx))
        hold.HoldTick(in holdCtx, Runner.DeltaTime);
}
```

**每個 tick 重新找目標**，不要記住上一個 —— 玩家走開或轉頭就自動中斷，
而進度留在機台上，不需要另外寫中斷處理。

### 3.2 HUD

`GameHud` 的準心提示：目標是 `IHoldInteractable` 且 `CanHold` 為真時，
顯示 `GetHoldPrompt()` 而不是 `GetPrompt()`。

---

## 4. 新檔案 `Scripts/Machines/SpinningMachine.cs`

繼承 `NetworkInteractable`，**不要繼承 `MachineBase`** ——
`MachineBase` 的進度是 `TickTimer` 自動倒數，紡線是「按住才前進」，
兩種模型湊在一起會很難看懂。

```csharp
public class SpinningMachine : NetworkInteractable, IHoldInteractable, IThrownItemReceiver
{
    [SerializeField] private Transform _progressBar;
    [SerializeField] private Renderer _statusLight;
    [SerializeField] private Renderer _bodyRenderer;
    [SerializeField] private Renderer _woolSlot;      // 裡面那份毛，顯示顏色

    [Networked] public NetworkBool HasInput { get; set; }
    [Networked] public int InputColorRaw { get; set; }
    [Networked] public float Progress { get; set; }   // 0 ~ SpinSeconds
    [Networked] public NetworkBool HasOutput { get; set; }
    [Networked] public int OutputColorRaw { get; set; }

    // 待料槽：紡線中也能先把下一份丟進來排隊（見 4.1.1）
    [Networked] public NetworkBool HasQueued { get; set; }
    [Networked] public int QueuedColorRaw { get; set; }

    public float Progress01 => Mathf.Clamp01(Progress / GameTuning.SpinSeconds);
}
```

### 4.1 互動規則

| 狀況 | 左鍵單擊 | 左鍵按住 | 丟羊毛過來 |
|---|---|---|---|
| 空的、手上有羊毛 | 放入羊毛 | — | 裝進原料槽 |
| 有毛、還沒紡完 | — | **紡線（進度前進）** | 裝進待料槽 |
| 原料槽 + 待料槽都滿 | 提示「裝滿了」 | 紡線 | 拒絕 |
| 紡完了（`HasOutput`） | 空手取走絲線 | — | 照樣可以裝 |

**注意最後一列**：成品還沒被取走也**不應該擋住裝料** ——
負責丟的人看不到機台的細節狀態，擋住他會讓補料變成猜謎。
只有「原料槽 + 待料槽都滿」才拒絕。

- `HoldTick`：`Progress += deltaTime`，到 `SpinSeconds` 就 `HasOutput = true`、
  `OutputColorRaw = InputColorRaw`、`HasInput = false`、`Progress = 0`，
  然後如果待料槽有東西就**自動遞補**成下一份原料
- **丟進來不會讓它開始動。** `AcceptThrown` 只負責裝料，`Progress` 一步都不前進

### 4.1.1 待料槽（讓 1.1 的分工真的成立）

沒有待料槽的話，負責丟的人必須抓準「剛紡完」那一瞬間才丟得進去 ——
那等於逼他站在旁邊等，1.1 的分工就白做了。所以留**一格**待料：

```
丟進來（或手放進來）：
  !HasInput            -> 直接進原料槽
  HasInput && !HasQueued -> 進待料槽
  兩個都滿             -> 拒絕，提示「裝滿了」

紡完一份：
  產出絲線 -> HasInput = false -> 待料槽有東西就遞補上來
```

仍然是**一次紡一份**，待料槽只是讓補料可以提前，不會加快紡線速度。
手放進去和丟進來走同一段邏輯。

### 4.2 **不要**自動出貨到輸送帶

其他機台旁邊有輸送帶會自動出貨，**紡線機不要做這件事**。

理由：預設佈局裡的輸送帶是往交貨窗口送的，紡線機一旦自動出貨，
絲線會被送去交貨窗口而不是織布機 —— 玩家會完全看不懂發生什麼事。
而且你紡線時本來就站在機台前，順手拿走的成本是零。

絲線本身仍然是普通的 `CarriableItem`，**可以用手丟到輸送帶上**，那條路不受影響。

### 4.3 外觀

- 進度條沿 X 縮放，只在 `HasInput` 時顯示
- `_woolSlot` 顯示目前那份毛的顏色，空的時候關掉
- **待料槽要另外有一個看得見的位置**（機台側邊一顆小球），
  負責丟的人要能從遠處看出「還有沒有空位」—— 他不會走過來讀提示字
- 狀態燈：空（綠）→ 有毛待紡（黃）→ 紡好待取（絲線的顏色）
- **正在被紡的時候要看得出來**（機體輕微旋轉或閃爍），
  不然隊友看不出「那台正在被人操作」

---

## 5. `WeavingMachine` 改成只吃絲線

把這五處的 `ItemKind.Wool` 換成 `ItemKind.Thread`：

- `CanInteract`（第 106 行附近）
- `GetPrompt`（第 117 行附近）
- `Interact`（第 151 行附近）
- `CanAcceptThrown`（第 210 行附近）
- 提示字裡的「毛」改成「線」

`WoolCount` / `MainColorRaw` / `AccentColorRaw` 的邏輯完全不動 ——
主色／點綴色、放入順序有差、織製中可以補第二份，全部照舊。

---

## 6. 資產與場景

### `PlaceholderAssetBuilder`

末端追加兩項（既有段落一行都不要動）：

- `Item_Thread`：絲線。外型做成**線軸（細長圓柱）**，跟羊毛的球體一眼分得出來。
  顏色沿用 `PlaceholderPalette.Dye()`
- `Machine_SpinningMachine`：掛 `NetworkObject`、`SpinningMachine`、`DeployableDevice`

### `StallLoadout.Village`

- `Devices` 加一台 `SpinningMachine`
- `DefaultLayout` 把它排在**織布機的上游**（z 比織布機小一格），
  讓動線由南往北讀成：素材箱 → 紡線機 → 織布機 → 輸送帶 → 交貨窗口
- **不要**讓它正交相鄰於任何一條現有輸送帶（雖然 4.2 已經關掉自動出貨，
  但排在旁邊會讓玩家誤以為它會自己送）
- 放好之後 `ValidateStallSetup` 的佈局衝突檢查必須通過

`StallLoadout.Classic` 不要動。

### 除錯鍵

`DebugItemSpawner` 加一個生絲線的鍵（例如 `9`），
這樣測織布機時不用每次都先跑一趟紡線。顏色用紅色。

---

## 7. 驗收

**回歸**

1. `Stall_Test` 按 Play 跟這一批之前完全一樣（Classic loadout 沒有紡線機）
2. 其他機台的單擊互動行為沒有任何改變
3. 拿著卡車按住左鍵仍然是蓄力，不會誤觸紡線

**紡線機**

4. 手上有羊毛 → 對紡線機按一下 → 毛進去，機台顯示那個顏色
5. 空手按住左鍵 → 進度條前進，約 3.5 秒完成
6. **放開按鍵 → 進度停住不歸零**；再按住 → 從原處接著跑
7. **走開再回來 → 進度還在**
8. **隊友走過去按住 → 接著紡完**（這是這一批最重要的一條）
9. 紡好後空手按一下取走絲線，絲線是線軸外型、顏色正確
10. 成品沒取走時不能放新的毛，提示字要說清楚
11. 把羊毛**丟**進紡線機也算數
12. **丟進去之後機器不會自己動** —— 進度是 0，要有人走過去按住才開始
13. 紡線中把第二份羊毛丟進來 → 進待料槽，側邊的小球亮起來
14. 紡完一份之後待料槽自動遞補，不用再丟一次
15. 原料槽 + 待料槽都滿時才拒絕；成品沒取走**不會**擋住裝料
16. **兩人分工跑一次**：A 站著按住不放、B 在素材箱那邊一直丟，
    A 全程不用離開機台
17. 紡線機旁邊擺一條輸送帶，絲線**不會**自動跑上去

**織布機**

18. 拿生羊毛對織布機 → 被拒絕，提示說需要絲線
19. 兩份絲線 → 主色 + 點綴色照舊，交換順序會得到不同的衣服
20. 織製中丟第二份絲線進去仍然有效

**連線**

21. 兩個 client：進度條、原料槽顏色、待料槽、正在被操作的表現兩端一致
22. client 端按住也能紡（判定是用互動的那個玩家，不是主機玩家）
23. 一邊紡到一半、另一邊接手，進度連續不跳動
24. **一邊站著紡、另一邊丟料**，兩端看到的待料槽狀態一致

---

## 8. 這一批不要做的

- **不要動 `StallTargetBase`。** 產能會下降，門檻確實需要調，
  但要等我玩過拿到實際數字，現在改是猜的
- 不做單人難度補償（之後可能會加，但先讓它難）
- 不做紡線的音效循環（有 `SfxId.MachineStart` 就先用）

---

## 9. 開工前

有不確定的先問我。特別是第 3.1 節的按住接線 ——
那是整個專案第一次有「按住打到世界物件」的路徑，
要確認它不會跟卡車蓄力、接住、以及單擊互動互相打架。
