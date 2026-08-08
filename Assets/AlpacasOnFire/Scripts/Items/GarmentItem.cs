using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using UnityEngine;

namespace AlpacasOnFire.Items
{
    /// <summary>做好的衣服。可以穿到隊友或人偶身上，也可以直接裝箱。</summary>
    public class GarmentItem : CarriableItem, IGarmentHostUser
    {
        public bool TryUseOnHost(IGarmentHost host, in InteractionContext ctx, bool execute, out string prompt)
        {
            prompt = null;
            if (host == null || host.HasGarment) return false;

            prompt = $"[Space] 幫它穿上 {Spec.Describe()}";
            if (!execute) return true;

            if (!host.TryWear(Spec)) return false;

            GameAudio.PlayAt(SfxId.DressOn, transform.position);
            ctx.Player.Carry.ConsumeHeld();
            return true;
        }
    }
}
