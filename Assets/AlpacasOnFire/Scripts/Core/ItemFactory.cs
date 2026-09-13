using AlpacasOnFire.Items;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Core
{
    /// <summary>生成可攜帶物件的統一入口。只在 StateAuthority 呼叫。</summary>
    public static class ItemFactory
    {
        public static CarriableItem Spawn(NetworkRunner runner, ItemKind kind, GarmentSpec spec,
                                          Vector3 position, Quaternion rotation = default)
        {
            if (runner == null) return null;
            var prefab = GameCatalog.Instance != null ? GameCatalog.Instance.GetItem(kind) : null;
            if (prefab == null) return null;

            if (rotation == default) rotation = Quaternion.identity;

            var obj = runner.Spawn(prefab, position, rotation, null, (r, o) =>
            {
                var item = o.GetComponent<CarriableItem>();
                if (item != null) item.Spec = spec;
            });
            return obj != null ? obj.GetComponent<CarriableItem>() : null;
        }

        /// <summary>
        /// 生成後直接放到玩家手上。
        ///
        /// **持有狀態在生成回呼裡就寫好，不走 TryPickup。** 這是連線時
        /// 「羊毛卡在手上卻不能用」那個 bug 的根源：
        ///
        /// Fusion 的 Spawn 是延遲的 —— 單人模式多半當場完成，連線時卻常常
        /// 要等到下一個 tick 才真的 Spawned()。而 TryPickup 第一件事就是檢查
        /// `!item.Object.IsValid` 然後**直接回 false**。於是羊毛生出來了、
        /// 素材箱的存量也扣了，但玩家手上是空的；那顆毛就停在剛才手的位置不動，
        /// 看起來像卡在手上，實際上誰也碰不到它。
        ///
        /// 改成在 onBeforeSpawned 裡寫 HolderId（Fusion 要求初始化網路狀態就在這裡做），
        /// 再把 HeldId 指過去，兩邊一次寫好，就完全繞開那個空窗期。
        /// </summary>
        public static CarriableItem SpawnIntoHands(NetworkRunner runner, ItemKind kind, GarmentSpec spec,
                                                   Player.PlayerController player)
        {
            if (runner == null || player == null || player.Object == null) return null;
            if (player.Carry == null || player.Carry.HasItem) return null;

            var prefab = GameCatalog.Instance != null ? GameCatalog.Instance.GetItem(kind) : null;
            if (prefab == null) return null;

            var holderId = player.Object.Id;

            var obj = runner.Spawn(prefab, player.HandAnchor.position, Quaternion.identity, null, (r, o) =>
            {
                var carriable = o.GetComponent<CarriableItem>();
                if (carriable == null) return;
                carriable.Spec = spec;
                carriable.HolderId = holderId;   // 物品 -> 玩家，在這裡就綁好
            });

            if (obj == null) return null;

            var item = obj.GetComponent<CarriableItem>();
            if (item == null) return null;

            player.Carry.AdoptSpawned(item);     // 玩家 -> 物品
            return item;
        }
    }
}
