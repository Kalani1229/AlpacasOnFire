using System;
using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Machines;
using AlpacasOnFire.Orders;
using AlpacasOnFire.Player;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Npc
{
    /// <summary>
    /// 顧客排隊管理。掛在場景的 [GameSystems] 上，單例，只在營業中運作。
    ///
    /// **刻意不改寫 OrderBoard**：Stall_Test 還在用它，動了就會弄壞舊場景。
    /// 交貨判定由 DeliveryCounter 先問這裡，沒有才 fallback 回 OrderBoard ——
    /// 那一行 fallback 就是兩個場景並存的關鍵。
    ///
    /// 需求怎麼抽（「永遠不會有無解訂單」的保證）：
    /// 掃描場上**現在還有存量**的素材箱，從那些顏色裡挑主色、再挑點綴色。
    /// 不從固定池子抽 —— 那樣一定會出現做不出來的訂單。
    /// </summary>
    public class CustomerQueue : NetworkBehaviour
    {
        public static CustomerQueue Instance { get; private set; }

        /// <summary>UI 用：成交（價格, 描述）。</summary>
        public static event Action<int, string> OnCustomerServed;

        /// <summary>UI 用：交了沒人要的衣服（描述）。**不是**顧客等太久 —— 那個是 OnCustomerTimeout。</summary>
        public static event Action<string> OnCustomerLeft;

        /// <summary>UI 用：顧客等太久自己走了（描述）。由 Customer 在耐心耗盡時觸發。</summary>
        public static event Action<string> OnCustomerTimeout;

        /// <summary>
        /// 給 Customer 用的觸發口。
        /// C# 不允許在宣告事件的型別外面 Invoke，所以耐心耗盡的通知要繞這一手。
        /// </summary>
        public static void RaiseTimeout(string desc) => OnCustomerTimeout?.Invoke(desc);

        [Networked] private TickTimer SpawnTimer { get; set; }

        private readonly List<DyeColorType> _availableColors = new();

        public override void Spawned()
        {
            Instance = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        // ---------------- 模擬 ----------------

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var stall = StallManager.Instance;
            if (stall == null || !stall.IsBusinessMode)
            {
                // 不在營業中：把還站著的顧客請走，免得結算之後還杵在那裡
                if (ActiveCount() > 0) DismissAll();
                return;
            }

            if (!SpawnTimer.ExpiredOrNotRunning(Runner)) return;

            TrySpawnCustomer();
            SpawnTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.CustomerIntervalSeconds);
        }

        public int ActiveCount()
        {
            int n = 0;
            for (int i = 0; i < WoolNpc.All.Count; i++)
            {
                var c = CustomerOf(WoolNpc.All[i]);
                if (c != null && c.Active) n++;
            }
            return n;
        }

        private static Customer CustomerOf(WoolNpc npc)
            => npc != null ? npc.GetComponent<Customer>() : null;

        /// <summary>
        /// 把目前排隊中的顧客填進 buffer。HUD 的訂單卡要用。
        /// 唯讀，任何端都可以呼叫。
        ///
        /// **順序刻意跟著 WoolNpc.All 走**（也就是生成順序），不照耐心排序 ——
        /// 照耐心排的話卡片會隨著倒數互相超車、上下亂跳，很難讀。
        /// 「先做哪一張」交給卡片上的倒數條表達就夠了。
        /// </summary>
        public void CollectActive(List<Customer> buffer)
        {
            buffer.Clear();

            for (int i = 0; i < WoolNpc.All.Count; i++)
            {
                var c = CustomerOf(WoolNpc.All[i]);
                if (c == null || c.Object == null || !c.Object.IsValid) continue;
                if (!c.Active) continue;
                buffer.Add(c);
            }
        }

        private void DismissAll()
        {
            for (int i = 0; i < WoolNpc.All.Count; i++)
            {
                var c = CustomerOf(WoolNpc.All[i]);
                if (c != null && c.Active) c.Leave(served: true);   // 不扣款，只是收攤了
            }
        }

        // ---------------- 徵召 ----------------

        private void TrySpawnCustomer()
        {
            if (ActiveCount() >= GameTuning.CustomerMaxConcurrent) return;

            var counter = FindCounter();
            if (counter == null) return;

            if (!TryBuildRequest(out var spec, out int price)) return;

            var npc = FindIdleNpc();
            if (npc == null) return;   // 場上沒有閒著的羊，這一輪先跳過

            var customer = CustomerOf(npc);
            if (customer == null)
            {
                Debug.LogError("[v6] WoolNpc prefab 上沒有 Customer 元件，顧客系統無法運作。" +
                               "請重跑「羊駝很忙 / 1. 建置佔位資產」。");
                return;
            }

            var spot = QueueSpotFor(counter, ActiveCount());
            var exit = npc.HomePoint;

            customer.Recruit(spec, price, spot, exit);
        }

        private DeliveryCounter FindCounter()
        {
            for (int i = 0; i < DeployableDevice.All.Count; i++)
            {
                var d = DeployableDevice.All[i];
                if (d == null || !d.StallOwned) continue;
                if (d.DeviceType != LevelElementType.DeliveryCounter) continue;

                var counter = d.OwnerTransform != null
                    ? d.OwnerTransform.GetComponent<DeliveryCounter>() : null;
                if (counter != null) return counter;
            }
            return null;
        }

        /// <summary>排隊位置：窗口外側往前排開，不要疊在一起。</summary>
        private static Vector3 QueueSpotFor(DeliveryCounter counter, int index)
        {
            var anchor = counter.CustomerQueueAnchor;
            return anchor.position + counter.WindowForward * (index * 1.3f);
        }

        /// <summary>找一隻現在沒在當顧客的羊。</summary>
        private WoolNpc FindIdleNpc()
        {
            WoolNpc best = null;
            int seen = 0;

            for (int i = 0; i < WoolNpc.All.Count; i++)
            {
                var npc = WoolNpc.All[i];
                if (npc == null || npc.Object == null || !npc.Object.IsValid) continue;
                if (npc.IsCustomer) continue;

                // 蓄水池抽樣：不用先建清單就能均勻隨機挑一隻
                seen++;
                if (UnityEngine.Random.Range(0, seen) == 0) best = npc;
            }
            return best;
        }

        // ---------------- 需求 ----------------

        /// <summary>
        /// 掃描場上**還有存量**的素材箱。這是「不會有無解訂單」的保證來源。
        ///
        /// 注意是掃「現在」的存量，所以某個顏色用完之後，新顧客就不會再要那個顏色了。
        /// 已經在排隊的顧客需求不會改 —— 改了玩家會困惑，那是刻意保留的失敗狀態。
        /// </summary>
        private void RefreshAvailableColors()
        {
            _availableColors.Clear();

            for (int i = 0; i < MaterialCrate.All.Count; i++)
            {
                var crate = MaterialCrate.All[i];
                if (crate == null || crate.Object == null || !crate.Object.IsValid) continue;
                if (!crate.HasStock) continue;

                if (!_availableColors.Contains(crate.Color)) _availableColors.Add(crate.Color);
            }

            // 白毛（羊駝毛）永遠不在素材箱裡 —— 它是玩家互相剃出來的現場產物。
            // 只掃箱子的話白色永遠不算「做得出來」，白色訂單就一張都不會生成。
            if (CanShearForWhite() && !_availableColors.Contains(DyeColorType.White))
                _availableColors.Add(DyeColorType.White);
        }

        private readonly List<PatternType> _availablePatterns = new();

        /// <summary>
        /// 從**場上實際擺出來的織布機**抽版型。
        ///
        /// 跟顏色是同一條原則：只要求做得出來的東西。擺了 T恤織布機就出 T-shirt 單，
        /// 擺了襯衫織布機就出襯衫單，兩台都擺就兩種都出。沒擺襯衫織布機卻出襯衫單的話，
        /// 那張單從生出來的那一刻就是無解的。
        ///
        /// 一台都沒有的話退回 T-shirt —— 那時候根本織不出東西，出什麼都一樣，
        /// 但至少不會是 PatternType.None（那不是一件衣服，Describe() 會變成「白色無」）。
        /// </summary>
        private PatternType PickPattern()
        {
            _availablePatterns.Clear();

            for (int i = 0; i < WeavingMachine.All.Count; i++)
            {
                var m = WeavingMachine.All[i];
                if (m == null || m.Object == null || !m.Object.IsValid) continue;
                if (m.OutputPattern == PatternType.None) continue;
                if (!_availablePatterns.Contains(m.OutputPattern)) _availablePatterns.Add(m.OutputPattern);
            }

            if (_availablePatterns.Count == 0) return PatternType.TShirt;
            return _availablePatterns[UnityEngine.Random.Range(0, _availablePatterns.Count)];
        }

        /// <summary>
        /// 現在有沒有白毛的來源。
        ///
        /// 正式玩法的條件是「場上至少有兩隻羊駝」：不能剃自己，所以單人場次
        /// 白毛無解，這時候生白色訂單就違反了「永遠不會有無解訂單」這條保證。
        ///
        /// 不看身上還剩幾份毛 —— 毛每 6 秒長回一份，等一下就有了，
        /// 拿瞬間的存量當條件會讓白色訂單忽有忽無。
        ///
        /// **Editor 與開發版例外：單人也算有來源。** 那兩種組建下
        /// DebugItemSpawner 是存在的，按數字鍵 1 就生得出白毛，所以白色訂單
        /// 解得掉、也才測得到整條白色流程。正式版沒有那個產生器，就回到兩人條件。
        /// </summary>
        private static bool CanShearForWhite()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 除錯鍵「1」＝白毛，單人也拿得到，所以白色訂單不是無解訂單
            return true;
#else
            int alpacas = 0;
            for (int i = 0; i < PlayerController.All.Count; i++)
            {
                var p = PlayerController.All[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                if (++alpacas >= 2) return true;
            }
            return false;
#endif
        }

        private bool TryBuildRequest(out GarmentSpec spec, out int price)
        {
            spec = default;
            price = 0;

            RefreshAvailableColors();
            if (_availableColors.Count == 0) return false;

            var main = _availableColors[UnityEngine.Random.Range(0, _availableColors.Count)];

            // 有一定機率只要單色（低價位訂單）。只有一種顏色可用時也只能單色。
            bool wantsAccent = _availableColors.Count > 1 && UnityEngine.Random.value > 0.35f;

            var accent = main;
            if (wantsAccent)
            {
                // 挑一個跟主色不同的
                for (int guard = 0; guard < 8; guard++)
                {
                    var pick = _availableColors[UnityEngine.Random.Range(0, _availableColors.Count)];
                    if (pick == main) continue;
                    accent = pick;
                    break;
                }
            }

            spec = GarmentSpec.Create(PickPattern(), main, AccessoryType.None, accent);
            price = GameTuning.WoolPrice(main) + (spec.HasAccent ? GameTuning.WoolPrice(accent) : 0);
            return true;
        }

        // ---------------- 交貨 ----------------

        /// <summary>
        /// 有沒有顧客要這件衣服。DeliveryCounter 會先問這裡。
        /// 唯讀，任何端都可以呼叫。
        /// </summary>
        public bool HasMatch(GarmentSpec spec)
        {
            return FindMatch(spec) != null;
        }

        private Customer FindMatch(GarmentSpec spec)
        {
            Customer best = null;
            float leastPatience = float.MaxValue;

            for (int i = 0; i < WoolNpc.All.Count; i++)
            {
                var c = CustomerOf(WoolNpc.All[i]);
                if (c == null || !c.Active) continue;
                if (!c.Wanted.Matches(spec)) continue;

                // 先服務快沒耐心的那一位
                float p = c.Patience01;
                if (p < leastPatience)
                {
                    leastPatience = p;
                    best = c;
                }
            }
            return best;
        }

        /// <summary>
        /// 交貨。比對得到就加錢、顧客離開，回傳 true。
        /// 比對不到回傳 false —— 呼叫端要負責「衣服留在手上」。只在 StateAuthority。
        /// </summary>
        public bool TryDeliver(GarmentSpec spec)
        {
            if (!HasStateAuthority) return false;

            var customer = FindMatch(spec);
            if (customer == null)
            {
                LevelDirector.Instance?.AddMoney(-GameTuning.WrongDeliveryPenalty,
                                                 $"沒人要這件：{spec.Describe()}");
                RPC_Result(false, spec.Describe(), 0);
                return false;
            }

            int price = customer.Price;
            customer.Leave(served: true);

            LevelDirector.Instance?.AddMoney(price, $"賣出 {spec.Describe()}");
            RPC_Result(true, spec.Describe(), price);
            return true;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Result(NetworkBool success, string desc, int price)
        {
            GameAudio.Play(success ? SfxId.ShipSuccess : SfxId.ShipFail);
            if (success) OnCustomerServed?.Invoke(price, desc);
            else OnCustomerLeft?.Invoke(desc);
        }
    }
}
