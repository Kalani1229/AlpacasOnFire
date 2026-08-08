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

        /// <summary>生成後直接放到玩家手上。</summary>
        public static CarriableItem SpawnIntoHands(NetworkRunner runner, ItemKind kind, GarmentSpec spec,
                                                   Player.PlayerController player)
        {
            if (player == null) return null;
            var item = Spawn(runner, kind, spec, player.HandAnchor.position);
            if (item != null) player.Carry.TryPickup(item);
            return item;
        }
    }
}
