using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Player
{
    /// <summary>
    /// 從畫面中心（＝角色朝向）發射射線，找出最合適的 IInteractable。
    ///
    /// 這個類別「不認識」任何具體的互動類型——它只負責找目標、問 CanInteract、呼叫 Interact。
    /// 要新增互動類型時不需要改這裡。
    /// </summary>
    public class PlayerInteractor
    {
        private readonly PlayerController _player;
        private static readonly Collider[] OverlapBuffer = new Collider[32];
        private readonly List<IInteractable> _candidates = new();

        public PlayerInteractor(PlayerController player) => _player = player;

        public InteractionContext BuildContext()
        {
            var origin = _player.HeadAnchor.position;
            var dir = _player.AimDirection;
            return new InteractionContext(_player, _player.Carry.Held, origin, dir, _player.Runner);
        }

        /// <summary>找目前準心對到、且在這個情境下真的可以互動的物件。唯讀，任何端都可以呼叫。</summary>
        public IInteractable FindTarget(out InteractionContext ctx)
        {
            ctx = BuildContext();
            _candidates.Clear();

            var origin = ctx.Origin;
            var dir = ctx.Direction;

            // 沿準心方向的 SphereCast（主要判定）
            var hits = Physics.SphereCastAll(origin, GameTuning.InteractProbeRadius, dir,
                                             GameTuning.InteractRange, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits) Collect(h.collider);

            // 腳邊容錯：正前方一小段距離內的球形範圍
            int count = Physics.OverlapSphereNonAlloc(origin + dir * (GameTuning.InteractRange * 0.5f),
                                                      GameTuning.InteractRange * 0.55f,
                                                      OverlapBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++) Collect(OverlapBuffer[i]);

            IInteractable best = null;
            int bestPriority = int.MinValue;
            float bestDist = float.MaxValue;

            foreach (var candidate in _candidates)
            {
                var anchor = candidate.InteractionAnchor;
                if (anchor == null) continue;

                var to = anchor.position - origin;
                float dist = to.magnitude;
                if (dist > GameTuning.InteractRange + 0.6f) continue;
                if (dist > 0.05f && Vector3.Dot(to / dist, dir) < 0.2f) continue; // 必須大致在面前
                if (!candidate.CanInteract(in ctx)) continue;

                int p = candidate.InteractionPriority;
                if (p > bestPriority || (p == bestPriority && dist < bestDist))
                {
                    best = candidate;
                    bestPriority = p;
                    bestDist = dist;
                }
            }
            return best;
        }

        private void Collect(Collider col)
        {
            if (col == null) return;
            var interactable = col.GetComponentInParent<IInteractable>();
            if (interactable == null) return;
            if (ReferenceEquals(interactable, _player)) return;                 // 不能對自己互動
            if (interactable is Component c && c.transform.IsChildOf(_player.transform)) return;

            // Fusion 的生成是延遲的：Runner.Spawn() 之後 GameObject 與碰撞體會先存在，
            // Spawned() 要等到模擬迴圈才跑。在那個空窗期讀 [Networked] 屬性會直接丟
            // InvalidOperationException，而 GameHud 每一幀都呼叫 FindTarget -> CanInteract，
            // 一定會撞上（機台改成執行期生成之後才會暴露這個問題，場景物件時期沒有空窗期）。
            //
            // 統一在這裡擋掉還沒 Spawned、或已經 Despawned 的物件，
            // 這樣任何 IInteractable 實作都不必各自寫防呆。
            if (interactable is NetworkBehaviour nb && (nb.Object == null || !nb.Object.IsValid)) return;

            if (!_candidates.Contains(interactable)) _candidates.Add(interactable);
        }

        /// <summary>Space 被按下。只在 StateAuthority 上呼叫。</summary>
        public void TryInteract()
        {
            var target = FindTarget(out var ctx);
            target?.Interact(in ctx);
        }
    }
}
