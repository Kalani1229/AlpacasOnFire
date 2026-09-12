using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Player
{
    /// <summary>玩家手上的東西：拾取、丟出、接住、消耗。</summary>
    public class PlayerCarry : NetworkBehaviour
    {
        [Networked] public NetworkId HeldId { get; set; }

        private PlayerController _player;

        public bool HasItem => HeldId.IsValid;

        public CarriableItem Held
        {
            get
            {
                if (!HeldId.IsValid || Runner == null) return null;
                return Runner.TryFindObject(HeldId, out var obj) ? obj.GetComponent<CarriableItem>() : null;
            }
        }

        public override void Spawned()
        {
            _player = GetComponent<PlayerController>();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            TickAutoCatch();

            // 自我修復：手上的東西被別人 Despawn 掉時，HeldId 會變成一個指不到東西的 id。
            // 那會造成「UI 顯示手上有東西、畫面上卻什麼都沒有，而且再也放不掉」。
            // 這裡每個 tick 檢查一次，發現就清掉。
            if (!HeldId.IsValid) return;
            if (Runner != null && Runner.TryFindObject(HeldId, out var obj) && obj != null) return;

            Debug.LogWarning("[攜帶] 手上的物件已經不存在了，清掉持有狀態。");
            HeldId = default;
        }

        // ---------------- 拾取 / 放下 ----------------

        public bool TryPickup(CarriableItem item)
        {
            if (!HasStateAuthority || item == null) return false;
            // 已經被 Despawn 的物件不能撿 —— 撿了會留下一個指不到東西的 HeldId
            if (item.Object == null || !item.Object.IsValid) return false;
            if (HasItem || item.IsHeld) return false;

            item.AttachTo(_player);
            HeldId = item.Object.Id;
            GameAudio.PlayAt(SfxId.Pickup, transform.position);
            return true;
        }

        public void Drop()
        {
            if (!HasStateAuthority) return;
            var item = Held;
            HeldId = default;
            if (item == null) return;

            item.DetachToGround(_player.HandAnchor.position + _player.transform.forward * 0.4f);
            GameAudio.PlayAt(SfxId.Drop, transform.position);
        }

        /// <summary>
        /// 騰出手來拿新東西：手上有東西就先放掉，讓接下來的拿取一定成功。
        ///
        /// 為什麼不是「手上有東西就不給拿」：那會逼玩家為了拿一份毛先找地方放東西，
        /// 在限時的攤位上是純粹的摩擦。主動接住（TryManualCatch）早就是這個手感了 ——
        /// 接到了就把原本手上的東西放到腳邊，這裡跟它一致。
        ///
        /// 一律放到腳邊、不銷毀 —— 玩家看得到、撿得回來。
        ///
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public void MakeRoomForPickup()
        {
            if (!HasStateAuthority || !HasItem) return;
            Drop();
        }

        /// <summary>手上的東西被用掉了（縫紉機吃掉羊毛、衣服穿到人偶上……）。</summary>
        public void ConsumeHeld()
        {
            if (!HasStateAuthority) return;
            var item = Held;
            HeldId = default;
            if (item != null && item.Object != null)
                Runner.Despawn(item.Object);
        }

        /// <summary>把手上的東西交出去但不銷毀（例如放進機台的暫存槽）。</summary>
        public CarriableItem ReleaseHeld()
        {
            if (!HasStateAuthority) return null;
            var item = Held;
            HeldId = default;
            return item;
        }

        // 隨身剃毛器（E）已經移除。剃毛不再是「拿著工具去用」，
        // 而是對著羊或隊友按左鍵就直接剃 —— 剃毛器只在動作的那一瞬間伸出來。
        // 見 PlayerController.TriggerShearVisual() 與 WoolNpc.Interact()。

        // ---------------- 丟出（左鍵第 3 順位 / Q）----------------

        /// <summary>
        /// 純粹的丟出，不做任何前置判斷 —— 要不要丟由呼叫端決定。
        /// 兩個入口：PlayerController.HandlePrimaryPress 的第 3 順位（面前空無一物），
        /// 以及 Q（近距離硬要丟的逃生口）。
        /// </summary>
        public void HandleThrow(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !HasItem) return;

            var item = Held;
            HeldId = default;
            item.LaunchFrom(_player, ctx.Direction);
            GameAudio.PlayAt(SfxId.Throw, transform.position);
        }

        // ---------------- 接住 ----------------

        /// <summary>找一個接得到的滯空物品，最近的優先。</summary>
        public CarriableItem FindCatchable(float radius, bool requireFacing)
        {
            CarriableItem best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < CarriableItem.All.Count; i++)
            {
                var item = CarriableItem.All[i];
                if (item == null) continue;
                if (!item.CanBeCaughtBy(_player, radius, requireFacing, out float d)) continue;
                if (d < bestDist) { bestDist = d; best = item; }
            }
            return best;
        }

        /// <summary>
        /// 自動接住：空手、大致面向、在小範圍內就直接拿到，什麼都不用按。
        /// 每個 tick 在狀態權威端跑。
        /// </summary>
        private void TickAutoCatch()
        {
            if (HasItem) return;

            var item = FindCatchable(GameTuning.CatchAutoRadius, requireFacing: true);
            if (item == null) return;

            Catch(item);
        }

        /// <summary>
        /// 主動接住（互動鍵）：範圍比自動大一點、也不要求面向。
        ///
        /// **接到了才會把原本手上的東西放到腳邊**；沒接到就什麼都不做，
        /// 手上的東西不會白白掉出去。回傳有沒有接到，讓呼叫端決定要不要改跑情境互動。
        /// </summary>
        public bool TryManualCatch()
        {
            if (!HasStateAuthority) return false;

            var item = FindCatchable(GameTuning.CatchManualRadius, requireFacing: false);
            if (item == null) return false;

            if (HasItem) Drop();   // 接到了才拋棄手上的東西
            Catch(item);
            return true;
        }

        private void Catch(CarriableItem item)
        {
            item.AttachTo(_player);
            HeldId = item.Object.Id;
            GameAudio.PlayAt(SfxId.Catch, transform.position);
        }

        // ---------------- E：持續使用工具 ----------------

        public void TickTool(in InteractionContext ctx, bool held, float deltaTime)
        {
            if (!HasStateAuthority) return;
            if (Held is IHoldTool tool)
                tool.ToolTick(in ctx, held, deltaTime);
        }

        /// <summary>
        /// 按下右鍵：手上是惡搞道具就出手。
        ///
        /// 回傳 true 代表「這次右鍵被道具吃掉了」——**打不到人也算吃掉**。
        /// 不然拿著大蔥對空氣按右鍵會掉回次要互動，把旁邊手提箱的選色面板打開。
        ///
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public bool TryPrank(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return false;
            return Held is Prank.PrankTool prank && prank.TryUse(in ctx);
        }

        /// <summary>
        /// 惡搞道具的蓄力（卡車）。吃**右鍵按住**，跟出手同一個鍵。
        ///
        /// 另外開一條而不是塞進 IHoldTool，是為了不動噴槍那條已經在跑的路徑 ——
        /// 那個介面的語意是「持續使用」，卡車是「蓄力後一次性爆發」，不一樣。
        /// </summary>
        public void TickPrank(in InteractionContext ctx, bool useToolHeld, float deltaTime)
        {
            if (!HasStateAuthority) return;
            if (Held is Prank.PrankTool prank)
                prank.PrankTick(in ctx, useToolHeld, deltaTime);
        }
    }
}
