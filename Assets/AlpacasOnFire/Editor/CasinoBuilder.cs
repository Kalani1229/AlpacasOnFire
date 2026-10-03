using System.Collections.Generic;
using AlpacasOnFire.Casino;
using AlpacasOnFire.Core;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static AlpacasOnFire.EditorTools.EditorBuildUtils;

namespace AlpacasOnFire.EditorTools
{
    /// <summary>
    /// 遜咖賭場的資產與場景設定。
    ///
    /// 選單一鍵做兩件事：
    ///   1. 建刮刮樂櫃檯的 prefab（NetworkObject + ScratchCardCounter + 碰撞體）並登錄進 GameCatalog
    ///      —— 外觀全部是執行期生的，所以 prefab 很單純
    ///   2. 在**目前開著的場景**裡，把 CasinoState 掛到 [GameSystems] 上
    ///      （它是 Fusion 的場景物件，存檔時 Fusion 會自己重新 bake）
    ///
    /// 「1. 建置佔位資產」也會建櫃檯 prefab（不然整套重建時 Catalog 會被整份覆寫、櫃檯登錄就掉了）。
    /// </summary>
    public static class CasinoBuilder
    {
        private const string CounterPrefabName = "Casino_ScratchCardCounter";

        [MenuItem("羊駝很忙/賭場：建置刮刮樂櫃檯並掛上賭場狀態", priority = 21)]
        public static void BuildAndAttach()
        {
            EnsureFolders();

            // ---- 1. prefab + Catalog ----
            var entry = BuildScratchCounter();

            var catalog = AssetDatabase.LoadAssetAtPath<GameCatalog>($"{ResourcesDir}/GameCatalog.asset");
            if (catalog == null)
            {
                Debug.LogError("[賭場] 找不到 GameCatalog，請先執行「羊駝很忙 / 1. 建置佔位資產」。");
                return;
            }

            var list = new List<GameCatalog.ElementEntry>(catalog.elements ?? new GameCatalog.ElementEntry[0]);
            list.RemoveAll(e => e.type == LevelElementType.ScratchCardCounter);
            list.Add(entry);
            catalog.elements = list.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            // ---- 2. 目前場景的 [GameSystems] ----
            var systems = GameObject.Find("[GameSystems]");
            if (systems == null)
            {
                Debug.LogError("[賭場] 目前的場景裡找不到 [GameSystems]。請先打開 Village_Test 再按一次。" +
                               "（櫃檯 prefab 已經建好了）");
                return;
            }

            if (systems.GetComponent<CasinoState>() == null)
            {
                Undo.AddComponent<CasinoState>(systems);
                EditorSceneManager.MarkSceneDirty(systems.scene);
                Debug.Log("[賭場] 已把 CasinoState 掛到 [GameSystems]。**記得存檔場景（Ctrl+S）**。");
            }
            else
            {
                Debug.Log("[賭場] [GameSystems] 上已經有 CasinoState 了。");
            }

            Debug.Log("[賭場] 刮刮樂櫃檯 prefab 已建立並登錄進 GameCatalog。" +
                      "如果進 Play 時 Fusion 說找不到 prefab，執行 Tools > Fusion > Rebuild Prefab Table。");
            Selection.activeObject = systems;
        }

        /// <summary>建刮刮樂櫃檯的 prefab。PlaceholderAssetBuilder 的整套建置也會呼叫這支。</summary>
        public static GameCatalog.ElementEntry BuildScratchCounter()
        {
            var root = new GameObject(CounterPrefabName);
            root.AddComponent<NetworkObject>();
            root.AddComponent<ScratchCardCounter>();

            // 實體碰撞體：櫃檯本身擋人沒關係（不是擺在路中間），也讓準心掃得到
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.55f, 0f);
            col.size = new Vector3(1.9f, 1.1f, 0.9f);

            var prefab = SavePrefab(root, CounterPrefabName);
            return new GameCatalog.ElementEntry { type = LevelElementType.ScratchCardCounter, prefab = prefab };
        }
    }
}
