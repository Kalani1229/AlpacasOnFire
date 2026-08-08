using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>飾品取得點。</summary>
    public class AccessoryDispenser : NetworkInteractable
    {
        [SerializeField] private AccessoryType _accessory = AccessoryType.Button;

        public AccessoryType Accessory => _accessory;

        public override bool CanInteract(in InteractionContext ctx) => ctx.IsEmptyHanded;

        public override string GetPrompt(in InteractionContext ctx)
            => ctx.IsEmptyHanded ? $"[Space] 拿{PlaceholderPalette.AccessoryName(_accessory)}" : "先空出雙手";

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !ctx.IsEmptyHanded) return;
            var spec = new GarmentSpec { AccessoryRaw = (int)_accessory };
            ItemFactory.SpawnIntoHands(Runner, ItemKind.Accessory, spec, ctx.Player);
        }
    }
}
