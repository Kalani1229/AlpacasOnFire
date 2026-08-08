# 羊駝很忙 — Phase 1 設置與交付說明

Unity 6000.2.6f2 / URP / Input System / Photon Fusion 2

---

## 0. Photon Fusion 2 設定

專案裡已經有 **Fusion 2.0.12**（`Assets/Photon/Fusion`），本階段所有程式碼都是照這個版本的 API 寫的。

**Fusion Hub 在哪裡**：上方選單 **Tools → Fusion → Fusion Hub**（快捷鍵 Alt+F）。
它不會自動彈出來，也不會預先建立 `Assets/Photon/Fusion/Resources/` —— 那個資料夾與 `PhotonAppSettings.asset` 是你在 Hub 裡貼上 App Id **之後**才會被建立的。

1. 到 [Photon Dashboard](https://dashboard.photonengine.com/) 建一個 **Fusion** 類型的 App，複製 App Id。
2. `Tools → Fusion → Fusion Hub` → 貼進 **Fusion App Id** 欄位（或走 `Tools → Fusion → Realtime Settings`）。

> 離線的 `GameMode.Single` 不需要 App Id 也能跑，所以想先驗證玩法可以跳過這步；Host / Client 連線測試才一定要。

**如果選單列找不到「羊駝很忙」**：那一定是專案有編譯錯誤。Unity 只要有任何一個 C# 錯誤，就不會註冊任何 `MenuItem`。先把 Console 的紅字清乾淨，選單就會出現。

---

## 1. 建置佔位資產與場景（兩個選單指令）

Fusion 匯入、編譯通過之後，依序執行 Unity 上方選單：

| 順序 | 選單 | 做什麼 |
|---|---|---|
| 1 | **羊駝很忙 / 1. 建置佔位資產（材質 + Prefab + Catalog）** | 產生所有材質、prefab、`Resources/GameCatalog.asset`，並自動建立 `Player` / `CarriedItem` 兩個 Layer |
| 2 | **羊駝很忙 / 3. 建置全部場景（標題 + 測試關 + 第一關）** | 產生 `Title`、`Level_Test`、`Level01_Workshop` 三個場景並加入 Build Settings |

（選單 2 是「只建關卡資料、不動場景」，平常不用單獨執行。）

**順序不能顛倒**：第 3 步會去 `GameCatalog` 拿 prefab，Catalog 不完整就會做出一個只有 UI、沒有任何物件的空殼場景。所以第 3 步開頭會先檢查 Catalog，不完整就直接中止並在 Console 說明。

第 1 步跑完請確認 Console 出現
`[羊駝很忙] 佔位資產建置完成：玩家 prefab OK、物品 9 種、關卡元件 15 種。`
如果有紅字，它會列出**哪幾項**失敗（其餘會照常建完，不會整批中斷）。

**羊駝很忙 / 4. 檢查設置（診斷用）** 可以隨時檢查 Catalog 缺了什麼。
遇到「Play 之後只有 UI、場上空空的」，先跑這一項。

這兩步可以重複執行。改了配色或尺寸就重跑第 1 步，改了關卡佈局就重跑第 3 步。

**開始玩**：打開 `Assets/AlpacasOnFire/Scenes/Level01_Workshop.unity` 直接按 Play，會用 `GameMode.Single` 開一場離線遊戲。

---

## 2. 操作方式

| 動作 | 按鍵 |
|---|---|
| 移動 | WASD（永遠相對角色目前朝向） |
| 視角 + 角色朝向 | 滑鼠水平（兩者共用同一個 yaw，永遠一致） |
| 鏡頭俯仰 | 滑鼠垂直（只影響鏡頭與互動射線，角色不會傾斜） |
| 互動 | Space（情境式） |
| 丟出 / 接住 | Q（有東西正飛向你 → 一定是接住；否則才是丟出） |
| 使用工具（持續） | E 按住（噴槍） |
| 暫停 | Esc |

---

## 3. 兩條產線怎麼跑

**白 T-shirt**
剃毛器 → 面向隊友 Space（隊友掉羊毛）→ 撿 3 顆羊毛逐一放進縫紉機 → Space 選版型 → 等 6 秒 → 拿成品 → 拿空箱子 → 對衣服或箱子 Space 裝箱 → 郵箱 Space 出貨

**紅 T-shirt**
同上做出白衣服 → 走到戶外採紅染料 → 放進果汁機 → 等 4 秒拿到染劑罐 → 拿噴槍對染劑罐 Space 填裝 → 把衣服穿到人偶或隊友身上 → 按住 E 噴滿（約 1.6 秒）→ 空手 Space 取下 → 裝箱 → 出貨

---

## 4. 關卡編輯器

**羊駝很忙 / 關卡編輯器**（快捷鍵 Ctrl+Shift+L）

- 設定關卡時間、星級門檻（通關分數）、訂單內容池
- 新增／刪除／複製元件與 NPC，逐項編輯位置、旋轉、縮放
- `Variant` 欄位：染料點填 `Red`/`Blue`/`Green`/`Yellow`、飾品點填 `Button`/`Ribbon`/`Badge`、NPC 填種類（Phase 2 用）
- **在目前場景生成**：把資料倒進場景的 `[Level]` 節點
- **從場景回寫**：你在 Scene 視窗直接拖動物件後，把座標同步回關卡資料（也會自動收編你手動複製出來的物件）
- **存成 JSON / 從 JSON 讀入**
- **建立獨立場景**：連同 Fusion runner、關卡系統、UI 一起產生一個可直接玩的場景

### 存檔格式（Phase 3 會直接沿用）

`LevelDefinition`（ScriptableObject，也可序列化成 JSON）內含 `List<LevelElementRecord>`：

```
LevelElementRecord { id, type, position, rotationEuler, scale, variant, intParam }
```

`LevelElementType` 已經預留了 `Npc`、`RecyclingMachine` 等 Phase 2 才有邏輯的類型 —— 現在可以放置、可以顯示佔位方塊，但沒有任何功能邏輯。**新增類型請往列舉後面加，不要插在中間**（存檔相容）。

---

## 5. 程式架構地圖

```
Assets/AlpacasOnFire/
├─ Scripts/
│  ├─ Core/         GameTuning（全部數值）、GameEnums、GarmentSpec、GameCatalog、ItemFactory、GameAudio、PlaceholderPalette
│  ├─ Networking/   GameLauncher（Runner + 玩家生成 + 輸入）、NetInput、SpawnPointRegistry
│  ├─ Player/       PlayerController、PlayerCameraRig（spring arm）、PlayerCarry、PlayerInteractor、LocalInputProvider
│  ├─ Interaction/  IInteractable、IGarmentHost、NetworkInteractable
│  ├─ Items/        CarriableItem 與各種道具、IItemUser / IGarmentHostUser / IHoldTool
│  ├─ Machines/     MachineBase、SewingMachine、Juicer、Mannequin、Mailbox、BoxDispenser、DyeSourceNode、AccessoryDispenser、RecyclingMachine
│  ├─ Orders/       OrderEntry、OrderBoard、LevelDirector
│  ├─ UI/           GameUIRoot、GameHud、PatternSelectPanel、PauseMenu、ResultsScreen、TitleMenu、UIFactory
│  └─ Level/        LevelDefinition、LevelElementRecord、LevelElementTag、LevelBuilder
└─ Editor/          PlaceholderAssetBuilder、LevelSceneBuilder、LevelEditorWindow、LevelDefinitionInspector
```

### 互動系統為什麼這樣寫（Phase 2 的擴充點）

`PlayerInteractor` **完全不認識任何具體的互動類型**：它只負責從畫面中心發射球形射線、問 `CanInteract`、呼叫 `Interact`。所有分歧靠三個介面做雙重分派：

- `IInteractable` — 「我可以被互動」（機台、地面物品、隊友、人偶、箱子、郵箱）
- `IItemUser` — 「我手上的東西可以對另一個**物品**做事」（箱子裝衣服、噴槍填染劑、飾品裝上衣服）
- `IGarmentHostUser` — 「我手上的東西可以對**可穿衣對象**做事」（剃毛器剃隊友、衣服穿上去、飾品裝在穿著的衣服上）

Phase 2 要加偷竊／搶奪／吐口水／回收機，寫新的實作類別即可，不需要改 `PlayerInteractor` 或任何既有物件。`RecyclingMachine` 已經是 `IInteractable`，只是 `CanInteract` 回傳 `false`，Phase 2 直接補 `Interact` 內容就好。

---

## 6. 連線測試

1. 先在 `GameMode.Single` 把所有玩法驗證完（`[Network]` 物件的 GameLauncher Inspector 可切換 Auto Start Mode）。
2. 用 [ParrelSync](https://github.com/VeriorPas/ParrelSync) 複製一份專案，開兩個 Unity Editor 實例。
3. 從 `Title` 場景進：一邊按「建立房間（Host）」，另一邊按「加入房間（Client）」。

已納入同步的狀態：玩家位置／朝向、持有物、機台處理狀態與進度、訂單板內容與剩餘時間、隊伍金額、人偶穿著與噴漆進度、噴槍染劑量、羊毛存量。

---

## 7. Phase 1 不做的東西（規格書明訂）

NPC AI、偷竊／搶奪、吐口水、回收機邏輯、套裝訂單判定、版型解鎖、移動中的關卡、頭目戰。
NPC 與回收機只有佔位方塊 + 可被關卡編輯器放置，沒有任何功能邏輯。

---

## 8. 我設定的所有預設數值

全部集中在 `Assets/AlpacasOnFire/Scripts/Core/GameTuning.cs`，改一個地方就會全域生效。

### 角色

| 項目 | 值 |
|---|---|
| 羊駝站立高度 / 半徑 | 1.8 m / 0.35 m |
| 移動速度（maxSpeed） | 5.0 m/s |
| 移動加速度（acceleration） | 40 m/s² |
| 放開按鍵的減速（braking） | 30 |
| 重力 | 20 m/s²（以 −20 傳入） |
| 手上有東西時速度倍率 | 0.85 |

> 移動是由 Fusion 內建的 **`NetworkCharacterController`** 驅動，上面四個值在 `PlayerController.Spawned()` 從 `GameTuning` 餵進去，並且 **`rotationSpeed` 固定為 0** —— NCC 預設會把角色轉向移動方向，那會破壞共用朝向模型，所以關掉，朝向完全由滑鼠的 Yaw 決定。
>
> **不要改回「原生 CharacterController + NetworkTransform」**。Unity 的 CharacterController 有內部快取位置，直接寫 `transform.position` 它認不得；Fusion 重模擬時必須先 `enabled = false / true` 才會同步。少了這步，用戶端會在連線幾秒後開始抖動並瞬間大步移動。NCC 內部的 `CopyToEngine()` 就是在做這件事。同理，要把角色瞬移時一律走 `NetworkCharacterController.Teleport()`。

### 攝影機

| 項目 | 值 |
|---|---|
| 鏡頭距離 | 4.5 m |
| 樞紐高度（腳底起算） | 1.5 m |
| 側向偏移 | 0.35 m（略偏右，避免角色擋住準心） |
| 俯仰角範圍 / 預設 | −35° ~ 70° / 18° |
| 跟隨平滑（SmoothDamp 時間） | 0.08 s |
| 碰撞 SphereCast 半徑 | 0.25 m |
| 碰撞後再往前縮 | 0.20 m |
| 遮擋時拉近速度 / 離開後放遠速度 | 40 m/s / 6 m/s（拉近快、放遠慢，避免牆邊抖動） |
| 滑鼠靈敏度 X / Y | 0.12 / 0.10 度每像素 |

### 互動與丟接

| 項目 | 值 |
|---|---|
| 互動距離 / 射線半徑 | 2.5 m / 0.35 m |
| 眼睛（射線起點）高度 | 1.5 m |
| 丟出速度 / 上拋比例 | 9.0 m/s / 0.25 |
| 飛行重力 | 20 m/s² |
| 接住半徑 | 1.8 m |
| 判定「正在接近」的最小接近速度 | 0.5 m/s |
| 飛行逾時 | 6 s |

### 生產鏈

| 項目 | 值 |
|---|---|
| 羊駝身上羊毛上限 | 3 份 |
| 羊毛再生 | 每 6 秒長回 1 份 |
| 每次剃毛掉落 | 1 顆羊毛 |
| 縫紉機需要羊毛 | 3 份 |
| **縫紉機處理秒數** | **6.0 秒** |
| **果汁機處理秒數** | **4.0 秒** |
| **噴槍容量** | **100 單位** |
| 噴槍消耗速率 | 25 單位／秒（滿容量可噴 4 秒） |
| 一件衣服需要噴的量 | 40 單位（約 1.6 秒） |
| 噴灑距離 / 夾角判定 | 3.0 m / dot ≥ 0.85 |
| 染料點重生 | 5 秒 |

### 訂單與經濟

| 項目 | 值 |
|---|---|
| 關卡時間 | 180 秒（3 分鐘） |
| 同時最多訂單數 | 4 張 |
| 第一張訂單延遲 | 3 秒 |
| 訂單生成間隔 | 20 秒 |
| 單張訂單存活時間（Countdown Border 總長） | 75 秒 |
| 訂單基礎報酬 | $100 |
| 含飾品加成 | +$20 |
| **訂單超時扣款** | **$40** |
| **錯誤出貨扣款** | **$25** |

### 星級門檻

| 星級 | 金額 |
|---|---|
| ★ | $300 |
| ★★ | $600 |
| ★★★ | $900 |

### 機台外型

| 項目 | 值 |
|---|---|
| 固定機台高度 | 1.9 m（略高於羊駝的 1.8 m，確保視覺上明顯比素材與工具大） |
| 機台佔地 | 1.2 × 1.2 m |

### 佔位配色（已修正兩處衝突）

縫紉機 **橙色**（原本藍色，與 P1 撞色）、人偶 **米色**（原本黃色，與 P3 撞色）。
其餘依規格書：玩家 P1 藍 / P2 紅 / P3 黃 / P4 綠、NPC 灰、果汁機紫、回收機深藍灰、箱子木箱色、工具銀灰底 + 紅刀鋒 / 青噴嘴。

---

## 9. 音效

`GameAudio.Play(SfxId.X)` / `PlayAt(...)`，佔位音是程式合成的簡單音效（不需要任何音檔）。
已掛好的觸發點：出貨成功、出貨失敗、訂單超時扣款、剃毛成功、飾品裝上、噴槍噴出染劑、接住、丟出、拾取、放下、人偶穿上／脫下衣服、機台開始／完成、關卡結束。
之後換成正式音效只要改 `GameAudio.Resolve()`，呼叫端一行都不用動。
