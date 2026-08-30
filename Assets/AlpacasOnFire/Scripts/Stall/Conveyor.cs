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

        private float _scrollPhase;

        public Vector3 Direction => transform.forward;
        public Transform OutputAnchor => _outputAnchor != null ? _outputAnchor : transform;

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            float dt = Runner.DeltaTime;
            float halfLength = GameTuning.ConveyorLength * 0.5f;
            float halfWidth = GameTuning.ConveyorWidth * 0.5f;

            for (int i = 0; i < CarriableItem.All.Count; i++)
            {
                var item = CarriableItem.All[i];
                if (item == null || item.Object == null) continue;
                if (item.IsHeld || item.InFlight) continue;          // 拿在手上／飛行中的不管
                if (item.Kind == ItemKind.Suitcase) continue;        // 手提箱不上輸送帶

                // 換算成輸送帶的本地座標，判斷有沒有在帶面上
                var local = transform.InverseTransformPoint(item.transform.position);
                if (Mathf.Abs(local.x) > halfWidth) continue;
                if (Mathf.Abs(local.z) > halfLength) continue;

                float aboveBelt = local.y - GameTuning.ConveyorHeight;
                if (aboveBelt < -0.15f || aboveBelt > GameTuning.ConveyorCaptureHeight) continue;

                float nextZ = local.z + GameTuning.ConveyorSpeed * dt;

                if (nextZ >= halfLength)
                {
                    // 到末端：送出去並讓它落到出口錨點
                    var exit = OutputAnchor.position;
                    item.DetachToGround(exit);
                    continue;
                }

                var nextLocal = new Vector3(
                    Mathf.MoveTowards(local.x, 0f, dt * 1.5f),   // 順便把東西帶到帶面中線
                    local.y,
                    nextZ);
                item.transform.position = transform.TransformPoint(nextLocal);
            }
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
