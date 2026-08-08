using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>箱子取得點：空手互動就拿到一個空箱子。</summary>
    public class BoxDispenser : NetworkInteractable
    {
        public override bool CanInteract(in InteractionContext ctx) => ctx.IsEmptyHanded;

        public override string GetPrompt(in InteractionContext ctx)
            => ctx.IsEmptyHanded ? "[Space] 拿一個空箱子" : "先空出雙手";

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !ctx.IsEmptyHanded) return;
            ItemFactory.SpawnIntoHands(Runner, ItemKind.Box, default, ctx.Player);
        }
    }
}
