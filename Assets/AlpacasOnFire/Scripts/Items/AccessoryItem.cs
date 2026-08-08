using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using UnityEngine;

namespace AlpacasOnFire.Items
{
    /// <summary>飾品：可以裝在地上的衣服，也可以裝在隊友／人偶身上穿著的衣服。</summary>
    public class AccessoryItem : CarriableItem, IItemUser, IGarmentHostUser
    {
        [SerializeField] private AccessoryType _accessoryType = AccessoryType.Button;

        public AccessoryType AccessoryType => _accessoryType;

        public override void Spawned()
        {
            base.Spawned();
            if (HasStateAuthority && Spec.Accessory == AccessoryType.None)
            {
                var s = Spec;
                s.Accessory = _accessoryType;
                Spec = s;
            }
        }

        public bool TryUseOnItem(CarriableItem target, in InteractionContext ctx, bool execute, out string prompt)
        {
            prompt = null;
            if (target == null || target.Kind != ItemKind.Garment) return false;
            if (target.Spec.Accessory != AccessoryType.None) return false;

            prompt = $"[Space] 裝上{PlaceholderPalette.AccessoryName(Spec.Accessory)}";
            if (!execute) return true;

            var spec = target.Spec;
            spec.Accessory = Spec.Accessory;
            target.Spec = spec;

            GameAudio.PlayAt(SfxId.AccessoryAttach, target.transform.position);
            ctx.Player.Carry.ConsumeHeld();
            return true;
        }

        public bool TryUseOnHost(IGarmentHost host, in InteractionContext ctx, bool execute, out string prompt)
        {
            prompt = null;
            if (host == null || !host.HasGarment) return false;
            if (host.Garment.Accessory != AccessoryType.None) return false;

            prompt = $"[Space] 裝上{PlaceholderPalette.AccessoryName(Spec.Accessory)}";
            if (!execute) return true;

            if (!host.TryTakeOff(out var spec)) return false;
            spec.Accessory = Spec.Accessory;
            host.TryWear(spec);

            GameAudio.PlayAt(SfxId.AccessoryAttach, ctx.Player.transform.position);
            ctx.Player.Carry.ConsumeHeld();
            return true;
        }
    }
}
