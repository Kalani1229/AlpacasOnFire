# 施工指令：把羊駝模型與動畫整合到玩家角色

> 貼給新對話即可。只整合**玩家角色**，其他動物維持佔位。

---

## 0. 紅線

**一、絕對不要 merge `feat-art` 分支。**

`feat-art` 是在 v6 之前分出去的，它**沒有**這些東西：
`Scripts/Npc/`、`MaterialCrate`、`WeavingMachine`、`TeamStash`、`StallLoadout`、
`CustomerQueue`、`Prank/`、`VillageSceneBuilder`、以及所有 v6 的文件。

merge 或 rebase 會把這幾週的工作整批還原。**只能用 `git checkout feat-art -- <路徑>` 挑檔案。**

**二、只動玩家。** 其他可以被剃毛的動物**不是羊駝**，`Npc_WoolNpc.prefab` 維持佔位幾何，
一行都不要改。

**三、不要從 `feat-art` 拿這些檔案**（拿了會覆蓋掉 v6 的工作）：

- 任何 `Assets/AlpacasOnFire/Scripts/**`
- 任何 `Assets/AlpacasOnFire/Scenes/**`
- `Assets/AlpacasOnFire/Resources/GameCatalog.asset`
- `Assets/AlpacasOnFire/Levels/**`
- `Assets/AlpacasOnFire/Prefabs/**`（包含 `Alpaca_Player.prefab`，理由見第 3 節）
- `Assets/AlpacasOnFire/Editor/**`

---

## 1. 要挑過來的檔案

```bash
git checkout feat-art -- \
  "Assets/AlpacasOnFire/Models/MD_Alpaca.fbx" \
  "Assets/AlpacasOnFire/Models/MD_Alpaca.fbx.meta" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Body.mat" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Body.mat.meta" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_DarkBody.mat" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_DarkBody.mat.meta" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Eye.mat" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Eye.mat.meta" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Fur.mat" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Fur.mat.meta" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Mouth.mat" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Mouth.mat.meta" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Pupil.mat" \
  "Assets/AlpacasOnFire/Models/M_Alpaca_Pupil.mat.meta" \
  "Assets/AlpacasOnFire/Animation/Player" \
  "Assets/AlpacasOnFire/Texture/mouth_a.png" \
  "Assets/AlpacasOnFire/Texture/mouth_a.png.meta" \
  "Assets/Plugins/JMO Assets" \
  "ProjectSettings/ToonyColorsPro.json"
```

**Toony Colors Pro 一定要一起拿** —— 羊駝的材質用的是 TCP2 的 shader
（`M_Alpaca_Fur.mat` 的 shader guid `df5bb027d94a6c44bb32b3c31ec1303f`）。
少了外掛，羊駝會整隻變洋紅色。

`.meta` 一定要跟著，否則 Unity 會重新產生 GUID，材質與 FBX 的關聯會斷掉。

拿完先開 Unity 讓它 import 一次，確認 Console 沒有 shader 相關錯誤。

---

## 2. 動畫的現況（先讀這段再動手）

`AC_Alpaca.controller` 裡有三個 state：**Idle / Run / Work**，
但 `m_AnimatorParameters: []`、每個 state 的 `m_Transitions: []`
—— **沒有參數、沒有轉場**，美術只是把 clip 放進去。

**所以不要去改那個 controller。** 用程式直接切狀態：

```csharp
_animator.CrossFade("Run", 0.15f, 0);
```

好處是不用碰 YAML、不用手拉轉場線，之後美術加新 clip 也只要多一行對應。

---

## 3. 最大的陷阱：`PlaceholderAssetBuilder` 會蓋掉美術

`Alpaca_Player.prefab` 是 `PlaceholderAssetBuilder.BuildPlayer()` 產生的
（最後一行 `SavePrefab(root, "Alpaca_Player")`）。
如果只是手動把模型拖進 prefab，**下次跑「1. 建置佔位資產」就會被膠囊蓋回去**。

**做法：改 `BuildPlayer()`，讓它在模型存在時用模型、不存在時用膠囊。**

```csharp
// BuildPlayer() 裡，原本建 Body 膠囊 + Snout 的地方改成：
const string AlpacaModelPath = "Assets/AlpacasOnFire/Models/MD_Alpaca.fbx";
var model = AssetDatabase.LoadAssetAtPath<GameObject>(AlpacaModelPath);

if (model != null)
{
    // 用美術模型
    var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
    visual.name = "Visual";
    visual.transform.SetParent(root.transform, false);

    var animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
    animator.runtimeAnimatorController =
        AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/AlpacasOnFire/Animation/Player/AC_Alpaca.controller");
    animator.applyRootMotion = false;   // 位移由 NCC 負責，不能讓動畫搶
}
else
{
    // 沒有模型就退回原本的膠囊，一行都不要改
    ...
}
```

**其餘部分全部保持原樣** —— `GarmentVisual`、`FleeceIndicator`、`ShearsVisual`、
四個 Anchor、`CharacterController`、`NetworkObject`、`NetworkCharacterController`、
`PlayerController`、`PlayerCarry`、`PlayerStallAgent`、`Prank.StaggerStatus`
以及所有 `SetRef` 的連線邏輯都不要動。

### 3.1 `_bodyRenderer` 變成多個

`PlayerController` 目前是單一 `_bodyRenderer`，擋住畫布時整隻換成 `_fadeMaterial`。
羊駝 FBX 有六個材質，可能是多個 Renderer。

**加法處理**：`PlayerController` 新增 `[SerializeField] private Renderer[] _bodyRenderers;`，
淡化邏輯改成「有 `_bodyRenderers` 就用它，否則沿用既有的 `_bodyRenderer`」。
舊欄位保留不刪，佔位版本的行為才不會變。

### 3.2 比例與軸心

FBX 的原始大小不會剛好等於 `GameTuning.AlpacaHeight = 1.8`。
**不要在程式裡硬寫縮放值**，改成在 `BuildPlayer()` 讀一個常數：

```csharp
// 這個值請人眼校到「膠囊的高度 ≈ 羊駝的高度」之後填回來
private const float AlpacaModelScale = 1.0f;
private static readonly Vector3 AlpacaModelOffset = Vector3.zero;
```

第一次跑完之後由我目視調整這兩個值，然後你再改回程式裡 —— 這樣重建才不會跑掉。

---

## 4. 新檔案：`Scripts/Player/PlayerAnimator.cs`

掛在玩家 prefab 根物件上，負責挑動畫。

### 4.1 最重要的一條：用同步狀態驅動，不要用輸入驅動

動畫如果讀 `LocalInputProvider`，**你只會看到自己在跑，隊友全部在原地滑行**。
一定要讀 `NetworkCharacterController.Velocity`（那是同步的），
而且在 `Render()` 裡做，不是 `FixedUpdateNetwork()`。

### 4.2 狀態判定

```
Work   —— WorkTimer 還在跑（優先權最高）
Run    —— 水平速度 > AnimRunThreshold
Idle   —— 其他
```

- 水平速度 = `new Vector2(Velocity.x, Velocity.z).magnitude`
- `AnimRunThreshold` 放 `GameTuning`，建議 `0.6f`
- 狀態沒變就不要重複 `CrossFade`，否則動畫會卡在第一幀

### 4.3 Work 的觸發

`PlayerController` 加一個同步計時器，讓所有人都看得到：

```csharp
[Networked] public TickTimer WorkTimer { get; set; }

/// <summary>做了一個「動手」的動作，播 Work 動畫。只在 StateAuthority 呼叫。</summary>
public void TriggerWork(float seconds = 0.45f)
    => WorkTimer = TickTimer.CreateFromSeconds(Runner, seconds);
```

在這些地方呼叫 `TriggerWork()`：

- 剃毛成功（`WoolNpc` 被剃、或隊友被剃）
- 把東西放進機台 / 取出成品
- 交貨
- 從素材箱拿毛

**不要**在撿東西、丟東西、走路時呼叫 —— Work 是「在工作」，不是「有動作」。

現有的 `_shearsVisual`（剃毛時亮 0.35 秒）維持不動，兩者可以並存。

---

## 5. Prank 狀態的動畫先不做

`Prank.StaggerStatus` 的暈眩、擊退目前沒有對應的 clip。
這一批**不處理** —— 暈倒時就讓它繼續播 Idle。
等美術做出 `A_Alpaca_Stagger` 再加，到時候只是多一行判定。

---

## 6. 驗收

**Claude 做完之後要能回答的**

1. `git status` 只有第 1 節列出的檔案 + `PlaceholderAssetBuilder.cs`
   + `PlayerController.cs` + 新的 `PlayerAnimator.cs`，沒有別的
2. 專案編譯通過，Console 沒有 shader / missing script 錯誤
3. `Npc_WoolNpc.prefab` 完全沒被修改（`git diff` 是空的）
4. 跑一次「1. 建置佔位資產」，`Alpaca_Player.prefab` 裡是羊駝模型不是膠囊
5. 再跑一次同一個選單，模型**沒有**被膠囊蓋掉

**要人眼確認的（Claude 做不到，會交接給我）**

6. 羊駝的大小跟原本的膠囊差不多（高度約 1.8）
7. 羊駝面向 +Z，跟移動方向一致，沒有躺著或倒立
8. 站著播 Idle、走動播 Run、剃毛播 Work
9. 跑步沒有明顯滑步（若有，調 `AnimRunThreshold` 或請美術改 clip 速度）
10. 手上拿東西時物品出現在 `HandAnchor`，位置沒有埋進身體
11. 擋住畫布時整隻會變半透明（`_bodyRenderers` 有接對）

**連線**

12. 兩個 client：**對方**的羊駝會跑、會做 Work 動作，不是原地滑行
13. client 端剃毛時，主機端看得到那隻羊駝播 Work

---

## 7. 開工前

先做第 1 節（挑檔案）並讓 Unity import 一次，**把 Console 的結果貼給我**再繼續。
如果 shader 或 FBX 有問題，後面全部白做。

第 3.2 節的比例與軸心、第 6 節第 6–11 項都需要我目視確認 —— 做到那裡先停下來問我。
