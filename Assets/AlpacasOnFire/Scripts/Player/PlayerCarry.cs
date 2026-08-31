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

        // ---------------- Q：丟出 / 接住 ----------------

        /// <summary>
        /// Q 鍵的判定優先權：只要有東西正飛向自己，一律先接住；否則才是丟出手上的東西。
        /// </summary>
        public void HandleThrowCatch(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            var incoming = FindIncoming();
            if (incoming != null)
            {
                if (HasItem) Drop();          // 手上有東西就先放下，才接得住
                incoming.AttachTo(_player);
                HeldId = incoming.Object.Id;
                GameAudio.PlayAt(SfxId.Catch, transform.position);
                return;
            }

            if (!HasItem) return;

            var item = Held;
            HeldId = default;
            item.LaunchFrom(_player, ctx.Direction);
            GameAudio.PlayAt(SfxId.Throw, transform.position);
        }

        /// <summary>有沒有東西正飛向我（也給 HUD 顯示「接住」提示用）。</summary>
        public CarriableItem FindIncoming()
        {
            CarriableItem best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < CarriableItem.All.Count; i++)
            {
                var item = CarriableItem.All[i];
                if (item == null || !item.IsFlyingToward(_player, out float d)) continue;
                if (d < bestDist) { bestDist = d; best = item; }
            }
            return best;
        }

        // ---------------- E：持續使用工具 ----------------

        public void TickTool(in InteractionContext ctx, bool held, float deltaTime)
        {
            if (!HasStateAuthority) return;
            if (Held is IHoldTool tool)
                tool.ToolTick(in ctx, held, deltaTime);
        }
    }
}
