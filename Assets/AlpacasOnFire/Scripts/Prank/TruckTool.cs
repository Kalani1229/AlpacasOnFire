using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Prank
{
    /// <summary>
    /// 卡車。**強攻**那條解法，也是三個裡唯一的單次道具。
    ///
    ///     按住 Q -> 舉起來（看得見，蓄滿要 2 秒）
    ///        └─ 放開 -> 丟出去（**蓄多久就飛多遠**）
    ///              └─ 落地或撞到東西 -> 爆炸 -> 消失
    ///
    /// **它不需要自己的按鍵，也不是武器。** 卡車就是一個「很重的可丟物」，
    /// 走的是所有東西都在走的 Q 蓄力丟出，只是它的 ThrowChargeSeconds 是 2 秒
    /// （羊毛 0.15）。左鍵對它沒有作用 —— 拿著卡車還是可以操作機台、撿東西。
    ///
    /// 沒蓄滿也丟得出去，只是丟到腳邊。蓄力不是「能不能用」的開關，是射程的旋鈕，
    /// 所以慌張的時候還是丟得出去（只是會炸到自己旁邊），
    /// 想砸遠處那隻紅毛羊就得站著舉滿兩秒、讓所有人看到你要出手。
    ///
    /// 爆炸是**範圍**的：半徑內所有人一起倒、身上的毛全部掉出來。
    /// 這是它跟大蔥最大的差別 —— 大蔥一次一個，卡車一次一片。
    ///
    /// 倒地只有 1.1 秒。笑點在爬起來的過程與散落一地的毛，不在被按在地上的那段。
    /// </summary>
    public class TruckTool : PrankTool
    {
        [Header("Truck")]
        [Tooltip("車體。蓄力時會抬高，讓所有人都看得到有人要出手。")]
        [SerializeField] private Transform _liftVisual;

        [Tooltip("爆炸的火球。平常關著，炸的時候脹大再消失。")]
        [SerializeField] private GameObject _blastVisual;

        /// <summary>爆炸動畫的殘餘時間。跑完就 Despawn。</summary>
        [Networked] private TickTimer BlastTimer { get; set; }

        /// <summary>已經引爆過了（避免爆炸動畫期間又被判定一次）。</summary>
        [Networked] private NetworkBool Exploded { get; set; }

        public bool IsExploding => Exploded;

        /// <summary>卡車很重：蓄滿要 2 秒（羊毛 0.15）。</summary>
        public override float ThrowChargeSeconds => GameTuning.ThrowChargeTruck;

        /// <summary>
        /// 左鍵也能蓄力丟。拿著卡車的時候左鍵沒有別的事好做 ——
        /// 它不是武器（IsUsable 是 false），唯一的用法就是丟出去，
        /// 所以兩個鍵都給它，不用特地去找 Q。
        /// </summary>
        public override bool ChargesOnPrimary => true;

        protected override string ActionVerb => "丟";

        /// <summary>
        /// **卡車不是武器。** 回 false 讓左鍵整個讓給情境互動 ——
        /// 拿著卡車還是要能操作機台、撿東西、跟隊友互動。
        /// 它唯一的用法是 Q 蓄力丟出去。
        /// </summary>
        protected override bool IsUsable(in InteractionContext ctx) => false;

        /// <summary>爆炸中的卡車不能撿 —— 它下一刻就不存在了。</summary>
        public override bool CanInteract(in InteractionContext ctx)
            => !Exploded && base.CanInteract(in ctx);

        protected override void Hit(IStaggerable target, in InteractionContext ctx) { /* 走爆炸，不是直擊 */ }

        // ---------------- 爆炸 ----------------

        /// <summary>落地或撞到東西 -> 引爆。CarriableItem 的飛行結束掛鉤。</summary>
        protected override void OnFlightEnded(bool hitSomething, Vector3 point)
        {
            if (!HasStateAuthority || Exploded) return;
            Explode(point);
        }

        private static readonly Collider[] Buffer = new Collider[32];

        private void Explode(Vector3 center)
        {
            Exploded = true;
            BlastTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.TruckBlastSeconds);
            GameAudio.PlayAt(SfxId.ShipFail, center);

            // 範圍傷害：半徑內每一個 IStaggerable 都倒，而且毛全部掉出來。
            //
            // **丟的人自己也算。這是刻意不排除的。**
            // 卡車是唯一有自傷風險的道具，那就是它的代價 —— 沒蓄滿力就丟出去的話：
            //   初速  5（沒蓄力）-> 落點 1.8 公尺 -> 在 3.4 的爆炸半徑內 -> 炸到自己
            //   初速 18（蓄滿）  -> 落點 9.2 公尺 -> 安全
            // 所以「站著舉滿兩秒」不只是為了丟得遠，也是為了不要炸到自己。
            // 慌張亂丟＝自己倒地、自己的毛掉一地，那正是它好笑的地方。
            //
            // 用 HashSet 語意去重 —— 一個目標身上可能有好幾顆碰撞體
            // （身體 + DeployHandle + 各種 trigger），不濾的話會被打好幾次。
            int count = Physics.OverlapSphereNonAlloc(center, GameTuning.TruckBlastRadius, Buffer,
                                                      ~0, QueryTriggerInteraction.Collide);

            _hitThisBlast.Clear();
            for (int i = 0; i < count; i++)
            {
                if (Buffer[i] == null) continue;

                var target = Buffer[i].GetComponentInParent<IStaggerable>();
                if (target == null || !target.CanBeStaggered) continue;
                if (_hitThisBlast.Contains(target)) continue;
                _hitThisBlast.Add(target);

                var anchor = target.StaggerAnchor;
                var away = anchor != null ? anchor.position - center : Vector3.forward;

                target.ApplyKnockback(away, GameTuning.TruckKnockbackSpeed, GameTuning.LeekKnockbackSeconds);
                target.ApplyStagger(GameTuning.TruckStaggerSeconds, dropWool: true);
            }
        }

        private readonly System.Collections.Generic.HashSet<IStaggerable> _hitThisBlast = new();

        public override void FixedUpdateNetwork()
        {
            base.FixedUpdateNetwork();
            if (!HasStateAuthority || !Exploded) return;

            // 動畫演完就收掉。Despawn 之後不能再碰任何欄位，所以放在最後。
            if (BlastTimer.Expired(Runner)) Runner.Despawn(Object);
        }

        // ---------------- 外觀 ----------------

        public override void Render()
        {
            base.Render();

            if (_liftVisual != null)
            {
                // 舉高 + 微微後仰，遠遠就看得出「那個人要丟東西了」。
                //
                // 蓄力值現在住在拿著它的那個玩家身上（PlayerCarry.ThrowCharge01），
                // 不是卡車自己的欄位 —— Q 蓄力是所有可丟物共用的機制，
                // 卡車只是把它畫出來。飛出去或爆炸時歸零，不然會歪著飛。
                float t = Exploded || InFlight ? 0f : HolderThrowCharge01;
                _liftVisual.localPosition = new Vector3(0f, t * 0.75f, 0f);
                _liftVisual.localRotation = Quaternion.Euler(-t * 28f, 0f, 0f);
            }

            RenderBlast();
        }

        /// <summary>
        /// 爆炸：火球瞬間脹到半徑大小再淡掉，同時車體縮小消失。
        /// 沒有粒子系統，就用一顆會脹大的球 —— 佔位美術的原則是「看得懂」而不是「好看」。
        /// </summary>
        private void RenderBlast()
        {
            if (_blastVisual == null) return;

            if (!Exploded)
            {
                if (_blastVisual.activeSelf) _blastVisual.SetActive(false);
                return;
            }

            if (!_blastVisual.activeSelf) _blastVisual.SetActive(true);

            float left = BlastTimer.RemainingTime(Runner) ?? 0f;
            float t = 1f - Mathf.Clamp01(left / Mathf.Max(0.01f, GameTuning.TruckBlastSeconds));

            // 前 40% 脹大，之後維持並讓車體縮掉
            float grow = Mathf.Clamp01(t / 0.4f);
            _blastVisual.transform.localScale =
                Vector3.one * (GameTuning.TruckBlastRadius * 2f * Mathf.SmoothStep(0.2f, 1f, grow));

            if (_liftVisual != null)
                _liftVisual.localScale = Vector3.one * Mathf.Clamp01(1f - t);
        }

        /// <summary>拿著它的人目前的 Q 蓄力進度。沒人拿著就是 0。</summary>
        private float HolderThrowCharge01
        {
            get
            {
                var holder = Holder;
                return holder != null && holder.Carry != null ? holder.Carry.ThrowCharge01 : 0f;
            }
        }

        public override string DisplayName
        {
            get
            {
                float t = HolderThrowCharge01;
                return t > 0.01f ? $"卡車（舉起 {t * 100f:F0}%）" : "卡車";
            }
        }
    }
}
