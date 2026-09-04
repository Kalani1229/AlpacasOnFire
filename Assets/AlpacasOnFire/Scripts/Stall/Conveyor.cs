using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 輸送帶：放在帶面上的可攜帶物件會被往 +Z 推，到末端就掉下去。
    ///
    /// v1 刻意只做「把東西從一端送到另一端」，不接進機台的輸入口 ——
    /// 規格書明講這不是自動化系統，只是一條方便的輸送線。
    ///
    /// 位置由狀態權威在 FixedUpdateNetwork 推動，其餘用戶端靠 CarriableItem 上既有的
    /// NetworkTransform 同步，不需要任何額外的同步程式碼。
    /// 用邊界框判斷而不是物理觸發：物品被拿起時碰撞體會被關掉，用 trigger 會漏掉狀態變化。
    /// </summary>
    public class Conveyor : NetworkBehaviour
    {
        [Header("Conveyor")]
        [SerializeField] private Transform _beltSurface;
        [SerializeField] private Transform _outputAnchor;
        [SerializeField] private Renderer[] _directionMarkers;

        /// <summary>場上所有輸送帶。機台要找「旁邊有沒有往外送的輸送帶」時用，
        /// 不依賴碰撞體，測試場景與攤位上都成立。</summary>
        public static readonly List<Conveyor> All = new();

        private float _scrollPhase;

        public Vector3 Direction => transform.forward;
        public Transform OutputAnchor => _outputAnchor != null ? _outputAnchor : transform;

        /// <summary>靠近入口那一端的帶面位置 —— 機台自動出貨就放在這裡。</summary>
        public Vector3 EntryPoint => transform.TransformPoint(new Vector3(
            0f,
            GameTuning.ConveyorHeight + 0.18f,
            -GameTuning.ConveyorLength * 0.5f + 0.2f));

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            float dt = Runner.DeltaTime;
            float halfLength = GameTuning.ConveyorLength * 0.5f;
            float halfWidth = GameTuning.ConveyorWidth * 0.5f;

            for (int i = CarriableItem.All.Count - 1; i >= 0; i--)
            {
                var item = CarriableItem.All[i];
                if (item == null || item.Object == null) continue;
                if (item.IsHeld || item.InFlight) continue;          // 拿在手上／飛行中的不管
                if (item.Kind == ItemKind.Suitcase) continue;        // 手提箱不上輸送帶

                var local = transform.InverseTransformPoint(item.transform.position);

                // 高度差太多就不管（在輸送帶下面很多、或還飛在半空中）
                float aboveBelt = local.y - GameTuning.ConveyorHeight;
                if (aboveBelt < GameTuning.ConveyorPickupDrop
                 || aboveBelt > GameTuning.ConveyorCaptureHeight) continue;

                bool onBelt = IsOnBelt(local);

                // ---- 帶面外圍：把附近的東西吸上來 ----
                // 不用剛好丟中帶面，丟到旁邊也會自己爬上去
                if (!onBelt)
                {
                    // **已經在別條輸送帶上的東西不要搶。**
                    // 相鄰兩條的帶面只差 0.15 公尺，遠在吸附半徑內 ——
                    // 不擋的話後面那條會一直把東西往回拉，跟前面那條互相拉扯，
                    // 物品就卡在交界處不動了。
                    if (IsOnAnyBelt(item)) continue;

                    float outX = Mathf.Max(0f, Mathf.Abs(local.x) - halfWidth);
                    float outZ = Mathf.Max(0f, Mathf.Abs(local.z) - halfLength);
                    if (outX * outX + outZ * outZ >
                        GameTuning.ConveyorAttractRadius * GameTuning.ConveyorAttractRadius) continue;

                    var target = new Vector3(
                        Mathf.Clamp(local.x, -halfWidth * 0.6f, halfWidth * 0.6f),
                        GameTuning.ConveyorHeight + 0.12f,
                        Mathf.Clamp(local.z, -halfLength * 0.9f, halfLength * 0.9f));

                    var pulled = Vector3.MoveTowards(local, target, GameTuning.ConveyorAttractSpeed * dt);
                    item.transform.position = transform.TransformPoint(pulled);
                    continue;   // 這一 tick 先吸進來，下一 tick 才開始被推
                }

                // ---- 帶面上：往前推 ----
                float nextZ = local.z + GameTuning.ConveyorSpeed * dt;

                if (nextZ >= halfLength)
                {
                    // 末端 ①：前面還有一條同向的輸送帶 -> 交棒，不要落地
                    var next = FindNextConveyor();
                    if (next != null)
                    {
                        item.transform.position = next.EntryPoint;
                        continue;
                    }

                    // 末端 ②：前面的機台收得下這個東西 -> 直接送進去
                    if (TryDeliverToMachine(item)) continue;

                    // 末端 ③：什麼都沒有，落地
                    item.DetachToGround(OutputAnchor.position);
                    continue;
                }

                var nextLocal = new Vector3(
                    Mathf.MoveTowards(local.x, 0f, dt * 1.5f),   // 順便把東西帶到帶面中線
                    local.y,
                    nextZ);
                item.transform.position = transform.TransformPoint(nextLocal);
            }
        }

        /// <summary>這個本地座標算不算「在帶面上」。</summary>
        private static bool IsOnBelt(Vector3 local)
        {
            float aboveBelt = local.y - GameTuning.ConveyorHeight;
            if (aboveBelt < GameTuning.ConveyorOnBeltDrop
             || aboveBelt > GameTuning.ConveyorCaptureHeight) return false;

            return Mathf.Abs(local.x) <= GameTuning.ConveyorWidth * 0.5f
                && Mathf.Abs(local.z) <= GameTuning.ConveyorLength * 0.5f;
        }

        /// <summary>這個物品是不是已經在某一條輸送帶的帶面上（包含自己）。</summary>
        private static bool IsOnAnyBelt(CarriableItem item)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                if (c == null || c.Object == null) continue;
                if (IsOnBelt(c.transform.InverseTransformPoint(item.transform.position))) return true;
            }
            return false;
        }

        /// <summary>
        /// 末端前方有機台而且收得下的話，直接把東西送進去。
        /// 用的是跟「丟進機台」同一個 IThrownItemReceiver 介面 ——
        /// 所以縫紉機收羊毛、果汁機收染料、交貨窗口收成品衣服，三種都自動成立，
        /// 之後新增的機台只要實作那個介面就會一起支援。
        /// </summary>
        private bool TryDeliverToMachine(CarriableItem item)
        {
            var probe = transform.TransformPoint(new Vector3(
                0f, GameTuning.ConveyorHeight, GameTuning.ConveyorLength * 0.5f + 0.35f));

            var hits = Physics.OverlapSphere(probe, GameTuning.ConveyorDeliverRadius,
                                             ~0, QueryTriggerInteraction.Collide);
            foreach (var col in hits)
            {
                var receiver = col.GetComponentInParent<IThrownItemReceiver>();
                if (receiver == null) continue;
                if (!receiver.CanAcceptThrown(item) || !receiver.AcceptThrown(item)) continue;

                Runner.Despawn(item.Object);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 找「接在我前面、而且同方向」的下一條輸送帶。
        /// 兩條同向的輸送帶擺在一起就會自動串成一條長線。
        /// 只認正交相鄰的格子（斜角約 2.12 公尺，超過門檻所以不會誤判）。
        /// </summary>
        public Conveyor FindNextConveyor()
        {
            var forward = Direction.normalized;
            float bestSqr = GameTuning.ConveyorLinkRadius * GameTuning.ConveyorLinkRadius;
            Conveyor best = null;

            for (int i = 0; i < All.Count; i++)
            {
                var other = All[i];
                if (other == null || other == this || other.Object == null) continue;

                // 必須同方向，不然東西會被推回來或卡在交界
                if (Vector3.Dot(forward, other.Direction.normalized) < 0.9f) continue;

                var offset = other.transform.position - transform.position;
                offset.y = 0f;

                // 必須在我前方
                if (Vector3.Dot(forward, offset) <= 0.01f) continue;

                float sqr = offset.sqrMagnitude;
                if (sqr > bestSqr) continue;

                bestSqr = sqr;
                best = other;
            }
            return best;
        }

        public override void Render()
        {
            if (_directionMarkers == null || _directionMarkers.Length == 0) return;

            // 帶面上的方向標記循環閃動，一眼看得出往哪邊送
            _scrollPhase += Time.deltaTime * GameTuning.ConveyorSpeed;
            int lit = Mathf.FloorToInt(_scrollPhase) % _directionMarkers.Length;
            if (lit < 0) lit += _directionMarkers.Length;

            for (int i = 0; i < _directionMarkers.Length; i++)
            {
                if (_directionMarkers[i] == null) continue;
                _directionMarkers[i].enabled = i == lit;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.85f, 0.95f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, GameTuning.ConveyorHeight, 0f),
                new Vector3(GameTuning.ConveyorWidth, GameTuning.ConveyorCaptureHeight, GameTuning.ConveyorLength));
            Gizmos.DrawRay(new Vector3(0f, GameTuning.ConveyorHeight, 0f), Vector3.forward * 1.5f);
        }
    }
}
