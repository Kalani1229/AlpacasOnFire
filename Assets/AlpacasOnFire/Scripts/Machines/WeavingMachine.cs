using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 織布機：吃 1–2 份**絲線**，織出「主色 + 點綴色」的衣服。
    ///
    /// 加了紡線機之後原料從羊毛換成絲線（素材箱 → 紡線機 → 織布機 → 交貨窗口）。
    /// 換掉的只有「認哪一種 ItemKind」，主色／點綴色、放入順序有差、
    /// 織製中可以補第二份，全部照舊 —— 底下的欄位仍然叫 WoolCount，
    /// 是為了不動存檔與既有的 prefab 序列化欄位。
    ///
    /// **這台機器是一道限時決策，沒有開始鈕。**
    ///
    ///     放入第一份線（主色）
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
        //
        // 放不進去的時候**直接交換**：把機台裡的東西換到手上，手上的放進去。
        //   手上絲線 + 成品待取        -> 拿走成品，絲線放進去開始織
        //   手上絲線 + 兩份線已經在織  -> 換出「半成品」（帶著已織秒數），絲線放進去重新開始
        //   手上半成品 + 機台有任何東西 -> 換出那個東西，半成品放進去從原進度繼續
        //   手上半成品 + 機台是空的     -> 直接放進去從原進度繼續
        // 還放得下的時候（第二份線）照舊加點綴色，不會觸發交換。

        private bool IsEmptyMachine => !Processing && !HasOutput && WoolCount == 0;

        private bool Fits(CarriableItem unfinished)
            => unfinished != null && unfinished.Spec.Pattern == _outputPattern;

        public override bool CanInteract(in InteractionContext ctx)
        {
            // 半成品一律讓它進得來：版型不對也要能顯示「這台織不了」
            if (ctx.HeldKind == ItemKind.UnfinishedGarment) return true;

            // 成品待取：空手取貨，或拿著絲線交換
            if (HasOutput) return ctx.IsEmptyHanded || ctx.HeldKind == ItemKind.Thread;

            // 拿著生羊毛時要讓它進得來，不然 FindTarget 濾掉之後
            // 「織布機只吃絲線」那句提示根本顯示不出來，玩家只看到機台沒反應。
            // Interact() 會擋掉，所以按下去仍然什麼都不會發生。
            if (ctx.HeldKind == ItemKind.Wool) return true;

            // 空手沒有別的意思 —— 沒有開始鈕。
            // 拿著絲線：放得下就放，放不下（兩份都在織）就交換
            return ctx.HeldKind == ItemKind.Thread;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.HeldKind == ItemKind.UnfinishedGarment)
            {
                if (!Fits(ctx.Held))
                    return $"這台只織{PlaceholderPalette.PatternName(_outputPattern)}";
                if (IsEmptyMachine)
                    return $"[Space] 放回半成品，從 {ctx.Held.Work01 * 100f:F0}% 繼續織";
                return HasOutput
                    ? $"[Space] 換出 {DescribeOutput()}，放回半成品"
                    : $"[Space] 換出織了 {Progress01 * 100f:F0}% 的半成品，放回手上的半成品";
            }

            if (HasOutput)
            {
                if (ctx.HeldKind == ItemKind.Thread)
                    return $"[Space] 換出 {DescribeOutput()}，放入{PlaceholderPalette.DyeName(ctx.Held.Spec.Color)}線開始織";
                return ctx.IsEmptyHanded
                    ? $"[Space] 取出 {DescribeOutput()}"
                    : "先空出雙手才能取出成品";
            }

            if (ctx.HeldKind == ItemKind.Thread)
            {
                if (WoolCount >= GameTuning.WeaveMaxWool)
                    return $"[Space] 換出織了 {Progress01 * 100f:F0}% 的半成品，" +
                           $"放入{PlaceholderPalette.DyeName(ctx.Held.Spec.Color)}線重新開始";

                if (!Processing)
                    return $"[Space] 放入{PlaceholderPalette.DyeName(ctx.Held.Spec.Color)}線開始織（主色）";

                // 織製中：告訴玩家還剩多少時間可以加第二份
                float left = ProcessTimer.RemainingTime(Runner) ?? 0f;
                return $"[Space] 加入{PlaceholderPalette.DyeName(ctx.Held.Spec.Color)}線當點綴（剩 {left:F1} 秒）";
            }

            // 拿著生羊毛過來的人要看得懂「還要先紡」，不然只會覺得機台壞了。
            // **排在織布中之前** —— 這句話比「還剩幾秒」重要得多。
            if (ctx.HeldKind == ItemKind.Wool)
                return "織布機只吃絲線 —— 先拿去紡線機紡成線";

            if (Processing)
            {
                float left = ProcessTimer.RemainingTime(Runner) ?? 0f;
                return WoolCount >= GameTuning.WeaveMaxWool
                    ? $"織布中… 還有 {left:F1} 秒"
                    : $"織布中… 還有 {left:F1} 秒可以加點綴色";
            }

            return "需要絲線（第一份是主色、第二份是點綴色）";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            // ---- 手上是半成品 ----
            if (ctx.HeldKind == ItemKind.UnfinishedGarment)
            {
                var held = ctx.Held;
                if (!Fits(held)) return;

                var spec = held.Spec;
                float elapsed = held.WorkSeconds;
                float total = held.WorkTotal;

                ctx.Player.Carry.ConsumeHeld();
                if (!IsEmptyMachine && !ExtractToHands(ctx.Player))
                {
                    // 換不出來：把半成品原樣放回腳邊，機台不動
                    DropBack(ctx.Player, ItemKind.UnfinishedGarment, spec, elapsed, total);
                    return;
                }

                ResumeUnfinished(spec, elapsed, total);
                ctx.Player.TriggerWork();
                return;
            }

            // ---- 空手取成品（原本的行為）----
            if (HasOutput && ctx.IsEmptyHanded)
            {
                TryTakeOutput(in ctx);
                return;
            }

            if (ctx.HeldKind != ItemKind.Thread) return;

            var colour = ctx.Held.Spec.Color;

            // ---- 放得下：照舊 ----
            if (CanAcceptWool)
            {
                ctx.Player.Carry.ConsumeHeld();
                ctx.Player.TriggerWork();   // 放入絲線算「在工作」
                InsertWool(colour);
                return;
            }

            // ---- 放不下：交換（成品待取，或兩份線已經在織）----
            ctx.Player.Carry.ConsumeHeld();
            if (!ExtractToHands(ctx.Player))
            {
                DropBack(ctx.Player, ItemKind.Thread, GarmentSpec.Create(PatternType.None, colour), 0f, 0f);
                return;
            }

            ctx.Player.TriggerWork();
            InsertWool(colour);
        }

        /// <summary>
        /// 把機台裡的東西交到玩家手上（手必須已經騰空）：
        ///   成品待取 -> 成品衣服
        ///   織製中   -> 半成品（版型、主色／點綴色、已織秒數、目標長度都帶走），機台清空
        /// </summary>
        private bool ExtractToHands(Player.PlayerController player)
        {
            if (HasOutput) return GiveOutputToHands(player);
            if (!Processing) return WoolCount == 0;   // 空的：沒東西要拿，算成功

            var spec = BuildOutputSpec();
            float elapsed = ElapsedSeconds;
            float total = ProcessDuration;

            var item = ItemFactory.SpawnIntoHands(Runner, ItemKind.UnfinishedGarment, spec, player,
                                                  Mathf.Max(0.01f, elapsed), total);
            if (item == null)
            {
                Debug.LogError("[織布機] 生不出半成品 —— GameCatalog 裡沒有 UnfinishedGarment 的 prefab。" +
                               "請執行選單「羊駝很忙 / 補上半成品衣服 prefab」。");
                return false;
            }

            CancelProcess();
            ClearSlots();
            GameAudio.PlayAt(SfxId.Pickup, transform.position);
            return true;
        }

        /// <summary>半成品放進來，從它自己的進度接著織。版型已經在呼叫端確認過。</summary>
        private void ResumeUnfinished(GarmentSpec spec, float elapsed, float total)
        {
            if (total <= 0f) total = GameTuning.WeaveSingleSeconds;

            MainColorRaw = (int)spec.Color;
            AccentColorRaw = (int)spec.AccentColor;
            // 目標長度就是當時放了幾份線的證據（主色、點綴色同色的雙色也分得出來）
            WoolCount = total >= GameTuning.WeaveDoubleSeconds - 0.01f ? 2 : 1;

            ResumeProcess(total, elapsed);
        }

        private void ClearSlots()
        {
            WoolCount = 0;
            MainColorRaw = 0;
            AccentColorRaw = 0;
        }

        /// <summary>交換失敗時的保險：手上那份東西原樣放回腳邊，不能讓它憑空消失。</summary>
        private void DropBack(Player.PlayerController player, ItemKind kind, GarmentSpec spec,
                              float work, float total)
        {
            var pos = player.transform.position + player.transform.forward * 0.6f + Vector3.up * 0.4f;
            ItemFactory.Spawn(Runner, kind, spec, pos, default, work, total);
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

            ClearSlots();
        }

        // ---------------- 被丟進來的絲線 ----------------

        /// <summary>
        /// **跟其他機台不一樣：織製中也收。** 理由見 CanAcceptWool 的註解。
        /// 只改織布機，縫紉機與果汁機維持原本的 `!Processing && !HasOutput`。
        /// </summary>
        public bool CanAcceptThrown(CarriableItem item)
        {
            if (item == null) return false;
            if (item.Kind == ItemKind.Thread) return CanAcceptWool;

            // 半成品丟進**空的**同版型織布機就接著織。丟的時候沒有手可以接換出來的東西，
            // 所以機台有東西就不收（跟絲線丟不進滿的機台是同一個道理）
            if (item.Kind == ItemKind.UnfinishedGarment) return Fits(item) && IsEmptyMachine;
            return false;
        }

        public bool AcceptThrown(CarriableItem item)
        {
            if (!HasStateAuthority || !CanAcceptThrown(item)) return false;

            if (item.Kind == ItemKind.UnfinishedGarment)
            {
                ResumeUnfinished(item.Spec, item.WorkSeconds, item.WorkTotal);
                return true;
            }

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
