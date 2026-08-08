using AlpacasOnFire.Interaction;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 回收機。Phase 1 只是佔位方塊，沒有任何邏輯——
    /// 但已經是 IInteractable，Phase 2 直接在 Interact 裡補上回收邏輯即可，
    /// 不用動 PlayerInteractor 或任何既有程式碼。
    /// </summary>
    public class RecyclingMachine : NetworkInteractable
    {
        public override bool CanInteract(in InteractionContext ctx) => false;
        public override string GetPrompt(in InteractionContext ctx) => "回收機（Phase 2 開放）";
        public override void Interact(in InteractionContext ctx) { /* Phase 2 */ }
    }
}
