using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Npc
{
    /// <summary>
    /// NPC 的走路／待機切換。每種動物的 Animator Controller（AC_NPC_Bear / Capybara / Gorilla）
    /// 都有一個 bool 參數 `isWalking`，Idle ⇄ Walking 的轉場已經在 controller 裡拉好 ——
    /// 這裡只負責在「開始走」時設 true、「停下來」時設 false，而且只在值變了的那一幀設。
    ///
    /// 純 C#，由 WoolNpc / WildBeast 在 Render() 裡呼叫：
    ///  - 讀 NetworkCharacterController.Velocity（[Networked]），每一端看到的都一樣，
    ///    隊友那邊的羊也會正確地走／停。
    ///  - 放在 Render() 不是 FixedUpdateNetwork()，重模擬不會重複觸發。
    ///
    /// 沒有 Animator、controller 或 isWalking 參數（佔位方塊版）就整支靜默。
    /// </summary>
    public sealed class NpcLocomotionAnim
    {
        private static readonly int IsWalkingHash = Animator.StringToHash("isWalking");

        private Animator _animator;
        private bool _hasParam;
        private bool? _sent;

        public void Bind(Component owner)
        {
            _animator = owner.GetComponentInChildren<Animator>(true);
            _hasParam = false;
            _sent = null;

            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            foreach (var p in _animator.parameters)
                if (p.nameHash == IsWalkingHash && p.type == AnimatorControllerParameterType.Bool)
                    _hasParam = true;
        }

        /// <param name="ncc">移動來源。</param>
        /// <param name="suppressed">倒地之類「不該算在走」的狀態。</param>
        public void Tick(NetworkCharacterController ncc, bool suppressed)
        {
            if (!_hasParam || _animator == null) return;

            // Animator 被關掉（或物件還沒啟用）時設參數沒有效果，記憶要清掉，
            // 重新啟用後才會再送一次正確的值
            if (!_animator.isActiveAndEnabled) { _sent = null; return; }

            bool walking = false;
            if (!suppressed && ncc != null)
            {
                // 只看水平速度：重力讓 y 一直有值
                var v = ncc.Velocity;
                walking = new Vector2(v.x, v.z).magnitude > GameTuning.AnimRunThreshold;
            }

            if (_sent == walking) return;
            _animator.SetBool(IsWalkingHash, walking);
            _sent = walking;
        }
    }
}
