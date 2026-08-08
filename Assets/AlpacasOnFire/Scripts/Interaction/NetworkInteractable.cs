using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Interaction
{
    /// <summary>
    /// 可互動物件的方便基底（機台類都繼承它）。
    /// 不強制——只要實作 IInteractable 就會被 PlayerInteractor 找到。
    /// </summary>
    public abstract class NetworkInteractable : NetworkBehaviour, IInteractable
    {
        [Header("Interaction")]
        [SerializeField] protected Transform _interactionAnchor;
        [SerializeField] protected int _priority = 0;

        public virtual Transform InteractionAnchor => _interactionAnchor != null ? _interactionAnchor : transform;
        public virtual int InteractionPriority => _priority;

        public abstract bool CanInteract(in InteractionContext ctx);
        public abstract string GetPrompt(in InteractionContext ctx);
        public abstract void Interact(in InteractionContext ctx);
    }
}
