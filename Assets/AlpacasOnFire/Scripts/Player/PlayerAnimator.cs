using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Player
{
    /// <summary>
    /// 挑動畫。掛在玩家 prefab 的根物件上，Animator 在子物件（Visual）。
    ///
    /// **用同步狀態驅動，不要用輸入驅動。**
    /// 如果讀 LocalInputProvider，你只會看到自己在跑、隊友全部在原地滑行 ——
    /// 那支只有本機玩家有值。改讀 NetworkCharacterController.Velocity，
    /// 它是 [Networked] 的，每一端算出來都一樣。
    ///
    /// 而且判定放在 Render() 不是 FixedUpdateNetwork()：
    /// 動畫是表現層，要跟畫面更新率走；放在網路 tick 裡會在低 tick rate 下一頓一頓的，
    /// 而且 Fusion 的重模擬會讓同一個 tick 跑好幾次，CrossFade 會被重複觸發。
    ///
    /// 沒有 Animator 就整支靜默 —— 佔位膠囊版本沒有動畫，不該吼錯誤。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimator : NetworkBehaviour
    {
        // 這三個名字要跟 AC_Alpaca.controller 裡的 state 名稱一致。
        // 那個 controller 沒有參數也沒有轉場（美術只是把 clip 放進去），
        // 所以我們用 CrossFade 直接指定 state —— 不用改 YAML、不用手拉轉場線，
        // 之後美術加新 clip 也只是多一行對應。
        private const string StateIdle = "Idle";
        private const string StateRun  = "Run";
        private const string StateWork = "Work";

        private const float CrossFadeSeconds = 0.15f;

        private enum Pose : byte { None = 0, Idle, Run, Work }

        [Tooltip("留空會自己在子物件裡找。")]
        [SerializeField] private Animator _animator;

        private PlayerController _player;
        private NetworkCharacterController _ncc;
        private Pose _current = Pose.None;

        public override void Spawned()
        {
            _player = GetComponent<PlayerController>();
            _ncc = GetComponent<NetworkCharacterController>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
        }

        public override void Render()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            // 倒地時 RagdollRig 會把 Animator 關掉（關節在接管骨頭）。
            // 這時候 CrossFade 是沒有效果的，但**記憶的狀態必須歸零** ——
            // 不歸零的話爬起來之後 want 仍然等於 _current，這支就再也不會
            // CrossFade，角色會卡在 ragdoll 結束的那一幀不動。
            if (!_animator.enabled)
            {
                _current = Pose.None;
                return;
            }

            var want = Evaluate();

            // **狀態沒變就不要再 CrossFade。** 每一幀都呼叫的話動畫會一直從頭開始，
            // 看起來就是卡在第一幀抖動。
            if (want == _current) return;
            _current = want;

            _animator.CrossFade(NameOf(want), CrossFadeSeconds, 0);
        }

        /// <summary>
        /// 優先權：Work > Run > Idle。
        ///
        /// Work 壓在最上面是因為它是「有意義的動作」—— 一邊走一邊交貨時，
        /// 該看到的是交貨而不是跑步。反正它只有 0.45 秒。
        /// </summary>
        private Pose Evaluate()
        {
            if (_player != null && !_player.WorkTimer.ExpiredOrNotRunning(Runner))
                return Pose.Work;

            if (_ncc != null)
            {
                // 只看水平速度：重力讓 y 一直有值，算進去的話站著也會被判定成在跑
                var v = _ncc.Velocity;
                float planar = new Vector2(v.x, v.z).magnitude;
                if (planar > GameTuning.AnimRunThreshold) return Pose.Run;
            }

            return Pose.Idle;
        }

        private static string NameOf(Pose p) => p switch
        {
            Pose.Run  => StateRun,
            Pose.Work => StateWork,
            _         => StateIdle,
        };
    }
}
