#if UNITY_EDITOR || DEVELOPMENT_BUILD
using AlpacasOnFire.Core;
using AlpacasOnFire.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlpacasOnFire.DebugTools
{
    /// <summary>
    /// 開發用的物品產生器 —— 單人測試手感時，沒有隊友可以剃毛，
    /// 用這個直接在腳邊生出羊毛，就能把整條產線跑起來。
    ///
    /// 特性：
    ///  - 只在 Editor 與 Development Build 下編譯，正式版完全不存在
    ///  - 自動建立，**不需要在任何場景裡掛東西**
    ///  - 只在狀態權威端有效（GameMode.Single 或主機端）
    ///
    /// 要新增其他物品，在 Keys 陣列加一行就好。
    /// </summary>
    public class DebugItemSpawner : MonoBehaviour
    {
        /// <summary>設成 false 就能暫時關掉除錯鍵（不用改程式）。</summary>
        public static bool Enabled = true;

        /// <summary>畫面左下角要不要顯示按鍵提示。</summary>
        public static bool ShowHint = true;

        private readonly struct Binding
        {
            public readonly Key Key;
            public readonly ItemKind Kind;
            public readonly string Label;
            public readonly string KeyLabel;   // 顯示用（Key.Digit1 印出來會是 "Digit1"，不好看）

            public Binding(Key key, string keyLabel, ItemKind kind, string label)
            {
                Key = key;
                KeyLabel = keyLabel;
                Kind = kind;
                Label = label;
            }
        }

        // 要加新的除錯物品，在這裡加一行即可。
        // 用數字鍵而不是 F 鍵 —— macOS 的 F 鍵預設綁系統功能（亮度、音量……），要按 Fn 才是 F1。
        private static readonly Binding[] Keys =
        {
            new(Key.Digit1, "1", ItemKind.Wool, "羊毛"),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("[DebugItemSpawner]");
            go.AddComponent<DebugItemSpawner>();
            DontDestroyOnLoad(go);
        }

        private void Update()
        {
            if (!Enabled) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            foreach (var binding in Keys)
            {
                if (!kb[binding.Key].wasPressedThisFrame) continue;
                Spawn(binding);
            }
        }

        private static void Spawn(Binding binding)
        {
            var player = PlayerController.Local;
            if (player == null || player.Object == null)
            {
                Debug.LogWarning("[Debug] 還沒有本機玩家，無法生成物品。");
                return;
            }

            // ItemFactory 只能在狀態權威端呼叫。
            // GameMode.Single 與主機端都成立；純用戶端要生東西得走 RPC，除錯鍵不值得為此複雜化。
            if (!player.HasStateAuthority)
            {
                Debug.LogWarning("[Debug] 除錯鍵只在 GameMode.Single 或主機端有效。");
                return;
            }

            var item = ItemFactory.Spawn(player.Runner, binding.Kind, default, GroundPointInFrontOf(player));
            if (item == null)
            {
                Debug.LogError($"[Debug] 生成 {binding.Label} 失敗 —— " +
                               "請確認 Resources/GameCatalog.asset 裡有登錄這個物品的 prefab。");
                return;
            }

            Debug.Log($"[Debug] 生成了 {binding.Label}。");
        }

        /// <summary>在玩家前方一點點的地面上找一個落點。</summary>
        private static Vector3 GroundPointInFrontOf(PlayerController player)
        {
            var origin = player.transform.position
                       + player.transform.forward * 0.9f
                       + Vector3.up * 1.5f;

            if (Physics.Raycast(origin, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.2f;

            // 找不到地面就丟在腳邊
            return player.transform.position + player.transform.forward * 0.9f + Vector3.up * 0.3f;
        }

        private void OnGUI()
        {
            if (!Enabled || !ShowHint) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.LowerLeft,
                normal = { textColor = new Color(1f, 1f, 1f, 0.65f) },
            };

            var text = "除錯：";
            foreach (var binding in Keys) text += $"[{binding.KeyLabel}] {binding.Label}　";

            GUI.Label(new Rect(12f, Screen.height - 26f, 600f, 20f), text, style);
        }
    }
}
#endif
