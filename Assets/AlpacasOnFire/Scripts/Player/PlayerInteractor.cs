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

        /// <summary>
        /// 掃出準心附近所有的 IInteractable。主要互動與次要互動共用這一段，
        /// 只是後續的篩選條件不同。
        /// </summary>
        private void CollectCandidates(in InteractionContext ctx)
        {
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
        }

        /// <summary>找目前準心對到、且在這個情境下真的可以互動的物件。唯讀，任何端都可以呼叫。</summary>
        public IInteractable FindTarget(out InteractionContext ctx)
        {
            ctx = BuildContext();
            CollectCandidates(in ctx);

            var origin = ctx.Origin;
            var dir = ctx.Direction;

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

        /// <summary>
        /// 互動範圍內、大致在面前，有沒有「任何」互動物 —— **不看 CanInteract**。
        ///
        /// 為什麼需要它：FindTarget() 已經把 CanInteract 為 false 的候選濾掉了，
        /// 所以「面前空無一物」與「面前有東西但現在不能用」從回傳值上分不出來。
        /// 左鍵的丟出分支必須分得出來 —— 機台滿了的時候按左鍵應該什麼都不做，
        /// 而不是把手上的毛往機台方向扔出去（同樣的畫面、同樣的按鍵，
        /// 卻因為一個看不見的狀態產生完全不同的結果）。
        ///
        /// 距離與面向的門檻**必須跟 FindTarget() 一模一樣**，
        /// 不然會出現「提示說可以丟、按下去卻不丟」的落差。
        ///
        /// 唯讀，任何端都可以呼叫（HUD 提示要用）。
        /// </summary>
        public bool HasAnyTargetInRange()
        {
            var ctx = BuildContext();
            CollectCandidates(in ctx);

            var origin = ctx.Origin;
            var dir = ctx.Direction;

            foreach (var candidate in _candidates)
            {
                // 自己手上的東西不算「面前有東西」。
                // 拿在手上的物件位置就在手上錨點，本來就在準心前方的判定範圍內；
                // 而且 CarriableItem.UpdateColliders() 只關掉非 trigger 的碰撞體，
                // 所以帶 trigger 的手持物（手提箱的 HeldProbe）被拿著時仍然掃得到。
                // 不排除的話「手上有東西」永遠等於「面前有東西」，第 3 條就永遠不會成立。
                if (ReferenceEquals(candidate, ctx.Held)) continue;

                var anchor = candidate.InteractionAnchor;
                if (anchor == null) continue;

                var to = anchor.position - origin;
                float dist = to.magnitude;
                if (dist > GameTuning.InteractRange + 0.6f) continue;
                if (dist > 0.05f && Vector3.Dot(to / dist, dir) < 0.2f) continue;

                return true;   // 有東西就夠了，能不能用不是這支的問題
            }
            return false;
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

        /// <summary>
        /// Space／左鍵被按下。只在 StateAuthority 上呼叫。
        /// 回傳有沒有真的互動到 —— 呼叫端要靠它決定要不要往下跑丟出。
        /// </summary>
        public bool TryInteract()
        {
            var target = FindTarget(out var ctx);
            if (target == null) return false;

            target.Interact(in ctx);
            return true;
        }

        /// <summary>
        /// 右鍵（次要互動）的目標。跟 FindTarget 分開找，因為兩者的
        /// CanInteract 條件不同 —— 同一個物件可能可以按右鍵但不能按 Space，反之亦然。
        /// 唯讀，任何端都可以呼叫（HUD 提示要用）。
        /// </summary>
        public ISecondaryInteractable FindSecondaryTarget(out InteractionContext ctx)
        {
            ctx = BuildContext();

            // 手上是持續使用型工具（噴槍、染劑刷）時，右鍵永遠屬於那個工具
            if (ctx.Held is Items.IHoldTool) return null;

            // 惡搞道具同理：拿著大蔥按右鍵就是打人，不會變成打開手提箱面板。
            // 這裡也擋一次是為了讓**提示字**一致 —— HUD 是靠 FindSecondaryTarget
            // 決定要不要顯示次要互動提示的，不擋的話會顯示一個按下去不會發生的動作。
            if (ctx.Held is Prank.PrankTool) return null;

            _candidates.Clear();
            CollectCandidates(in ctx);

            var origin = ctx.Origin;
            var dir = ctx.Direction;

            ISecondaryInteractable best = null;
            int bestPriority = int.MinValue;
            float bestDist = float.MaxValue;

            foreach (var candidate in _candidates)
            {
                if (candidate is not ISecondaryInteractable secondary) continue;

                var anchor = candidate.InteractionAnchor;
                if (anchor == null) continue;

                var to = anchor.position - origin;
                float dist = to.magnitude;
                if (dist > GameTuning.InteractRange + 0.6f) continue;
                if (dist > 0.05f && Vector3.Dot(to / dist, dir) < 0.2f) continue;
                if (!secondary.CanSecondaryInteract(in ctx)) continue;

                int p = candidate.InteractionPriority;
                if (p > bestPriority || (p == bestPriority && dist < bestDist))
                {
                    best = secondary;
                    bestPriority = p;
                    bestDist = dist;
                }
            }
            return best;
        }

        /// <summary>右鍵被按下。只在 StateAuthority 上呼叫。</summary>
        public void TrySecondaryInteract()
        {
            var target = FindSecondaryTarget(out var ctx);
            target?.SecondaryInteract(in ctx);
        }
    }
}
