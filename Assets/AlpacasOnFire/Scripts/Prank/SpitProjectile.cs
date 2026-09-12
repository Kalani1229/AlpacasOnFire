using AlpacasOnFire.Core;
using AlpacasOnFire.Player;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Prank
{
    /// <summary>
    /// 飛出去的那口口水。
    ///
    /// 它**不是** CarriableItem —— 撿得起來的口水很奇怪，而且它的生命週期
    /// 只有一秒多，走不到「掉在地上等人撿」那條路。所以自己走一套簡單的拋物線。
    ///
    /// 改成看得見的投射物之後，口水從「按了就中」變成**要瞄準**的能力：
    /// 飛行需要時間、會往下掉、會落空。這才配得上它零成本、無限量的定位。
    ///
    /// 只有狀態權威在算飛行與命中，位置靠 NetworkTransform 同步出去。
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class SpitProjectile : NetworkBehaviour
    {
        [Header("Spit")]
        [Tooltip("視覺本體。會沿飛行方向被拉長成一道殘影，不然太快看不見。")]
        [SerializeField] private Transform _visual;

        [Networked] private Vector3 Velocity { get; set; }
        [Networked] private float Elapsed { get; set; }

        /// <summary>誰吐的。不能噴到自己。</summary>
        [Networked] private NetworkId ShooterId { get; set; }

        /// <summary>由 PlayerController.TrySpit() 在生成回呼裡呼叫。</summary>
        public void Launch(PlayerController shooter, Vector3 direction)
        {
            Velocity = direction.normalized * GameTuning.SpitSpeed;
            Elapsed = 0f;
            if (shooter != null && shooter.Object != null) ShooterId = shooter.Object.Id;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            float dt = Runner.DeltaTime;
            Elapsed += dt;

            if (Elapsed > GameTuning.SpitLifeSeconds) { Runner.Despawn(Object); return; }

            var v = Velocity + Vector3.down * (GameTuning.SpitGravity * dt);
            var next = transform.position + v * dt;

            // 先看有沒有噴到人 —— 人比牆重要，不然貼著牆站的人永遠噴不到
            if (TryHitSomeone(transform.position, next)) { Runner.Despawn(Object); return; }

            // 撞到地形就化掉。用 Linecast 而不是每幀檢查位置，
            // 免得速度快的時候直接穿過薄牆。
            //
            // **一定要排掉吐的人自己。** 初速加到 65 之後，一個 tick 的線段長 1.08 公尺
            // （原本只有 0.22），往下瞄的時候這條線會直接穿過自己的膠囊 ——
            // 於是口水在第一個 tick 就被判定撞牆而消失，看起來就是「E 按了沒反應」。
            // 這是把速度調成五倍之後才冒出來的問題。
            if (Physics.Linecast(transform.position, next, out var hit, ~0, QueryTriggerInteraction.Ignore)
                && !IsShooter(hit.collider))
            {
                Runner.Despawn(Object);
                return;
            }

            Velocity = v;
            transform.position = next;
        }

        /// <summary>這個碰撞體是不是吐口水的人自己的。</summary>
        private bool IsShooter(Collider col)
        {
            if (col == null) return false;
            var nb = col.GetComponentInParent<NetworkBehaviour>();
            return nb != null && nb.Object != null && nb.Object.Id == ShooterId;
        }

        // ---------------- 外觀 ----------------

        /// <summary>
        /// 沿飛行方向拉長成一道殘影。
        ///
        /// 為什麼需要這個：初速 65 之下一個畫格（1/60 秒）移動 1.08 公尺，
        /// 而球本身只有 0.3 公尺 —— 每一格都跳出自己身長的三倍多，
        /// 眼睛看到的是「什麼都沒有」而不是一顆球。子彈曳光的老招：
        /// 把它拉長到至少填滿這一格的位移，視覺上就連成一條線了。
        /// </summary>
        public override void Render()
        {
            if (_visual == null) return;

            var v = Velocity;
            float speed = v.magnitude;
            if (speed < 0.01f) return;

            transform.rotation = Quaternion.LookRotation(v / speed, Vector3.up);

            // 這一格會走多遠，就拉多長（至少 1 倍，不要縮小）
            float step = speed * Time.deltaTime;
            float stretch = Mathf.Max(1f, step / Mathf.Max(0.01f, BaseLength));

            _visual.localScale = new Vector3(1f, 1f, stretch);
        }

        /// <summary>視覺本體原始的 z 長度（prefab 上的球直徑）。</summary>
        private const float BaseLength = 0.3f;

        private static readonly RaycastHit[] Buffer = new RaycastHit[16];

        /// <summary>
        /// 這一個 tick 的移動路徑上有沒有噴到人。
        ///
        /// **一定要沿路徑掃，不能只檢查終點。** 初速 65 的話一個 tick 會走 1.08 公尺，
        /// 比命中半徑（0.55）還大 —— 只看終點的話，站在兩個取樣點中間的人會被整顆
        /// 口水跨過去，變成「明明對準了卻沒中」。
        /// </summary>
        private bool TryHitSomeone(Vector3 from, Vector3 to)
        {
            var delta = to - from;
            float dist = delta.magnitude;
            if (dist < 0.0001f) return false;

            int count = Physics.SphereCastNonAlloc(from, GameTuning.SpitHitRadius, delta / dist,
                                                   Buffer, dist, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var col = Buffer[i].collider;
                if (col == null) continue;

                var target = col.GetComponentInParent<IStaggerable>();
                if (target == null || !target.CanBeStaggered) continue;

                // 噴出去的瞬間會碰到自己的碰撞體，要排掉
                if (target is NetworkBehaviour nb && nb.Object != null && nb.Object.Id == ShooterId) continue;

                target.ApplyBlind(GameTuning.SpitBlindSeconds);
                GameAudio.PlayAt(SfxId.Spray, transform.position);
                return true;
            }
            return false;
        }
    }
}
