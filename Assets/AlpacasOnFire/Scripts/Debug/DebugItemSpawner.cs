#if UNITY_EDITOR || DEVELOPMENT_BUILD
using AlpacasOnFire.Core;
using AlpacasOnFire.Player;
using AlpacasOnFire.Stall;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlpacasOnFire.DebugTools
{
    /// <summary>
    /// 開發用的產生器 —— 單人測試手感時，沒有隊友可以剃毛、也不想為了測一台機器
    /// 就先把整套擺攤流程跑一遍，用這個直接在腳邊生出東西。
    ///
    /// 特性：
    ///  - 只在 Editor 與 Development Build 下編譯，正式版完全不存在
    ///  - 自動建立，**不需要在任何場景裡掛東西**
    ///  - 只在狀態權威端有效（GameMode.Single 或主機端）
    ///  - **不動任何場景與 loadout** —— 在哪個場景都能用，也不會弄壞舊場景的佈局
    ///
    /// 要新增其他除錯物件，在 Keys 陣列加一行就好。
    /// </summary>
    public class DebugItemSpawner : MonoBehaviour
    {
        /// <summary>設成 false 就能暫時關掉除錯鍵（不用改程式）。</summary>
        public static bool Enabled = true;

        /// <summary>畫面左下角要不要顯示按鍵提示。</summary>
        public static bool ShowHint = true;

        /// <summary>生什麼：可攜帶物品、場上的裝備，還是對自己施加一個惡搞效果。</summary>
        private enum SpawnKind : byte { Item, Device, SelfPrank }

        private readonly struct Binding
        {
            public readonly Key Key;
            public readonly string KeyLabel;   // 顯示用（Key.Digit1 印出來會是 "Digit1"，不好看）
            public readonly string Label;
            public readonly SpawnKind Kind;
            public readonly ItemKind Item;
            public readonly LevelElementType Device;
            public readonly DyeColorType Color;

            private Binding(Key key, string keyLabel, string label, SpawnKind kind,
                            ItemKind item, LevelElementType device, DyeColorType color)
            {
                Key = key;
                KeyLabel = keyLabel;
                Label = label;
                Kind = kind;
                Item = item;
                Device = device;
                Color = color;
            }

            public static Binding MakeItem(Key key, string keyLabel, ItemKind item, string label,
                                           DyeColorType color = DyeColorType.White)
                => new(key, keyLabel, label, SpawnKind.Item, item, default, color);

            public static Binding MakeDevice(Key key, string keyLabel, LevelElementType device, string label)
                => new(key, keyLabel, label, SpawnKind.Device, ItemKind.None, device, DyeColorType.White);

            public static Binding MakeSelfPrank(Key key, string keyLabel, string label)
                => new(key, keyLabel, label, SpawnKind.SelfPrank, ItemKind.None, default, DyeColorType.White);
        }

        // 要加新的除錯物件，在這裡加一行即可。
        // 用數字鍵而不是 F 鍵 —— macOS 的 F 鍵預設綁系統功能（亮度、音量……），要按 Fn 才是 F1。
        private static readonly Binding[] Keys =
        {
            Binding.MakeItem(Key.Digit1, "1", ItemKind.Wool, "白毛"),
            Binding.MakeDevice(Key.Digit2, "2", LevelElementType.WeavingMachine, "T恤織布機"),
            Binding.MakeItem(Key.Digit3, "3", ItemKind.Wool, "紅毛", DyeColorType.Red),
            Binding.MakeItem(Key.Digit4, "4", ItemKind.Wool, "藍毛", DyeColorType.Blue),
            Binding.MakeItem(Key.Digit5, "5", ItemKind.Wool, "黃毛", DyeColorType.Yellow),
            Binding.MakeDevice(Key.Digit6, "6", LevelElementType.WeavingMachineShirt, "襯衫織布機"),

            // 惡搞道具。生到腳邊，撿起來對隊友或羊按**左鍵**出手。
            // 卡車不是武器：按住左鍵或 Q 蓄力，放開丟出去，落地爆炸。
            // 口水不在這裡 —— 它是自帶能力，直接按 E。
            Binding.MakeItem(Key.Digit7, "7", ItemKind.Leek, "大蔥"),
            Binding.MakeItem(Key.Digit8, "8", ItemKind.Truck, "卡車"),

            // 對自己施加惡搞效果，用來單人測試「被打到是什麼感覺」。
            // **一個鍵輪流三種**：致盲 -> 擊退 -> 暈倒 -> 再回到致盲。
            // 三個一起上的話畫面會糊成一團，分不出哪個效果長什麼樣。
            Binding.MakeSelfPrank(Key.Digit0, "0", "對自己惡搞"),
        };

        /// <summary>0 鍵輪到第幾個效果。</summary>
        private static int _selfPrankIndex;

        private static readonly string[] SelfPrankNames = { "致盲", "擊退", "暈倒" };

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
                Debug.LogWarning("[Debug] 還沒有本機玩家，無法生成。");
                return;
            }

            // ItemFactory / Runner.Spawn 只能在狀態權威端呼叫。
            // GameMode.Single 與主機端都成立；純用戶端要生東西得走 RPC，除錯鍵不值得為此複雜化。
            if (!player.HasStateAuthority)
            {
                Debug.LogWarning("[Debug] 除錯鍵只在 GameMode.Single 或主機端有效。");
                return;
            }

            if (binding.Kind == SpawnKind.SelfPrank) { SelfPrank(player); return; }

            var point = GroundPointInFrontOf(player);

            if (binding.Kind == SpawnKind.Item) SpawnItem(binding, player, point);
            else SpawnDevice(binding, player, point);
        }

        /// <summary>
        /// 對自己施加一個惡搞效果，一次一種，按一次換下一種。
        ///
        /// 為什麼要有這個：三種效果平常都要別人打你才看得到，單人根本測不了。
        /// 而「被打到是什麼感覺」正是這套系統最需要反覆調的部分 ——
        /// 致盲濃不濃、擊退推多遠、倒地爬多久，都得自己親身體驗才調得準。
        ///
        /// 暈倒刻意帶 dropWool: true，連「身上的毛散一地」一起測到。
        /// </summary>
        private static void SelfPrank(PlayerController player)
        {
            var status = player.GetComponent<Prank.StaggerStatus>();
            if (status == null)
            {
                Debug.LogError("[Debug] 玩家 prefab 上沒有 StaggerStatus。" +
                               "請跑「羊駝很忙 / 1. 建置佔位資產」。");
                return;
            }

            string name = SelfPrankNames[_selfPrankIndex];

            switch (_selfPrankIndex)
            {
                case 0:
                    status.Blind(GameTuning.SpitBlindSeconds);
                    break;

                case 1:
                    // 往自己背後推 —— 面向哪裡就被推向哪裡的反方向，
                    // 跟真的被正面打到一樣
                    status.Knockback(-player.transform.forward,
                                     GameTuning.LeekKnockbackSpeed,
                                     GameTuning.LeekKnockbackSeconds);
                    break;

                default:
                    // 走 PlayerController 的 ApplyStagger 而不是 status.Stagger()，
                    // 因為前者還會脫手、掉毛 —— 那才是完整的「被卡車砸到」
                    player.ApplyStagger(GameTuning.TruckStaggerSeconds, dropWool: true);
                    break;
            }

            _selfPrankIndex = (_selfPrankIndex + 1) % SelfPrankNames.Length;
            Debug.Log($"[Debug] 對自己施加：{name}。再按一次是「{SelfPrankNames[_selfPrankIndex]}」。");
        }

        private static void SpawnItem(Binding binding, PlayerController player, Vector3 point)
        {
            // 顏色走 GarmentSpec 帶進去 —— 羊毛的顏色就是靠這個決定的
            var spec = GarmentSpec.Create(PatternType.None, binding.Color);

            var item = ItemFactory.Spawn(player.Runner, binding.Item, spec, point);
            if (item == null)
            {
                Debug.LogError($"[Debug] 生成 {binding.Label} 失敗 —— " +
                               "請確認 Resources/GameCatalog.asset 裡有登錄這個物品的 prefab。");
                return;
            }

            Debug.Log($"[Debug] 生成了 {binding.Label}。");
        }

        /// <summary>
        /// 生一台機台。**不經過擺攤系統** —— `DeployableDevice.StallOwned` 保持 false，
        /// 所以它不會被格子系統接管位置、也不會被收攤收走，就是一台放在地上的機器。
        /// 這樣在任何場景（包含還沒做好的 Village_Test）都能單獨測一台機台的手感。
        /// </summary>
        private static void SpawnDevice(Binding binding, PlayerController player, Vector3 point)
        {
            var prefab = StallCatalog.DevicePrefab(binding.Device);
            if (prefab == null)
            {
                Debug.LogError($"[Debug] 生成 {binding.Label} 失敗 —— " +
                               "Catalog 裡沒有這台機台，或它的 prefab 上沒有 NetworkObject。" +
                               "請跑「羊駝很忙 / 1. 建置佔位資產」再跑 Tools > Fusion > Rebuild Prefab Table。");
                return;
            }

            // 面向玩家（操作面在機台的 -Z，所以讓 -Z 對著玩家）
            var toPlayer = player.transform.position - point;
            toPlayer.y = 0f;
            var rot = toPlayer.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(-toPlayer.normalized, Vector3.up)
                : Quaternion.identity;

            var obj = player.Runner.Spawn(prefab, point, rot);
            if (obj == null)
            {
                Debug.LogError($"[Debug] Runner.Spawn({binding.Device}) 回傳 null —— " +
                               "多半是 prefab 沒進 Fusion 的 prefab 表，" +
                               "跑一次 Tools > Fusion > Rebuild Prefab Table。");
                return;
            }

            Debug.Log($"[Debug] 生成了 {binding.Label}。");
        }

        /// <summary>在玩家前方一點點的地面上找一個落點。</summary>
        private static Vector3 GroundPointInFrontOf(PlayerController player)
        {
            var origin = player.transform.position
                       + player.transform.forward * 1.4f
                       + Vector3.up * 1.5f;

            if (Physics.Raycast(origin, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;

            // 找不到地面就丟在腳邊
            return player.transform.position + player.transform.forward * 1.4f + Vector3.up * 0.3f;
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
            foreach (var binding in Keys)
            {
                // 對自己惡搞是輪流的，要把「下一個是什麼」寫出來，
                // 不然按下去才知道這次是哪一種
                string label = binding.Kind == SpawnKind.SelfPrank
                    ? $"{binding.Label}（下一個：{SelfPrankNames[_selfPrankIndex]}）"
                    : binding.Label;

                text += $"[{binding.KeyLabel}] {label}　";
            }

            GUI.Label(new Rect(12f, Screen.height - 26f, 700f, 20f), text, style);
        }
    }
}
#endif
