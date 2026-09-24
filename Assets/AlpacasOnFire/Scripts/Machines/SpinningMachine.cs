using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 紡線機：羊毛 → 絲線。生產鏈從兩段變三段
    /// （素材箱 → **紡線機** → 織布機 → 交貨窗口）。
    ///
    /// **這是第一台「人必須待在原地」的機器。**
    /// 其他機台全是射後不理 —— 放進去、走開、它自己做完。紡線是反過來的：
    /// 有人被釘在那裡，按住左鍵進度才會前進。
    ///
    /// 三個由此而來的設計要求，讀這份檔案時請當成不可簡化的前提：
    ///
    ///  - **進度保留。** 放開按鍵或走開，`Progress` 停在原地，不歸零也不衰退。
    ///  - **可以接手。** A 紡到一半被叫走，B 走過去按住就接著紡完。
    ///    進度存在機台上、不存在玩家身上，所以這件事是自然成立的 ——
    ///    但 `Progress` **必須是 [Networked]**，不然接手的人看到的條子是錯的。
    ///  - **一次一份。** 原料槽一份、待料槽一份，紡完一份才輪到下一份。
    ///
    /// ---
    ///
    /// ### 核心原則：投料可以遠距，操作必須親臨
    ///
    /// 羊毛**丟得進去**（`IThrownItemReceiver`，跟其他機台一樣），
    /// 但**丟進去不會讓它開始動** —— `AcceptThrown` 一步 `Progress` 都不推。
    ///
    /// 這條規則長出來的分工正是這台機器存在的理由：
    /// <code>
    /// 玩家 A —— 站在紡線機前按住不放，專心紡
    /// 玩家 B —— 在素材箱那邊一份一份把羊毛丟過來
    /// </code>
    /// 兩個人各自待在自己的位置，靠丟接串起來。這是丟接系統第一次有「持續性」的用途。
    ///
    /// ---
    ///
    /// ### 為什麼不繼承 MachineBase
    ///
    /// `MachineBase` 的進度是 `TickTimer` 自動倒數，紡線是「按住才前進」。
    /// 兩種模型湊在一起會變成一堆互相打架的特例，所以這台直接繼承
    /// `NetworkInteractable`，自己管一條 `float Progress`。
    ///
    /// 代價是 `MachineBase` 的自動出貨、成品染色、狀態燈都得自己寫一遍 ——
    /// 其中**自動出貨是刻意不寫的**，理由見 <see cref="TryTakeOutput"/> 上方的註解。
    /// </summary>
    public class SpinningMachine : NetworkInteractable, IHoldInteractable, IThrownItemReceiver
    {
        [Header("Spinning")]
        [SerializeField] private Transform _progressBar;
        [SerializeField] private Renderer _statusLight;
        [Tooltip("留空的話會自動找名為 Body 的子物件。紡線中會輕微轉動。")]
        [SerializeField] private Renderer _bodyRenderer;
        [Tooltip("原料槽：裡面那份毛，顯示顏色。")]
        [SerializeField] private Renderer _woolSlot;
        [Tooltip("待料槽：機台側邊的小球。負責丟的人要能從遠處看出還有沒有空位。")]
        [SerializeField] private Renderer _queuedSlot;
        [Tooltip("紡線中會繞 Y 軸轉的部位。留空的話轉 _bodyRenderer 的 transform。")]
        [SerializeField] private Transform _spinVisual;

        // ---- 原料槽 ----
        [Networked] public NetworkBool HasInput { get; set; }
        [Networked] public int InputColorRaw { get; set; }

        /// <summary>0 ~ GameTuning.SpinSeconds。**必須是 [Networked]**，接手的人才看得到真的進度。</summary>
        [Networked] public float Progress { get; set; }

        // ---- 成品槽 ----
        [Networked] public NetworkBool HasOutput { get; set; }
        [Networked] public int OutputColorRaw { get; set; }

        // ---- 待料槽 ----
        // 沒有它的話，負責丟的人必須抓準「剛紡完」那一瞬間才丟得進去，
        // 等於逼他站在旁邊等，上面那套分工就白做了。所以留**一格**。
        [Networked] public NetworkBool HasQueued { get; set; }
        [Networked] public int QueuedColorRaw { get; set; }

        /// <summary>
        /// 最後一次被「按住」推進的 tick。用它在所有端推導「現在有人正在紡」。
        ///
        /// 不用一個 NetworkBool 是因為沒有人會來關掉它 —— 玩家走開、轉頭、
        /// 被撞倒，`HoldTick` 就只是不再被呼叫，沒有任何「結束」事件可以掛。
        /// 記最後一次的 tick 就不需要那個事件：超過幾個 tick 沒更新就是停了。
        /// </summary>
        [Networked] public int LastSpinTick { get; set; }

        private MaterialPropertyBlock _mpb;
        private readonly BarAnchor _progressBar01 = new(BarAnchor.Axis.X);
        private float _spinAngle;

        public DyeColorType InputColor => (DyeColorType)InputColorRaw;
        public DyeColorType OutputColor => (DyeColorType)OutputColorRaw;
        public DyeColorType QueuedColor => (DyeColorType)QueuedColorRaw;

        public float Progress01 => Mathf.Clamp01(Progress / GameTuning.SpinSeconds);

        /// <summary>現在有沒有人正按著。容忍 4 個 tick 的空檔，免得網路抖動讓它一閃一閃。</summary>
        public bool IsSpinning =>
            Object != null && Object.IsValid && Runner != null
            && LastSpinTick > 0 && ((int)Runner.Tick - LastSpinTick) <= 4;

        public override int InteractionPriority => 1;   // 跟其他機台同級

        /// <summary>
        /// 裝不裝得下再一份毛。
        ///
        /// **刻意不看 HasOutput。** 成品還沒被取走也不該擋住裝料 ——
        /// 負責丟的人看不到機台的細節狀態，擋住他會讓補料變成猜謎。
        /// 只有「原料槽 + 待料槽都滿」才拒絕。手放與丟進來共用這個條件。
        /// </summary>
        public bool CanAcceptWool => !HasInput || !HasQueued;

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            CacheBodyRenderer();
        }

        private void CacheBodyRenderer()
        {
            if (_bodyRenderer != null) return;
            var body = transform.Find("Body");
            if (body != null) _bodyRenderer = body.GetComponent<Renderer>();
        }

        // ---------------- 單擊：裝料 / 取貨 ----------------

        /// <summary>
        /// **這支回 false 的話，按住的路徑也會一起死。**
        ///
        /// `PlayerController` 的按住分支是走 `PlayerInteractor.FindTarget()` 找目標的，
        /// 而 `FindTarget()` 會先用 `CanInteract` 過濾候選。所以「有毛、還沒紡完」
        /// 那個狀態（單擊沒事做）仍然必須回 true，由 <see cref="Interact"/> 自己什麼都不做。
        /// 行為跟規格表一致，但按住找得到目標。
        /// </summary>
        public override bool CanInteract(in InteractionContext ctx)
        {
            // 手上有毛 —— 裝得下要進得來，裝滿了也要進得來（不然顯示不出「裝滿了」）
            if (ctx.HeldKind == ItemKind.Wool) return true;

            // 成品待取
            if (HasOutput) return true;

            // 有料 -> 按住才有得紡，見上面的註解
            return HasInput;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            // 裝料優先於取貨：成品沒取走也不擋裝料，提示要跟行為一致
            if (ctx.HeldKind == ItemKind.Wool)
            {
                string colour = PlaceholderPalette.DyeName(ctx.Held.Spec.Color);
                if (!HasInput)  return $"[左鍵] 放入{colour}毛";
                if (!HasQueued) return $"[左鍵] 把{colour}毛放進待料槽";
                return "裝滿了 —— 等這一份紡完";
            }

            if (HasOutput)
                return ctx.IsEmptyHanded
                    ? $"[左鍵] 取走{PlaceholderPalette.DyeName(OutputColor)}絲線"
                    : "先空出雙手才能取走絲線";

            // 有料在裡面：單擊沒事做，該說的話交給 GetHoldPrompt
            if (HasInput) return "";

            return "需要羊毛";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (ctx.HeldKind == ItemKind.Wool)
            {
                if (!CanAcceptWool) return;
                var colour = ctx.Held.Spec.Color;
                ctx.Player.Carry.ConsumeHeld();
                ctx.Player.TriggerWork();
                InsertWool(colour);
                return;
            }

            if (HasOutput) TryTakeOutput(in ctx);
        }

        // ---------------- 按住：紡線 ----------------

        /// <summary>
        /// 成品沒取走就不能再紡一份 —— 成品槽只有一格，紡出來沒地方放。
        /// 注意這跟「裝料不看 HasOutput」不衝突：裝進來的毛會在待料槽等著。
        /// </summary>
        public bool CanHold(in InteractionContext ctx) => HasInput && !HasOutput;

        public string GetHoldPrompt(in InteractionContext ctx)
        {
            if (!HasInput) return "";
            if (HasOutput) return "先把絲線取走才能繼續紡";
            return $"[按住左鍵] 紡{PlaceholderPalette.DyeName(InputColor)}線　{Progress01 * 100f:F0}%";
        }

        /// <summary>
        /// 按住的每一個 tick。
        ///
        /// **進度只在這裡前進**，沒有任何自動倒數 —— 沒人按著就是停著。
        /// 放開／走開不需要任何收尾程式碼：`HoldTick` 單純不再被呼叫，
        /// `Progress` 就留在機台上，下一個人接手時直接從那裡繼續。
        /// </summary>
        public void HoldTick(in InteractionContext ctx, float deltaTime)
        {
            if (!HasStateAuthority) return;
            if (!CanHold(in ctx)) return;

            LastSpinTick = (int)Runner.Tick;
            Progress += deltaTime;

            // 每個 tick 都重新點一次工作動畫（WorkAnimSeconds 比一個 tick 長很多，
            // 所以效果是「按著的期間一直在動」，而不是一直重播）
            ctx.Player.TriggerWork();

            if (Progress < GameTuning.SpinSeconds) return;

            OutputColorRaw = InputColorRaw;
            HasOutput = true;
            HasInput = false;
            InputColorRaw = 0;
            Progress = 0f;

            GameAudio.PlayAt(SfxId.MachineDone, transform.position);
            PromoteQueued();
        }

        // ---------------- 料槽 ----------------

        /// <summary>
        /// 放一份毛進去。手放的與丟進來的走同一段。
        /// 原料槽空就進原料槽，否則進待料槽，兩個都滿就拒絕。
        /// </summary>
        private bool InsertWool(DyeColorType colour)
        {
            if (!HasInput)
            {
                InputColorRaw = (int)colour;
                HasInput = true;
                GameAudio.PlayAt(SfxId.Pickup, transform.position);
                return true;
            }

            if (!HasQueued)
            {
                QueuedColorRaw = (int)colour;
                HasQueued = true;
                GameAudio.PlayAt(SfxId.Pickup, transform.position);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 紡完一份之後待料槽自動遞補，不用再丟一次。
        /// **這不會加快紡線速度**，待料槽只是讓補料可以提前。
        /// </summary>
        private void PromoteQueued()
        {
            if (!HasQueued) return;

            InputColorRaw = QueuedColorRaw;
            HasInput = true;
            HasQueued = false;
            QueuedColorRaw = 0;
            GameAudio.PlayAt(SfxId.MachineStart, transform.position);
        }

        /// <summary>
        /// 玩家把絲線拿走。
        ///
        /// **這台機器刻意沒有「旁邊有輸送帶就自動出貨」。**
        /// 其他機台有，紡線機沒有 —— 預設佈局裡的輸送帶是往交貨窗口送的，
        /// 紡線機一旦自動出貨，絲線會被送去交貨窗口而不是織布機，
        /// 玩家會完全看不懂發生什麼事。而且紡線時本來就站在機台前，
        /// 順手拿走的成本是零。
        ///
        /// 絲線本身仍然是普通的 CarriableItem，**可以用手丟到輸送帶上**，那條路不受影響。
        /// </summary>
        private void TryTakeOutput(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !HasOutput) return;
            if (!ctx.IsEmptyHanded) return;

            var spec = GarmentSpec.Create(PatternType.None, OutputColor);
            var item = ItemFactory.SpawnIntoHands(Runner, ItemKind.Thread, spec, ctx.Player);
            if (item == null) return;

            HasOutput = false;
            OutputColorRaw = 0;
            GameAudio.PlayAt(SfxId.Pickup, transform.position);
            ctx.Player.TriggerWork();
        }

        // ---------------- 被丟進來的羊毛 ----------------

        public bool CanAcceptThrown(CarriableItem item)
            => item != null && item.Kind == ItemKind.Wool && CanAcceptWool;

        /// <summary>
        /// **收下料，但一步都不推進度。** 這就是「投料可以遠距、操作必須親臨」的實作。
        /// 丟進去的毛會乖乖待在槽裡，要有人走過去按住才開始轉。
        /// </summary>
        public bool AcceptThrown(CarriableItem item)
        {
            if (!HasStateAuthority || !CanAcceptThrown(item)) return false;
            return InsertWool(item.Spec.Color);
        }

        // ---------------- 外觀 ----------------

        public override void Render()
        {
            _mpb ??= new MaterialPropertyBlock();

            // 進度條：只在有料時出現，由左往右長
            if (_progressBar != null)
            {
                bool show = HasInput;
                if (_progressBar.gameObject.activeSelf != show) _progressBar.gameObject.SetActive(show);
                if (show) _progressBar01.Apply(_progressBar, Progress01);
            }

            RenderSlot(_woolSlot, HasInput, InputColor);
            RenderSlot(_queuedSlot, HasQueued, QueuedColor);

            if (_statusLight != null) Tint(_statusLight, StatusColor());

            RenderSpin();
        }

        /// <summary>空（綠）→ 有毛待紡（黃）→ 紡好待取（絲線的顏色）。</summary>
        private Color StatusColor()
        {
            if (HasOutput) return PlaceholderPalette.Dye(OutputColor);
            if (HasInput) return new Color(0.95f, 0.75f, 0.15f);
            return new Color(0.25f, 0.85f, 0.35f);
        }

        private void RenderSlot(Renderer r, bool filled, DyeColorType colour)
        {
            if (r == null) return;
            if (r.enabled != filled) r.enabled = filled;
            if (!filled) return;
            Tint(r, PlaceholderPalette.Dye(colour));
        }

        /// <summary>
        /// 正在被紡的時候要看得出來，不然隊友看不出「那台正在被人操作」。
        /// 轉的是視覺物件，沒有碰撞體被牽動。
        /// </summary>
        private void RenderSpin()
        {
            CacheBodyRenderer();
            var spin = _spinVisual != null
                ? _spinVisual
                : (_bodyRenderer != null ? _bodyRenderer.transform : null);
            if (spin == null) return;

            if (IsSpinning)
            {
                _spinAngle += 260f * Time.deltaTime;
                if (_spinAngle >= 360f) _spinAngle -= 360f;
            }
            else if (!Mathf.Approximately(_spinAngle, 0f))
            {
                // 停下來時轉回原位，不要卡在一個歪掉的角度
                _spinAngle = Mathf.MoveTowards(_spinAngle > 180f ? _spinAngle - 360f : _spinAngle,
                                               0f, 360f * Time.deltaTime);
            }

            spin.localRotation = Quaternion.Euler(0f, _spinAngle, 0f);
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }
    }
}
