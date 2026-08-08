using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Player;
using UnityEngine;

namespace AlpacasOnFire.Items
{
    /// <summary>剃毛器：持有時面向隊友按 Space，隊友掉落羊毛。</summary>
    public class ShearsTool : CarriableItem, IGarmentHostUser
    {
        public bool TryUseOnHost(IGarmentHost host, in InteractionContext ctx, bool execute, out string prompt)
        {
            prompt = null;

            // 只能剃活的羊駝，人偶沒毛
            if (host is not PlayerController target) return false;
            if (target == ctx.Player) return false;
            if (target.Fleece <= 0)
            {
                prompt = "羊毛還沒長回來";
                return false;
            }

            prompt = $"[Space] 剃毛（{target.Fleece}/{GameTuning.FleeceMax}）";
            if (!execute) return true;

            return target.Shear(ctx.Player);
        }
    }
}
