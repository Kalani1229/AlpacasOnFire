# 施工指令：卡車爆擊的 Active Ragdoll 倒地

> 貼給新對話即可。只做**玩家**、只由**卡車爆炸**觸發。
> 大蔥的小擊維持現況，不進 ragdoll。

---

## 0. 紅線（這一節最重要）

**一、Ragdoll 是純本機視覺，絕對不進網路狀態。**

理由是硬的：Unity 的 PhysX 跨機器不是決定性的，而 Fusion 會**重模擬**過去的 tick。
十幾個關節每重跑一次就多一點誤差，幾秒內各端就會各自飛走。

所以：

- Ragdoll 的更新全部放在 `Render()`，**不要碰 `FixedUpdateNetwork()`**
- 不新增任何 `[Networked]` 欄位
- 各端自己跑自己的，姿勢略有差異**完全沒關係** —— 沒有人看得出來，也不影響任何判定

**二、位置的權威留給 `NetworkCharacterController`。**
膠囊照舊由 NCC 控制（`StaggerStatus` 的擊退已經在做了），ragdoll 只是掛在上面演。
**不要**讓骨盆去推膠囊。

**三、不要改 `StaggerStatus` 的網路欄位、不要改 `TruckTool`、不要改 `GameTuning` 既有數值。**

**四、這一批只做玩家。** `WoolNpc` / `WildBeast` 是佔位幾何、沒有骨架，之後有模型再說。

---

## 1. 觸發點

`TruckTool.Explode()` 目前對範圍內每個 `IStaggerable` 呼叫
`ApplyKnockback(...)` + `ApplyStagger(TruckStaggerSeconds, dropWool: true)`。

所以 **`StaggerStatus.Staggered` 由 false 變 true 的那一刻**就是進 ragdoll 的時機。
不需要新增旗標 —— 大蔥只做擊退不做 `Stagger`，所以這個條件天生就只有卡車會滿足。

**先確認這件事。** 如果 `LeekTool` 也會呼叫 `ApplyStagger`，
就要在 `StaggerStatus` 加一個**非同步的本機欄位**（例如 `public bool LastWasHeavy`）來區分，
不要為此新增 `[Networked]` 狀態。

---

## 2. 架構：v1 不做第二套骨架

網路上常見的 active ragdoll 教學會要你準備兩套骨架（一套隱形的跑 Animator 當目標姿勢，
一套物理的蒙皮）。**v1 不要這樣做**，改成：

> **目標姿勢 = 一個靜態的站立姿勢快照。**

在 `Awake` 時把每根骨頭在**站立姿勢**下的 `localRotation` 記起來，
ragdoll 期間所有關節都朝這個固定姿勢施力。

為什麼這樣就夠：完全癱軟時目標姿勢根本不重要；
它只在**恢復階段**有影響，而那時候你要的正是「掙扎著想站直」——
朝一個固定站姿施力，讀起來就是那個感覺，而且這正是 PEAK 的笑點所在。

之後真的需要「倒地時還能揮手揮腳」再升級成活的姿勢來源，對外介面不用改。

---

## 3. 新檔案 `Scripts/Player/RagdollRig.cs`

掛在玩家 prefab 根物件上。

### 3.1 骨骼資料要可以在 Inspector 填

Claude 看不到 `MD_Alpaca.fbx` 的骨架長什麼樣，所以**不要在程式裡寫死骨頭名稱**。

```csharp
[System.Serializable]
public class RagdollBone
{
    public Transform bone;
    public float mass = 1f;
    public Vector3 colliderSize = new Vector3(0.12f, 0.25f, 0.12f);
    public Vector3 jointAxis = Vector3.right;
    public float angularLimit = 45f;
}

[SerializeField] private RagdollBone[] _bones;
[SerializeField] private Transform _hips;       // 根骨
[SerializeField] private Animator _animator;    // Visual 上的那一個
[SerializeField] private SkinnedMeshRenderer[] _renderers;
```

另外寫一支 Editor 輔助：選取根骨 → 按一個按鈕 → 把底下所有骨頭列成 `_bones` 的初始清單。
**實際要哪幾根、質量與角度上限由我人眼調**，這是 Claude 做不到的部分。

建議先只做**主要關節**（脖子、脊椎、四條腿各兩節、尾巴），不要每根都上。
關節越多越難調，而且四個玩家 × 十幾個關節已經不少了。

### 3.2 兩種狀態

```
待機（未倒地）
  - 所有 Rigidbody: isKinematic = true
  - 所有 Joint: 停用或 spring 拉滿
  - Animator: enabled = true（正常播動畫）

倒地
  - Animator: enabled = false      ← 關掉，不然會跟關節搶骨頭
  - 所有 Rigidbody: isKinematic = false
  - Joint 的 slerpDrive 依階段給力（見 4.）
```

### 3.3 關節設定（最容易寫錯的地方）

- `ConfigurableJoint.rotationDriveMode = RotationDriveMode.Slerp`
- 力道來自 **`slerpDrive.positionSpring` 與 `.maximumForce`** ——
  名字叫 positionSpring 但它屬於**角度驅動**。
  設成線性 drive 的話關節完全沒力，整隻會攤成一灘，這是這個做法最常見的失敗
- `targetRotation` **不能直接餵骨頭的 `localRotation`**。
  它是相對於初始姿勢、而且表達在由 `axis` / `secondaryAxis` 構成的 joint space 裡，還要取反。
  直接餵會得到一個完全扭曲的姿勢 —— 看起來像「物理壞了」而不是「公式錯了」，很難除錯。
  **請用社群通用的 `SetTargetRotationLocal(joint, targetLocalRotation, startLocalRotation)` 輔助函式**，
  不要自己推導
- `configuredInWorldSpace = false`
- 三軸 Motion 全部 `Locked`，角度用 `angularXLimit` / `angularYLimit` / `angularZLimit` 夾住

### 3.4 碰撞層

Ragdoll 的碰撞體要放**獨立的 layer**，只跟地面與牆壁碰撞：

- 不跟其他玩家、物品、互動探測碰撞
- 否則 `PlayerInteractor` 的 SphereCast 會打到骨頭，
  你會看到「對著倒在地上的隊友出現一堆奇怪的互動提示」
- `EnsureLayer` 在 `EditorBuildUtils` 已經有了，照既有做法加一層

---

## 4. 恢復的三個階段（手感全在這裡）

**笑點在爬起來，不在倒下。** 倒下只有半秒，爬起來那兩秒才是所有梗的來源。
所以 spring 不要線性回升，分三段：

```
① 癱軟   0 ~ 0.5s      spring = 0，完全沒力，該怎麼摔就怎麼摔
② 掙扎   0.5 ~ 1.5s    spring 從 0 爬到約 60%，開始想撐起來但撐不直
③ 站回   1.5s ~ 結束    spring 拉到 100%，姿勢收斂回站姿
結束後   0.35 秒        可見骨架從 ragdoll 姿勢混回動畫姿勢，Animator 重新啟用
```

**ragdoll 的總長度要等於 `GameTuning.TruckStaggerSeconds`**，
讓「看起來爬起來了」和「可以動了」是同一刻 —— 不然玩家會在還在地上時就開始走路。

新的數值放 `GameTuning` 最末端：

```csharp
// ---------- 倒地 ragdoll（純視覺）----------
public const float RagdollLimpSeconds    = 0.5f;   // 完全癱軟
public const float RagdollStruggleSpring = 0.6f;   // 掙扎階段的力道比例
public const float RagdollBlendBackTime  = 0.35f;  // 混回動畫的時間
public const float RagdollMaxSpring      = 3000f;  // 站直時的 slerpDrive.positionSpring
public const float RagdollMaxForce       = 1000f;  // slerpDrive.maximumForce
public const float RagdollLeashRadius    = 1.5f;   // 骨盆離膠囊最遠多少（見 5.）
```

---

## 5. 骨盆要栓在膠囊上

讓 ragdoll 完全自由的話，畫面上的羊駝會跟實際的碰撞位置分開 ——
在一個靠距離判定剃毛、交貨的遊戲裡那會很混亂。

做法：骨盆可以在膠囊周圍 `RagdollLeashRadius`（1.5 公尺）內自由翻滾，
超過就用力拉回。擊退本身已經讓膠囊在移動了（`StaggerStatus` 做的），
所以 ragdoll 是「在一個會飛出去的根上面翻滾」，翻滾感有了，位置也不會跑掉。

恢復時把可見骨架的位置平滑拉回膠囊，不要瞬移。

---

## 6. 驗收

**功能**

1. 被卡車炸到 → 整隻癱軟倒地，不是播一段動畫
2. 倒地期間會隨著擊退一起飛出去、在地上翻滾
3. 約 0.5 秒後開始掙扎，看得出「想站起來但站不直」
4. 站回來的時機跟「可以操作了」是同一刻，沒有落差
5. 混回動畫沒有瞬間跳格
6. 大蔥打到**不會**進 ragdoll，維持現況

**不能壞掉的**

7. 倒地期間膠囊的位置仍然由 NCC 控制，沒有飄走
8. 倒在地上的隊友**不會**產生奇怪的互動提示（碰撞層有隔開）
9. 倒地期間身上掉出來的毛照常散落、撿得到
10. 連續被炸兩次不會讓骨架爆開或卡在奇怪的姿勢
11. 站起來之後 Animator 正常，跑步、Idle、Work 都還在

**連線（重點）**

12. **沒有任何新的 `[Networked]` 欄位**
13. 兩個 client 都看得到對方倒地與爬起來
14. 兩端的姿勢細節不一樣是**可接受的**，但倒地／站起的**時間點**必須一致
15. client 端被炸、主機端被炸，表現都正確
16. 倒地的當下不會造成位置抖動或瞬移（ragdoll 沒有去推膠囊）

---

## 7. 交接點（Claude 做不到，會停下來問我）

- `_bones` 要填哪幾根骨頭、質量、碰撞體尺寸、角度上限 —— 需要看著模型調
- `RagdollMaxSpring` / `RagdollMaxForce` 的實際值 —— 一定要看著調
- 摔下去的姿勢好不好看

請先做到「骨架建得起來、倒得下去」，然後把場景交給我，我調完數值再繼續。

---

## 8. 開工前

先確認第 1 節那件事（`LeekTool` 有沒有呼叫 `ApplyStagger`）並回報。
另外把 `MD_Alpaca.fbx` 的骨架階層列出來給我看，我們一起決定哪幾根要上關節。
