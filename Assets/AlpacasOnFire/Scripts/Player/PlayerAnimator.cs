using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Player
{
    /// <summary>
    /// 挑動畫。掛在玩家 prefab 的根物件上，Animator 在子物件（Visual）。
    ///
    /// **走路／待機交給 Animator 自己的轉場**：AC_Alpaca 裡 Idle ⇄ Run 用 bool 參數
    /// `isWalking` 切換。開始走路設 true、停下來設 false，只在「值變了」的那一幀設一次。
    ///
    /// **Work、Spit 是一次性的動作**：沒有接轉場線，用 CrossFade 直接指定 state，
    /// 播完再 CrossFade 回 Idle / Run（看當下 isWalking）。
    /// Spit 這個 state 目前 controller 裡還沒有 —— 美術把吐口水的 clip 拖進 AC_Alpaca、
    /// state 命名成 "Spit" 就會自動接上；沒有的話這支會安靜跳過，不吼錯。
    ///
    /// **用同步狀態驅動，不要用輸入驅動。**
    /// 如果讀 LocalInputProvider，你只會看到自己在跑、隊友全部在原地滑行 ——
    /// 那支只有本機玩家有值。改讀 NetworkCharacterController.Velocity、WorkTimer、
    /// SpitTimer，它們都是 [Networked]，每一端算出來都一樣。
    ///
    /// 判定放在 Render() 不是 FixedUpdateNetwork()：動畫是表現層，要跟畫面更新率走；
    /// Fusion 的重模擬會讓同一個 tick 跑好幾次，SetBool / CrossFade 會被重複觸發。
    ///
    /// 沒有 Animator 就整支靜默 —— 佔位膠囊版本沒有動畫，不該吼錯誤。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAnimator : NetworkBehaviour
    {
        // 這些名字要跟 AC_Alpaca.controller 裡的 state／參數名稱一致。
        private const string StateIdle = "Idle";
        private const string StateRun  = "Run";
        private const string StateWork = "Work";
        private const string StateSpit = "Spit";

        private static readonly int IsWalkingHash = Animator.StringToHash("isWalking");
        private static readonly int SpitHash      = Animator.StringToHash(StateSpit);
        private static readonly int WorkHash      = Animator.StringToHash(StateWork);

        private const float CrossFadeSeconds = 0.15f;

        /// <summary>吐口水動畫最長播多久。clip 本身比這短就照 clip 走，這只是保險。</summary>
        private const float SpitMaxSeconds = 2f;

        // 只能往後加（專案規則：enum 只增不改）
        private enum Pose : byte { None = 0, Idle, Run, Work, Spit }

        [Tooltip("留空會自己在子物件裡找。")]
        [SerializeField] private Animator _animator;

        private PlayerController _player;
        private NetworkCharacterController _ncc;

        private Pose _current = Pose.None;
        private bool? _walkingSent;       // 上次送給 Animator 的 isWalking（null = 還沒送過）
        private bool _hasWalkingParam;
        private bool _hasSpitState;

        private int? _lastSpitStamp;
        private float _spitStartedAt = -1f;   // < 0 表示現在沒有要播吐口水

        public override void Spawned()
        {
            _player = GetComponent<PlayerController>();
            _ncc = GetComponent<NetworkCharacterController>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);

            // 中途加入時，對方可能剛吐過 —— 先記下目前的值，不要一進場就播一次
            _lastSpitStamp = _player != null ? _player.SpitStamp : null;

            CacheCapabilities();
        }

        private void CacheCapabilities()
        {
            _hasWalkingParam = false;
            _hasSpitState = false;
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            foreach (var p in _animator.parameters)
                if (p.nameHash == IsWalkingHash && p.type == AnimatorControllerParameterType.Bool)
                    _hasWalkingParam = true;

            _hasSpitState = _animator.HasState(0, SpitHash);

            if (!_hasWalkingParam)
                Debug.LogWarning("[PlayerAnimator] AC_Alpaca 沒有 bool 參數 isWalking，走路動畫改用 CrossFade 切換。");
        }

        public override void Render()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            DetectSpit();

            // 倒地時 RagdollRig 會把 Animator 關掉（關節在接管骨頭）。
            // 這時候設參數／CrossFade 都沒有效果，**記憶的狀態必須歸零** ——
            // 不歸零的話爬起來之後以為已經在對的狀態，就不會再切，角色卡在最後一幀。
            if (!_animator.enabled)
            {
                _current = Pose.None;
                _walkingSent = null;
                _spitStartedAt = -1f;
                return;
            }

            bool walking = IsWalking();
            SendWalking(walking);

            var want = Evaluate(walking);
            if (want == _current)
            {
                if (want == Pose.Work) LoopWorkWhileHeld();
                return;
            }

            var from = _current;
            _current = want;

            // Idle ⇄ Run 之間的切換交給 Animator 的轉場（isWalking）。
            // 只有「從一次性動作回來」或「剛啟用」時才要自己 CrossFade 回走路／待機。
            if (_hasWalkingParam && IsLocomotion(want) && IsLocomotion(from))
                return;

            _animator.CrossFade(NameOf(want), CrossFadeSeconds, 0);
        }

        // ---------------------------------------------------------------- 判定

        private bool IsWalking()
        {
            // 倒地到完全站好之前都算停著 —— 被撞飛的速度不是在走路
            if (_player != null && _player.IsIncapacitated) return false;
            if (_ncc == null) return false;

            // 只看水平速度：重力讓 y 一直有值，算進去的話站著也會被判定成在走
            var v = _ncc.Velocity;
            return new Vector2(v.x, v.z).magnitude > GameTuning.AnimRunThreshold;
        }

        private void SendWalking(bool walking)
        {
            if (!_hasWalkingParam) return;
            if (_walkingSent == walking) return;   // 只在變化的那一幀設
            _animator.SetBool(IsWalkingHash, walking);
            _walkingSent = walking;
        }

        /// <summary>SpitTimer 換了新的結束 tick = 剛吐了一口。</summary>
        private void DetectSpit()
        {
            if (_player == null) return;
            var stamp = _player.SpitStamp;
            if (stamp == _lastSpitStamp) return;
            _lastSpitStamp = stamp;

            if (stamp.HasValue && _hasSpitState && !_player.IsIncapacitated)
                _spitStartedAt = Time.time;
        }

        /// <summary>
        /// 優先權：Work > Spit > Run / Idle。
        ///
        /// Work 壓在最上面是因為它是「有意義的動作」—— 一邊走一邊交貨時，
        /// 該看到的是交貨而不是跑步。反正它只有 0.45 秒。
        /// </summary>
        private Pose Evaluate(bool walking)
        {
            if (_player != null && !_player.WorkTimer.ExpiredOrNotRunning(Runner))
            {
                _spitStartedAt = -1f;   // 被 Work 蓋掉的吐口水就不補播了
                return Pose.Work;
            }

            if (SpitPlaying()) return Pose.Spit;

            return walking ? Pose.Run : Pose.Idle;
        }

        private bool SpitPlaying()
        {
            if (_spitStartedAt < 0f) return false;

            float t = Time.time - _spitStartedAt;
            if (t > SpitMaxSeconds) { _spitStartedAt = -1f; return false; }

            // 已經切進 Spit 了：播完（normalizedTime ≥ 1）就結束。
            // 還沒切進去（CrossFade 中或剛觸發）就先繼續算在播。
            if (_current == Pose.Spit && !_animator.IsInTransition(0))
            {
                var info = _animator.GetCurrentAnimatorStateInfo(0);
                if (info.shortNameHash == SpitHash && info.normalizedTime >= 1f)
                {
                    _spitStartedAt = -1f;
                    return false;
                }
                if (info.shortNameHash != SpitHash && t > CrossFadeSeconds * 2f)
                {
                    // 被別的東西打斷了（例如 Work），就不要再切回來
                    _spitStartedAt = -1f;
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 按住機台（紡線機）時，HoldTick 每個 tick 都會重新點 WorkTimer，所以 Work 會一直持續。
        /// 但 A_Alpaca_Work 沒有勾 Loop（按一下的短動作只要播一次），
        /// 按超過片段長度就會停在最後一格 —— 這裡在播完時從頭再播，看起來就是一直在做事。
        /// 按一下的動作只有 0.45 秒，比片段短，永遠碰不到這裡。
        /// </summary>
        private void LoopWorkWhileHeld()
        {
            if (_animator.IsInTransition(0)) return;
            var info = _animator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == WorkHash && info.normalizedTime >= 1f)
                _animator.Play(WorkHash, 0, 0f);
        }

        private static bool IsLocomotion(Pose p) => p == Pose.Idle || p == Pose.Run;

        private static string NameOf(Pose p) => p switch
        {
            Pose.Run  => StateRun,
            Pose.Work => StateWork,
            Pose.Spit => StateSpit,
            _         => StateIdle,
        };
    }
}
