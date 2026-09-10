using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Orders;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 交貨窗口（Overcooked 模式）：玩家手持成品衣服面向它按 Space 就交貨。
    ///
    /// 本批直接交「成品衣服」，不經過箱子 —— 剛好 OrderBoard.TryDeliver() 吃的就是
    /// GarmentSpec，所以不需要新增多載，既有的訂單比對與計分完全沿用。
    /// BoxItem 與郵箱的程式碼都保留著，本批只是沒有用到。
    ///
    /// 造型上有明確的窗口朝向：+Z 是面向顧客的那一側，CustomerQueueAnchor 標出
    /// 下一批顧客的排隊位置（本批只是一個空物件與 Gizmo，沒有任何邏輯）。
    /// </summary>
    public class DeliveryCounter : NetworkInteractable, IThrownItemReceiver
    {
        [Header("Delivery Counter")]
        [SerializeField] private Transform _customerQueueAnchor;
        [SerializeField] private Renderer _statusLight;

        private MaterialPropertyBlock _mpb;

        /// <summary>顧客排隊的位置。下一批做 NPC 時直接讀這個點。</summary>
        public Transform CustomerQueueAnchor =>
            _customerQueueAnchor != null ? _customerQueueAnchor : transform;

        /// <summary>窗口朝外的方向（顧客站的那一側）。</summary>
        public Vector3 WindowForward => transform.forward;

        public override int InteractionPriority => 1;

        private static bool CanSellNow()
        {
            var stall = StallManager.Instance;
            // 沒有擺攤系統（例如舊的工坊場景）就照常運作，不要把既有場景弄壞
            return stall == null || stall.IsBusinessMode;
        }

        /// <summary>
        /// **兩個場景並存的關鍵接縫。**
        ///
        /// v6 的 Village 場景有 CustomerQueue，交貨要比對站在窗口外的顧客要什麼；
        /// Stall_Test 沒有它，就 fallback 回既有的 OrderBoard。
        /// 這一段 fallback 不能省 —— 省了舊場景就賣不出東西。
        ///
        /// 回傳 true = 收下了（呼叫端負責消耗衣服）。
        /// </summary>
        private static bool RouteDelivery(GarmentSpec spec)
        {
            var queue = Npc.CustomerQueue.Instance;
            if (queue != null && queue.Object != null && queue.Object.IsValid)
                return queue.TryDeliver(spec);

            return OrderBoard.Instance != null && OrderBoard.Instance.TryDeliver(spec);
        }

        /// <summary>有沒有人要這件衣服（唯讀，給提示字與丟擲判定用）。</summary>
        private static bool AnyoneWants(GarmentSpec spec)
        {
            var queue = Npc.CustomerQueue.Instance;
            if (queue != null && queue.Object != null && queue.Object.IsValid)
                return queue.HasMatch(spec);
            return OrderBoard.Instance != null;
        }

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (ctx.Held is not GarmentItem) return false;
            return CanSellNow();
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.Held is not GarmentItem garment)
                return "交貨窗口（需要做好的衣服）";

            if (!CanSellNow()) return "還沒開張，不能交貨";
            return $"[Space] 交貨 {garment.Spec.Describe()}";
        }

        // ---------------- 被丟過來的衣服 ----------------

        /// <summary>
        /// 直接把做好的衣服扔進窗口出貨。
        /// 注意：**比對不到訂單就不收**（回傳 false），衣服會照常落地 ——
        /// 跟手動交貨失敗時衣服留在手上是同一個原則，不會憑空消失。
        /// </summary>
        public bool CanAcceptThrown(CarriableItem item)
            => item is GarmentItem garment && CanSellNow() && AnyoneWants(garment.Spec);

        public bool AcceptThrown(CarriableItem item)
        {
            if (!HasStateAuthority || !CanAcceptThrown(item)) return false;
            if (item is not GarmentItem garment) return false;

            if (!RouteDelivery(garment.Spec)) return false;

            GameAudio.PlayAt(SfxId.ShipSuccess, transform.position);
            return true;
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;
            if (ctx.Held is not GarmentItem garment) return;
            if (!CanSellNow()) return;

            bool ok = RouteDelivery(garment.Spec);

            // 失敗時衣服留在玩家手上（跟郵箱的既有行為一致：扣款 + 訂單區閃紅）
            if (!ok) return;

            ctx.Player.Carry.ConsumeHeld();
        }

        public override void Render()
        {
            if (_statusLight == null) return;

            var stall = StallManager.Instance;
            Color c = stall == null ? new Color(0.3f, 0.85f, 0.4f)
                    : stall.IsBusinessMode ? new Color(0.35f, 0.95f, 0.45f)
                    : new Color(0.45f, 0.45f, 0.48f);

            _mpb ??= new MaterialPropertyBlock();
            _statusLight.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _statusLight.SetPropertyBlock(_mpb);
        }

        private void OnDrawGizmos()
        {
            var anchor = _customerQueueAnchor != null ? _customerQueueAnchor : transform;
            Gizmos.color = new Color(0.95f, 0.6f, 0.2f, 0.85f);
            Gizmos.DrawWireSphere(anchor.position + Vector3.up * 0.9f, 0.45f);
            Gizmos.DrawRay(anchor.position + Vector3.up * 0.9f, -transform.forward * 1.2f);
        }
    }
}
