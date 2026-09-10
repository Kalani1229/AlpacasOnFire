using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 織布機：吃 1–2 份羊毛，織出「主色 + 點綴色」的衣服。
    ///
    /// **這台機器是一道限時決策，沒有開始鈕。**
    ///
    ///     放入第一份毛（主色）
    ///        │  立刻開始織，目標 = 單色 4 秒
    ///        │
    ///        ├─ 4 秒內沒有第二份 ─────────→ 單色衣服
    ///        │
    ///        └─ 4 秒內放入第二份（點綴色）
    ///               目標改成雙色 7 秒，**已經過的時間算數**
    ///               （第 3 秒放入 -> 還要 4 秒，不是重跑 7 秒）
    ///                             ─────────→ 雙色衣服
    ///
    /// 想做雙色，第二份毛必須在單色織完之前送到 ——
    /// 所以丟接與輸送帶第一次有了真正的用途，不只是省腳程。
    ///
    /// **順序有差**：紅→藍 是「紅底藍紋」，藍→紅 是「藍底紅紋」，兩件不同的衣服。
    ///
    /// 「做好留在機台、手動取貨、旁邊有朝外送的輸送帶就自動出貨」整套都是
    /// MachineBase 既有的，這裡一行都不用重寫。
    /// </summary>
    public class WeavingMachine : MachineBase, IThrownItemReceiver
    {
        [Header("Weaving")]
        [Tooltip("這台機器產出的版型。一台機器只做一種。")]
        [SerializeField] private PatternType _outputPattern = PatternType.TShirt;

        [Tooltip("機體上的兩格色塊：第 0 格主色、第 1 格點綴色。長度要是 2。")]
        [SerializeField] private Renderer[] _woolSlotVisuals;

        [Header("進度條（自己畫，不用 MachineBase 的預設）")]
        [Tooltip("整條軌道，永遠是滿格 = 雙色秒數。")]
        [SerializeField] private Transform _weaveTrack;
        [Tooltip("目前目標的終點段（單色時停在刻度線，加了第二份毛才推到滿格）。")]
        [SerializeField] private Transform _weaveTarget;
        [Tooltip("已經織了多久。**只前進、不倒退**。")]
        [SerializeField] private Transform _weaveFill;
        [Tooltip("單色的終點刻度線，固定在 4/7 的位置。")]
        [SerializeField] private Transform _weaveTick;

        [Networked] public int WoolCount { get; set; }
        [Networked] public int MainColorRaw { get; set; }
        [Networked] public int AccentColorRaw { get; set; }

        private MaterialPropertyBlock _mpb;

        public DyeColorType MainColor => (DyeColorType)MainColorRaw;
        public DyeColorType AccentColor => (DyeColorType)AccentColorRaw;

        public override int InteractionPriority => 1;

        protected override ItemKind OutputItemKind => ItemKind.Garment;

        /// <summary>只放一份時點綴色 = 主色，也就是單色衣服。</summary>
        protected override GarmentSpec BuildOutputSpec()
            => GarmentSpec.Create(_outputPattern, MainColor, AccessoryType.None, AccentColor);

        /// <summary>
        /// 收不收得下再一份毛。
        ///
        /// **刻意不看 Processing** —— 織製中必須收得下第二份，
        /// 不然「趕在單色織完前送第二份毛」這個玩法直接不成立。
        /// 這是本專案唯一一處對「機台織製中不收東西」慣例的偏離，只有織布機這樣。
        /// 手放與丟進來共用這個條件。
        /// </summary>
        private bool CanAcceptWool => !HasOutput && WoolCount < GameTuning.WeaveMaxWool;

        // ---------------- 互動 ----------------

        public override bool CanInteract(in InteractionContext ctx)
        {
            // 成品沒拿走之前，Space 一律解讀成取貨
            if (HasOutput) return ctx.IsEmptyHanded;

            // 空手沒有別的意思 —— 沒有開始鈕
            if (ctx.HeldKind != ItemKind.Wool) return false;
            return CanAcceptWool;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (HasOutput)
                return ctx.IsEmptyHanded
                    ? $"[Space] 取出 {DescribeOutput()}"
                    : "先空出雙手才能取出成品";

            if (ctx.HeldKind == ItemKind.Wool)
            {
                if (WoolCount >= GameTuning.WeaveMaxWool)
                    return $"已經放滿 {GameTuning.WeaveMaxWool} 份了";

                if (!Processing)
                    return $"[Space] 放入{PlaceholderPalette.DyeName(ctx.Held.Spec.Color)}毛開始織（主色）";

                // 織製中：告訴玩家還剩多少時間可以加第二份
                float left = ProcessTimer.RemainingTime(Runner) ?? 0f;
                return $"[Space] 加入{PlaceholderPalette.DyeName(ctx.Held.Spec.Color)}毛當點綴（剩 {left:F1} 秒）";
            }

            if (Processing)
            {
                float left = ProcessTimer.RemainingTime(Runner) ?? 0f;
                return WoolCount >= GameTuning.WeaveMaxWool
                    ? $"織布中… 還有 {left:F1} 秒"
                    : $"織布中… 還有 {left:F1} 秒可以加點綴色";
            }

            return "需要羊毛（第一份是主色、第二份是點綴色）";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (HasOutput)
            {
                TryTakeOutput(in ctx);
                return;
            }

            if (ctx.HeldKind != ItemKind.Wool) return;
            if (!CanAcceptWool) return;

            var colour = ctx.Held.Spec.Color;
            ctx.Player.Carry.ConsumeHeld();
            InsertWool(colour);
        }

        /// <summary>
        /// 放一份毛進去。手放的與丟進來的共用這一段。
        ///
        /// 第一份：記主色並立刻開工（單色 4 秒）。
        /// 第二份：記點綴色並把目標改成雙色 7 秒，已經過的時間算數。
        /// </summary>
        private void InsertWool(DyeColorType colour)
        {
            if (WoolCount == 0)
            {
                MainColorRaw = (int)colour;
                AccentColorRaw = (int)colour;   // 先當單色，放第二份才會被覆蓋
                WoolCount = 1;

                BeginProcess(GameTuning.WeaveSingleSeconds);
                GameAudio.PlayAt(SfxId.Pickup, transform.position);
                return;
            }

            AccentColorRaw = (int)colour;
            WoolCount = 2;

            RetargetProcess(GameTuning.WeaveDoubleSeconds);
            GameAudio.PlayAt(SfxId.MachineStart, transform.position);
        }

        /// <summary>
        /// 織完之後把兩格與顏色清空，下一批才不會沿用上一件的顏色。
        ///
        /// 清空一定要排在 base.FixedUpdateNetwork() 之後 ——
        /// 成品的 GarmentSpec 是在那裡面用目前的顏色建出來的。
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            bool wasProcessing = Processing;
            base.FixedUpdateNetwork();

            if (!HasStateAuthority || !wasProcessing || Processing) return;

            WoolCount = 0;
            MainColorRaw = 0;
            AccentColorRaw = 0;
        }

        // ---------------- 被丟進來的羊毛 ----------------

        /// <summary>
        /// **跟其他機台不一樣：織製中也收。** 理由見 CanAcceptWool 的註解。
        /// 只改織布機，縫紉機與果汁機維持原本的 `!Processing && !HasOutput`。
        /// </summary>
        public bool CanAcceptThrown(CarriableItem item)
            => item != null && item.Kind == ItemKind.Wool && CanAcceptWool;

        public bool AcceptThrown(CarriableItem item)
        {
            if (!HasStateAuthority || !CanAcceptThrown(item)) return false;
            InsertWool(item.Spec.Color);
            return true;
        }

        // ---------------- 外觀 ----------------

        /// <summary>
        /// 進度條自己畫，**不用 MachineBase 的預設**。
        ///
        /// 為什麼：`Progress01` 是「已經過 / 目標時長」，目標從 4 變成 7 的那一刻
        /// 分母變大，條子會往回縮（3/4 = 75% -> 3/7 = 43%）。邏輯上正確，
        /// 但看起來像 bug。
        ///
        /// 所以這裡永遠以雙色秒數為滿格：
        ///   填色 = 已經過 / 7          -> **單調前進，永遠不倒退**
        ///   終點 = 目前目標 / 7        -> 加了第二份毛就從 4/7 推到 7/7
        ///   刻度 = 4/7 的固定位置      -> 單色的死線看得見
        ///
        /// 「已經過的時間算數」就這樣變成看得見的東西：
        /// 填色一格都不動，代價直接表現成「終點被推遠了」。
        ///
        /// prefab 上的 `_progressBar` 刻意留空，所以 base.Render() 會跳過它的
        /// 預設畫法 —— 其他機台完全不受影響。
        /// </summary>
        public override void Render()
        {
            base.Render();

            RenderWoolSlots();
            RenderProgress();
        }

        private void RenderWoolSlots()
        {
            if (_woolSlotVisuals == null) return;
            _mpb ??= new MaterialPropertyBlock();

            for (int i = 0; i < _woolSlotVisuals.Length; i++)
            {
                var r = _woolSlotVisuals[i];
                if (r == null) continue;

                bool filled = i < WoolCount;
                if (r.enabled != filled) r.enabled = filled;
                if (!filled) continue;

                Tint(r, PlaceholderPalette.Dye(i == 0 ? MainColor : AccentColor));
            }
        }

        private void RenderProgress()
        {
            bool show = Processing;

            if (_weaveTrack != null && _weaveTrack.gameObject.activeSelf != show)
                _weaveTrack.gameObject.SetActive(show);

            if (!show) return;

            float full = Mathf.Max(0.01f, GameTuning.WeaveDoubleSeconds);
            float fill01 = Mathf.Clamp01(ElapsedSeconds / full);
            float target01 = Mathf.Clamp01(ProcessDuration / full);

            SetBarWidth(_weaveFill, fill01);
            SetBarWidth(_weaveTarget, target01);

            // 刻度線固定在單色的死線上，不隨狀態移動
            if (_weaveTick != null)
            {
                float tick01 = Mathf.Clamp01(GameTuning.WeaveSingleSeconds / full);
                var p = _weaveTick.localPosition;
                _weaveTick.localPosition = new Vector3(BarLeft + tick01 * BarWidth, p.y, p.z);
            }

            _mpb ??= new MaterialPropertyBlock();

            // 已經追不上單色死線（目標還是單色、時間快到）時把填色轉暖，
            // 提醒玩家「要加點綴色就是現在」
            bool stillSingle = WoolCount < GameTuning.WeaveMaxWool;
            var fillColour = stillSingle
                ? Color.Lerp(new Color(0.95f, 0.75f, 0.15f), new Color(1f, 0.45f, 0.2f), fill01 / Mathf.Max(0.01f, target01))
                : new Color(0.4f, 0.85f, 1f);

            var fillRenderer = _weaveFill != null ? _weaveFill.GetComponent<Renderer>() : null;
            Tint(fillRenderer, fillColour);
        }

        /// <summary>進度條在機體本地座標中的左端與總長。跟 prefab 的擺法對應。</summary>
        private const float BarLeft = -0.5f;
        private const float BarWidth = 1.0f;

        /// <summary>把一段條子從左端往右畫到 t（0~1）。用縮放 + 位移，不用 Image。</summary>
        private static void SetBarWidth(Transform bar, float t)
        {
            if (bar == null) return;

            float w = Mathf.Max(0.001f, t * BarWidth);
            var s = bar.localScale;
            bar.localScale = new Vector3(w, s.y, s.z);

            var p = bar.localPosition;
            bar.localPosition = new Vector3(BarLeft + w * 0.5f, p.y, p.z);
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
