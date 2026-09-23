# 施工指令：大動物（批 1 — 逃跑規則與領域）

> 貼給新對話即可。這一批只驗證一件事：
> **「牠背對最近的玩家逃跑」這條規則，兩個人夾得到嗎？**
> 道具互動、暈眩、搬運全部留給批 2。

---

## 0. 紅線

- **不要修改 `Npc/WoolNpc.cs`**。大動物是**獨立的新元件**，不是 WoolNpc 的模式
- **大動物不可以進 `WoolNpc.All`** —— `CustomerQueue` 從那份清單抽顧客，
  大動物被抽去當顧客會很荒謬
- **不要實作 `IStaggerable`**。這一批道具對牠**完全無效**，這是刻意的：
  先確認光用身體圍堵好不好玩，好玩的話道具只會更好玩；不好玩的話道具也救不了
- 不要動 `Customer.cs`、`CustomerQueue.cs`、`StallManager`
- 不要改 `GameTuning` 的既有數值

---

## 1. 核心規則

> **牠永遠往「離最近的玩家最遠」的方向跑。**

這一條會自己做出難度曲線，不用寫任何難度設定：

- **一個人** → 牠永遠背對你，而且比你快。**抓不到是正確的**
- **兩個人** → 站兩側，牠往 A 跑就被 B 逼回來，可以慢慢夾
- **四個人** → 切斷退路，很快逼到角落

---

## 2. `Core/GameTuning.cs`

檔案最末端加一個區塊，既有數值一個都不要動：

```csharp
// ---------- 大動物（批 1）----------
public const int   BeastFleece          = 9;     // 身上的毛，一次全掉
public const float BeastGrazeSpeed      = 1.2f;  // 吃草閒晃
public const float BeastFleeSpeed       = 6.5f;  // 逃跑（玩家是 5.0，追不上是刻意的）
public const float BeastStareRadius     = 12f;   // 進到這裡牠會停下來盯著你
public const float BeastAlertRadius     = 10f;   // 進到這裡牠開始逃
public const float BeastTerritoryRadius = 40f;   // 不會離開領域
public const float BeastShearRange      = 2.5f;  // 剃毛距離（＝ InteractRange）
public const float BeastFleeMinSeconds  = 1.5f;  // 至少逃這麼久，不會你一退牠就停
public const float BeastArriveThreshold = 1.2f;
public const float BeastGravity         = 20f;

// 逃跑方向的取樣
public const int   BeastDirectionSamples = 16;   // 繞一圈取幾個方向
public const float BeastLookaheadSeconds = 1.0f; // 評分時往前推算多久
public const float BeastObstacleProbe     = 3.5f;// 方向上多近有障礙就淘汰
```

---

## 3. 新檔案 `Scripts/Npc/WildBeast.cs`

`NetworkBehaviour, IInteractable`。
**移動一定要用 `NetworkCharacterController`**，跟玩家和 WoolNpc 一樣 ——
裸 `CharacterController` + `NetworkTransform` 撐不過 Fusion 的重模擬，
用戶端會在幾秒後開始抖動並瞬間大步移動。這個坑已經踩過兩次了。

```csharp
[RequireComponent(typeof(NetworkCharacterController))]
public class WildBeast : NetworkBehaviour, IInteractable
{
    public static readonly List<WildBeast> All = new();   // 跟 WoolNpc.All 分開！

    private enum BeastState : byte { Graze = 0, Alert = 1, Flee = 2, Return = 3 }

    [SerializeField] private DyeColorType _woolColor = DyeColorType.Red;
    [SerializeField] private Transform _interactionAnchor;
    [SerializeField] private Renderer _bodyRenderer;
    [SerializeField] private Renderer[] _fleeceTufts;     // 有幾份毛就顯示幾撮

    [Networked] public int Fleece { get; set; }
    [Networked] public int StateRaw { get; set; }
    [Networked] public Vector3 HomePoint { get; set; }
    [Networked] public Vector3 TargetPoint { get; set; }
    [Networked] public Vector3 MoveDirection { get; set; }
    [Networked] private TickTimer FleeMinTimer { get; set; }
}
```

### 3.1 狀態機

```
Graze  —— 在領域內慢慢走向隨機目標點
   │ 最近的玩家 < StareRadius(12)
   ▼
Alert  —— 停下來、轉身面向那個玩家、不移動
   │ 最近的玩家 < AlertRadius(10)          │ 玩家退出 12m
   ▼                                       └──→ 回 Graze
Flee   —— 往「離最近玩家最遠」的方向全速跑
   │ 沒有玩家在 12m 內 且 FleeMinTimer 到期
   ▼
Graze（若還在領域內）或 Return（若跑出領域）

Return —— 走回 HomePoint，路上不理會玩家
```

### 3.2 逃跑方向：用取樣，不要用「直接背對」

直接取反方向會在領域邊界和牆壁上卡死。改成每次重算時取樣：

```
對 BeastDirectionSamples(16) 個方向各做一次評分：
  1. 從目前位置沿該方向推進 BeastFleeSpeed × BeastLookaheadSeconds 得到預測點
  2. 預測點超出領域（離 HomePoint > BeastTerritoryRadius）→ 直接淘汰
  3. 該方向 BeastObstacleProbe(3.5m) 內有障礙（Physics.Raycast）→ 直接淘汰
  4. 分數 = 預測點到「最近玩家」的距離
     再加上一個小獎勵：分數 += 0.3 × 預測點到「第二近玩家」的距離
  5. 取分數最高的方向
  6. 全部被淘汰 → 保持目前方向（牠被逼到角落了，這正是我們要的）
```

**第 4 步的「第二近玩家」獎勵很重要** —— 少了它，牠會直直撞進第二個玩家懷裡，
夾擊變得太容易。加了之後兩個人必須真的站對位置。

**第 6 步是這整個設計的高潮** —— 所有方向都被封死時牠會原地打轉，
那就是玩家靠近的窗口。不要寫成「隨機亂跑」，卡住就是卡住。

重算頻率：不要每個 tick 重算，會抖。每 **0.25 秒**重算一次，中間沿用上次的方向。

### 3.3 剃毛（批 1 的收網方式）

這一批沒有暈眩，所以**靠近到 2.5 公尺內就能剃**，而且**一次全掉**。

```csharp
public bool CanInteract(in InteractionContext ctx)
    => Fleece > 0 && ctx.HeldKind == ItemKind.Shears;

public string GetPrompt(in InteractionContext ctx)
    => Fleece > 0 ? $"[左鍵] 剃毛（{Fleece} 份一次全掉）" : "牠身上的毛剃光了";
```

執行時：

- `Fleece` 份羊毛**生成在地上**（不是直接進背包），繞著牠散開約 1.5–2.5 公尺
  —— 用 `ItemFactory.Spawn`，跟撞暈掉毛是同一個規則
- `Fleece = 0`
- 進入 `Flee`，`FleeMinTimer` 設長一點（5 秒），讓牠真的跑掉
- **這一批毛不會長回來**（批 2 再處理再生）

### 3.4 外觀與可讀性

**`Alert` 狀態的表現是這一批最重要的美術需求。** 玩家看不到 10 公尺的圈，
所以牠必須自己講：

- 停止移動
- **轉身正面對著那個玩家**
- 身體換一個明顯的顏色（佔位階段用 `MaterialPropertyBlock` 換色就好）

玩家會靠這個學會「再一步就會跑」的距離感，而那個學習過程本身就是玩法。
不要用 UI 畫警戒圈。

`_fleeceTufts` 照 `WoolNpc` 的做法：有幾份毛就顯示幾撮，剃光全關。

---

## 4. 佔位資產

`PlaceholderAssetBuilder` 末端追加 `Npc_WildBeast` 的佔位 prefab：

- 體型是 `WoolNpc` 的**兩倍**（之後隘口的尺寸要靠這個算）
- 掛 `NetworkObject`、`NetworkCharacterController`、`WildBeast`
- **不要**掛 `Customer`、不要掛 `DeployableDevice`
- `LevelElementType` 末端追加 `WildBeast = 27`

做完提醒我跑 `Tools > Fusion > Rebuild Prefab Table`。

---

## 5. 放進場景

`VillageSceneBuilder.SpawnNpcs()` 之後加一支 `SpawnBeasts()`：

- **只放一隻**，位置 `(0, 0, 75)`（北谷），`HomePoint` 設同一點
- 顏色用 `DyeColorType.Red`（最貴的毛配最難抓的動物）
- 放在獨立的根物件 `[Beasts]` 底下，不要混進 `[Npcs]` 或 `[Level]`

`3. 檢查羊駝村設置` 加一項：場上剛好一隻 `WildBeast`，而且它**不在** `WoolNpc.All` 裡。

---

## 6. 驗收

**單人（應該要抓不到）**

1. 遠遠看得到牠在北谷吃草
2. 走近到約 12 公尺，牠停下來、轉身盯著你、換色
3. 再往前，牠開始跑，而且你追不上
4. **一個人不管怎麼繞都剃不到** —— 這是通過，不是失敗
5. 牠不會跑出北谷（離 `HomePoint` 超過 40 公尺就折返）
6. 牠不會卡在牆裡、不會抖動、不會穿牆

**雙人（核心測試）**

7. 兩個人從兩側包抄，**夾得到**
8. 夾的過程中會出現「牠被逼到角落原地打轉」的瞬間
9. 剃下去 9 份紅毛散落一地，可以一份一份撿起來進共用背包
10. 剃完牠會真的跑掉，毛不會長回來

**連線**

11. 兩個 client 看到的牠位置一致、狀態（吃草／盯著／逃跑）一致
12. client 端靠近也會觸發 Alert（判定是用「最近的玩家」不是「主機玩家」）
13. client 端剃得到，毛在兩邊都看得到

---

## 7. 做完要回報的

這一批的重點不是功能對不對，是**手感**。請回報：

- 兩個人夾一次大概要多久？
- 有沒有出現「明明圍住了卻從縫隙溜走」的挫折感？
- 12 公尺的盯著提示，實際玩起來看得出來嗎？

這三個答案會決定批 2 的道具要怎麼調。

---

## 8. 開工前

有不確定的先問我。特別是第 3.2 節的方向取樣 ——
那是整個設計的核心，寫成「直接取反方向」會完全不好玩。
