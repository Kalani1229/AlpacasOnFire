using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Prank
{
    /// <summary>
    /// 大蔥。**驅趕**那條解法。
    ///
    /// 打中的人會被推開一段，螢幕震一下。推得動但推不遠 ——
    /// 它是「把牠趕到我要的位置」，不是「把牠打死」。
    ///
    /// 對 NPC 特別有用：羊被推著走的那 0.35 秒不需要你追，
    /// 可以把牠從樹後面趕到空地再動手。對隊友則是純粹的搗亂，
    /// 把正要交貨的人從窗口前面撞開。
    ///
    /// 不消耗、沒有蓄力，所以可以連打 —— 這是刻意的，連推很好笑。
    /// 揮打的動作也做得短（0.28 秒），連打才順。
    /// </summary>
    public class LeekTool : PrankTool
    {
        [Header("Leek")]
        [Tooltip("揮動的樞紐。整根蔥掛在它底下，揮打時繞著它轉。")]
        [SerializeField] private Transform _swingPivot;

        /// <summary>揮打動作的殘餘時間。走 [Networked] 才能讓所有人都看到這一揮。</summary>
        [Networked] private TickTimer SwingTimer { get; set; }

        /// <summary>揮打進度 0~1（0 = 剛揮出去，1 = 收完）。</summary>
        private float Swing01
        {
            get
            {
                float left = SwingTimer.RemainingTime(Runner) ?? 0f;
                if (left <= 0f) return 1f;
                return 1f - Mathf.Clamp01(left / Mathf.Max(0.01f, GameTuning.LeekSwingSeconds));
            }
        }

        protected override string ActionVerb => "打";

        protected override void Hit(IStaggerable target, in InteractionContext ctx)
        {
            // 推的方向 = 從打的人指向被打的人（正面推開，不是往任意方向彈）
            var anchor = target.StaggerAnchor;
            var dir = anchor != null ? anchor.position - ctx.Origin : ctx.Direction;

            target.ApplyKnockback(dir, GameTuning.LeekKnockbackSpeed, GameTuning.LeekKnockbackSeconds);
            GameAudio.PlayAt(SfxId.Throw, transform.position);
        }

        /// <summary>
        /// **打空也要揮。** 揮打動作綁在「按下右鍵」而不是「打中了」——
        /// 沒揮出去的話玩家不知道自己到底有沒有按到，會一直重按。
        /// </summary>
        public override bool TryUse(in InteractionContext ctx)
        {
            if (HasStateAuthority && SwingTimer.ExpiredOrNotRunning(Runner))
                SwingTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.LeekSwingSeconds);

            return base.TryUse(in ctx);
        }

        // ---------------- 揮打動畫 ----------------

        public override void Render()
        {
            base.Render();
            if (_swingPivot == null) return;

            // 由後往前砍：從舉高 -110° 掃到 +35°，然後留在收勢的位置。
            // 用 SmoothStep 讓中段最快 —— 等速掃看起來像在掃地，不像在打人。
            float t = Swing01;
            float angle = t >= 1f ? 0f : Mathf.Lerp(-110f, 35f, Mathf.SmoothStep(0f, 1f, t));

            _swingPivot.localRotation = Quaternion.Euler(angle, 0f, 0f);
        }
    }
}
