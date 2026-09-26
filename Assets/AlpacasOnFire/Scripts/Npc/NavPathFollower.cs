using AlpacasOnFire.Map;
using UnityEngine;
using UnityEngine.AI;

namespace AlpacasOnFire.Npc
{
    /// <summary>
    /// 沿著 NavMesh 路徑走的共用邏輯。**純 C#，不是 NetworkBehaviour。**
    ///
    /// 路徑存在普通欄位，不加 [Networked] —— 位置本來就由 NetworkCharacterController 同步，
    /// 路徑是「怎麼走過去」的過程，不是需要同步的狀態。
    ///
    /// 用法：
    /// <code>
    /// // 只在 HasStateAuthority 且 Runner.IsForward 的 tick 呼叫
    /// follower.Recalculate(transform.position, target, Runner.SimulationTime);
    /// var dir = follower.Steer(transform.position, arrive);   // 餵進 _ncc.Move(dir)
    /// </code>
    ///
    /// 算不出路徑（被建築圍死、沒有 NavMesh）時 Recalculate 回 false，
    /// 呼叫端退回原本的直線行為 —— 不要讓 NPC 整個停住。
    /// </summary>
    public class NavPathFollower
    {
        /// <summary>同一個目標多久重算一次。每個 tick 都算太浪費。</summary>
        public const float RecalcInterval = 0.5f;

        // **不能在這裡 new。** NavPathFollower 是當成 MonoBehaviour 的欄位初始化的
        // （Customer、WoolNpc 裡的 `= new NavPathFollower()`），而 Unity 禁止在
        // MonoBehaviour 建構階段建立 NavMeshPath —— 會丟 UnityException。
        // 第一次真的要算路徑時才建立（那時候一定已經在 FixedUpdateNetwork 裡了）。
        private NavMeshPath _path;
        private Vector3[] _corners = new Vector3[16];
        private int _cornerCount;
        private int _index;

        private Vector3 _target;
        private float _lastCalcTime = float.NegativeInfinity;
        private bool _hasTarget;

        public bool HasPath => _cornerCount > 0 && _index < _cornerCount;

        public Vector3 CurrentWaypoint => HasPath ? _corners[_index] : _target;

        /// <summary>
        /// 重算路徑。**只在 StateAuthority + IsForward 呼叫。**
        ///
        /// 目標沒變、而且距離上次算不到 RecalcInterval 秒，就沿用舊的路徑（回傳上次的結果）。
        /// now 用 Runner.SimulationTime —— 不要用 Time.time，那跟網路 tick 不同步。
        /// </summary>
        public bool Recalculate(Vector3 from, Vector3 to, float now, bool force = false)
        {
            bool sameTarget = _hasTarget && (to - _target).sqrMagnitude < 0.25f;
            if (!force && sameTarget && now - _lastCalcTime < RecalcInterval) return HasPath;

            _target = to;
            _hasTarget = true;
            _lastCalcTime = now;

            if (!NavUtil.HasNavMesh) { Clear(keepTarget: true); return false; }

            // 起點與終點都先落到 NavMesh 上：站在路緣上、目標點在建築牆邊，
            // 直接丟進 CalculatePath 會因為「不在 NavMesh 上」而失敗
            if (!NavUtil.SnapToNavMesh(from, 2f, out var a) || !NavUtil.SnapToNavMesh(to, 3f, out var b))
            {
                Clear(keepTarget: true);
                return false;
            }

            _path ??= new NavMeshPath();

            if (!NavMesh.CalculatePath(a, b, NavUtil.Filter, _path) || _path.status != NavMeshPathStatus.PathComplete)
            {
                // PathPartial 也算失敗：只走到一半會讓 NPC 卡在最接近的那面牆前面
                Clear(keepTarget: true);
                return false;
            }

            int n = _path.GetCornersNonAlloc(_corners);
            while (n >= _corners.Length)
            {
                _corners = new Vector3[_corners.Length * 2];
                n = _path.GetCornersNonAlloc(_corners);
            }

            _cornerCount = n;
            _index = n > 1 ? 1 : 0;   // 第 0 個是起點本身
            return HasPath;
        }

        /// <summary>
        /// 這一步該往哪走的水平單位向量。已經到終點回傳 Vector3.zero。
        /// arriveRadius 是「到了某個轉角就換下一個」的距離。
        /// </summary>
        public Vector3 Steer(Vector3 currentPosition, float arriveRadius)
        {
            while (HasPath)
            {
                var to = _corners[_index] - currentPosition;
                to.y = 0f;

                // 最後一個轉角要真的走到；中間的轉角靠近就換下一個，免得在轉角繞圈
                bool last = _index == _cornerCount - 1;
                float r = last ? arriveRadius : Mathf.Max(arriveRadius, 0.8f);

                if (to.magnitude > r) return to.normalized;
                _index++;
            }
            return Vector3.zero;
        }

        public void Clear() => Clear(keepTarget: false);

        private void Clear(bool keepTarget)
        {
            _cornerCount = 0;
            _index = 0;
            if (!keepTarget) { _hasTarget = false; _lastCalcTime = float.NegativeInfinity; }
        }
    }
}
