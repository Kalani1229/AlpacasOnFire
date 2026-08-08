using System;
using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Core
{
    /// <summary>
    /// 所有 prefab 的集中索引。放在 Resources 底下，程式碼一律透過 GameCatalog.Instance 取用，
    /// 不需要在每個場景手動拉引用。由 Editor 的「建置佔位資產」工具自動產生與填寫。
    /// </summary>
    [CreateAssetMenu(fileName = "GameCatalog", menuName = "羊駝很忙/Game Catalog")]
    public class GameCatalog : ScriptableObject
    {
        public const string ResourcePath = "GameCatalog";

        [Serializable]
        public struct ItemEntry
        {
            public ItemKind kind;
            public NetworkObject prefab;
        }

        [Serializable]
        public struct ElementEntry
        {
            public LevelElementType type;
            public GameObject prefab;
        }

        [Header("Player")]
        public NetworkObject playerPrefab;

        [Header("Items（可攜帶物件）")]
        public ItemEntry[] items = Array.Empty<ItemEntry>();

        [Header("Level Elements（關卡編輯器可放置的元件）")]
        public ElementEntry[] elements = Array.Empty<ElementEntry>();

        private static GameCatalog _instance;

        public static GameCatalog Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<GameCatalog>(ResourcePath);
                if (_instance == null)
                    Debug.LogError("[GameCatalog] 找不到 Resources/GameCatalog.asset。請執行選單「羊駝很忙 / 建置佔位資產」。");
                return _instance;
            }
        }

        public NetworkObject GetItem(ItemKind kind)
        {
            foreach (var e in items)
                if (e.kind == kind) return e.prefab;
            Debug.LogError($"[GameCatalog] 沒有登錄 {kind} 的 prefab。");
            return null;
        }

        public GameObject GetElement(LevelElementType type)
        {
            foreach (var e in elements)
                if (e.type == type) return e.prefab;
            return null;
        }
    }
}
