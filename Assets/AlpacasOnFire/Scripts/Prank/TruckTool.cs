using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Prank
{
    /// <summary>
    /// 卡車。**強攻**那條解法，也是三個裡唯一的單次道具。
    ///
    ///     按住右鍵 -> 舉起來（看得見，最多 2 秒）
    ///        └─ 放開 -> 丟出去（**蓄多久就飛多遠**）
    ///              └─ 落地或撞到東西 -> 爆炸 -> 消失
    ///
    /// **沒蓄力也丟得出去，只是丟到腳邊。** 蓄力不是「能不能用」的開關，
    /// 是射程的旋鈕 —— 所以慌張的時候還是丟得出去（只是會炸到自己旁邊），
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

        /// <summary>已經舉了多久。走 [Networked] 才能讓被害者看到蓄力。</summary>
        [Networked] public float Charge { get; set; }

        /// <summary>爆炸動畫的殘餘時間。跑完就 Despawn。</summary>
        [Networked] private TickTimer BlastTimer { get; set; }

        /// <summary>已經引爆過了（避免爆炸動畫期間又被判定一次）。</summary>
        [Networked] private NetworkBool Exploded { get; set; }

        public float Charge01 =>
            Mathf.Clamp01(Charge / Mathf.Max(0.01f, GameTuning.TruckChargeSeconds));

        public bool IsExploding => Exploded;

        protected override string ActionVerb => "丟";

        // 丟出去這件事由 PrankTick 的「放開」負責，按下的那一刻不出手
        protected override bool IsReady(in InteractionContext ctx) => false;

        protected override string NotReadyPrompt(in InteractionContext ctx)
            => Charge <= 0.01f
                ? "[按住右鍵] 舉起卡車，放開丟出"
                : $"舉起來了… {Charge01 * 100f:F0}%（放開丟出）";

        /// <summary>爆炸中的卡車不能撿 —— 它下一刻就不存在了。</summary>
        public override bool CanInteract(in InteractionContext ctx)
            => !Exploded && base.CanInteract(in ctx);

        protected override void Hit(IStaggerable target, in InteractionContext ctx) { /* 走爆炸，不是直擊 */ }

        // ---------------- 蓄力與丟出 ----------------

        public override void PrankTick(in InteractionContext ctx, bool held, float deltaTime)
        {
            if (!HasStateAuthority || Exploded) return;

            if (held)
            {
                // 舉到滿就停在滿 —— 不自動丟出去，什麼時候放手是玩家的決定
                if (Charge01 < 1f) Charge += deltaTime;
                return;
            }

            if (Charge <= 0f) return;

            // 放開 = 丟出去。蓄力決定初速，也就決定射程。
            float speed = Mathf.Lerp(GameTuning.TruckThrowSpeedMin,
                                     GameTuning.TruckThrowSpeedMax, Charge01);
            Charge = 0f;

            var player = ctx.Player;
            if (player == null) return;

            player.Carry.ReleaseHeld();      // 交出去但不銷毀 —— 它要飛出去
            LaunchFrom(player, ctx.Direction, speed);
            GameAudio.PlayAt(SfxId.Throw, transform.position);
        }

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
            if (!HasStateAuthority) return;

            // 一旦飛出去就把蓄力歸零。
            // 這一行擋的是：舉到一半改用左鍵丟出去（或被隊友半空接走）的話，
            // 蓄力值會留在卡車身上；下一個人拿到它的那一個 tick，PrankTick 會看到
            // 「沒按著右鍵但 Charge > 0」而當成放手，卡車就自己飛出去了。
            if (InFlight && Charge > 0f) Charge = 0f;

            if (!Exploded) return;

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
                // 飛出去之後歸零，不然會歪著飛。
                float t = Exploded || InFlight ? 0f : Charge01;
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

        public override string DisplayName => Charge > 0.01f
            ? $"卡車（舉起 {Charge01 * 100f:F0}%）"
            : "卡車";
    }
}
