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

        // ---------------- 隨身工具（E）----------------

        /// <summary>
        /// v6：剃毛器改成隨身預設工具。按 E 生一把到手上，再按一次收起來。
        ///
        /// 收起來是 Despawn 不是丟在地上 —— 它是「隨身」工具，不該在世界上留下一堆。
        /// 手上有別的東西時不做事，但要給提示，不能靜默失敗（玩家會以為按鍵壞了）。
        ///
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public void ToggleDefaultTool()
        {
            if (!HasStateAuthority) return;

            var held = Held;

            // 手上已經是剃毛器 -> 收起來
            if (held != null && held.Kind == ItemKind.Shears)
            {
                ConsumeHeld();
                GameAudio.PlayAt(SfxId.Drop, transform.position);
                return;
            }

            if (HasItem)
            {
                RPC_DefaultToolBlocked();
                return;
            }

            var tool = ItemFactory.SpawnIntoHands(Runner, ItemKind.Shears, default, _player);
            if (tool == null)
                Debug.LogError("[v6] 生成剃毛器失敗：GameCatalog 裡沒有 ItemKind.Shears 的 prefab。");
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
        private void RPC_DefaultToolBlocked()
        {
            Stall.StallManager.LocalNotice("先空出手才能拿剃毛器");
        }

        // ---------------- 丟出（Q）----------------

        /// <summary>Q 只負責丟出。接住已經移到互動鍵（Space／左鍵）與自動接住。</summary>
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
    }
}
