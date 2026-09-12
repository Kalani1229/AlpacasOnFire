using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using UnityEngine;

namespace AlpacasOnFire.Prank
{
    /// <summary>
    /// 「準心前方誰會被打到」。
    ///
    /// 抽成獨立的一支，因為找目標這件事**不屬於任何道具** ——
    /// 口水是羊駝自帶的能力、根本沒有道具，但它要用同一套判定。
    /// 放在這裡之後，道具與內建能力找到的目標一定是同一個。
    /// </summary>
    public static class PrankTargeting
    {
        private static readonly Collider[] Buffer = new Collider[32];

        /// <summary>
        /// 準心前方最近的、打得到的目標。玩家與 NPC 都可能被選中。
        ///
        /// 面向的門檻（Dot >= 0.2）跟 PlayerInteractor 一致，只有距離放寬到
        /// PrankRange —— 追著跑的時候一般互動的 2.5 公尺實在追不到。
        /// </summary>
        public static IStaggerable FindTargetInFront(in InteractionContext ctx)
        {
            if (ctx.Player == null) return null;

            var origin = ctx.Origin;
            var dir = ctx.Direction;

            int count = Physics.OverlapSphereNonAlloc(
                origin + dir * (GameTuning.PrankRange * 0.5f),
                GameTuning.PrankRange * 0.65f,
                Buffer, ~0, QueryTriggerInteraction.Collide);

            IStaggerable best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (Buffer[i] == null) continue;
                var s = Buffer[i].GetComponentInParent<IStaggerable>();
                if (s == null || !s.CanBeStaggered) continue;
                if (ReferenceEquals(s, ctx.Player)) continue;   // 不能打自己

                var anchor = s.StaggerAnchor;
                if (anchor == null) continue;

                var to = anchor.position - origin;
                to.y = 0f;
                float d = to.magnitude;
                if (d > GameTuning.PrankRange) continue;
                if (d > 0.05f && Vector3.Dot(to / d, dir) < 0.2f) continue;

                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }
    }
}
