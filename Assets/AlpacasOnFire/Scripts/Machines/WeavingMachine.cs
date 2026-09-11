using System.Collections.Generic;
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
    /// 進度條走 MachineBase 的預設：**整條就是目前這件衣服的完成點**。
    /// 單色時整條 = 4 秒；加了第二份毛之後整條 = 7 秒，條子會往回跳一次
    /// （終點被推遠了，已完成的比例本來就變小）。
    ///
    /// **順序有差**：紅→藍 是「紅底藍紋」，藍→紅 是「藍底紅紋」，兩件不同的衣服。
    ///
    /// 「做好留在機台、手動取貨、旁邊有朝外送的輸送帶就自動出貨」整套都是
    /// MachineBase 既有的，這裡一行都不用重寫。
    /// </summary>
    public class WeavingMachine : MachineBase, IThrownItemReceiver
    {
        /// <summary>
        /// 場上所有的織布機。顧客系統要靠它決定「現在做得出哪些版型」——
        /// 沒擺襯衫織布機就不該出襯衫訂單。
        ///
        /// 不走 DeployableDevice.All 是因為 DeployableDevice 掛在 DeployHandle **子物件**上，
        /// `dev.GetComponent&lt;WeavingMachine&gt;()` 會拿不到（機台本體在父物件）。
        /// </summary>
        public static readonly List<WeavingMachine> All = new();

        [Header("Weaving")]
        [Tooltip("這台機器產出的版型。一台機器只做一種 —— 想要兩種版型就得擺兩台。")]
        [SerializeField] private PatternType _outputPattern = PatternType.TShirt;

        /// <summary>這台織得出哪一種衣服。</summary>
        public PatternType OutputPattern => _outputPattern;

        [Tooltip("機體上的兩格色塊：第 0 格主色、第 1 格點綴色。長度要是 2。")]
        [SerializeField] private Renderer[] _woolSlotVisuals;

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

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            base.Spawned();
            if (!All.Contains(this)) All.Add(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

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
        /// 進度條走 MachineBase 的預設畫法，跟其他機台一樣。
        ///
        /// 整條 = 目前這件衣服的完成點：
        ///   只放一份毛 -> 整條 = 單色的 4 秒
        ///   放了第二份 -> 整條 = 雙色的 7 秒
        ///
        /// **加入第二份毛的那一刻，填色會往回跳**（第 3 秒時 3/4 = 75% 變成 3/7 = 43%）。
        /// 這是這個表示法的必然結果，不是 bug：終點被推遠了，所以「已完成的比例」
        /// 本來就變小。它同時也是最直接的回饋 —— 玩家看得出自己剛剛買了更多時間。
        /// </summary>
        public override void Render()
        {
            base.Render();
            RenderWoolSlots();
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
