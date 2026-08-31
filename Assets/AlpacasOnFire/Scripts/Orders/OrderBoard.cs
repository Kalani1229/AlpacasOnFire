using System;
using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Machines;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Orders
{
    /// <summary>
    /// 訂單板：生成訂單、倒數、超時扣款、出貨比對。
    /// 用 NetworkArray 同步，容量就是同時存在的訂單上限。
    /// </summary>
    public class OrderBoard : NetworkBehaviour
    {
        /// <summary>訂單板同時能掛的訂單數（NetworkArray 容量）。</summary>
        public const int Slots = 4;

        public static OrderBoard Instance { get; private set; }

        /// <summary>UI 用：出貨結果（成功與否, 描述文字）。</summary>
        public static event Action<bool, string> OnDeliveryResult;
        /// <summary>UI 用：訂單超時。</summary>
        public static event Action<string> OnOrderExpired;

        [Header("訂單內容池")]
        [SerializeField] private PatternType[] _patterns = { PatternType.TShirt };
        [SerializeField] private DyeColorType[] _colors = { DyeColorType.White, DyeColorType.Red };
        [SerializeField] private AccessoryType[] _accessories = { AccessoryType.None };
        [SerializeField, Range(0f, 1f)] private float _accessoryChance = 0f;

        [Networked, Capacity(Slots)] public NetworkArray<OrderEntry> Orders { get; }
        [Networked] private TickTimer SpawnTimer { get; set; }
        [Networked] private int NextOrderId { get; set; }

        public override void Spawned()
        {
            Instance = this;
            if (HasStateAuthority)
            {
                NextOrderId = 1;
                SpawnTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.FirstOrderDelaySeconds);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            var director = LevelDirector.Instance;
            if (director != null && !director.Running)
            {
                if (director.Finished) ClearAll();
                return;
            }

            TickCountdowns(Runner.DeltaTime);

            if (SpawnTimer.ExpiredOrNotRunning(Runner))
            {
                TrySpawnOrder();
                SpawnTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.OrderIntervalSeconds);
            }
        }

        private void TickCountdowns(float dt)
        {
            for (int i = 0; i < Slots; i++)
            {
                var e = Orders[i];
                if (!e.Active) continue;

                e.Remaining -= dt;
                if (e.Remaining > 0f)
                {
                    Orders.Set(i, e);
                    continue;
                }

                // 超時：訂單消失並扣款
                var desc = e.Spec.Describe();
                Orders.Set(i, default);
                LevelDirector.Instance?.AddMoney(-GameTuning.OrderTimeoutPenalty, $"訂單超時：{desc}");
                RPC_OrderExpired(desc);
            }
        }

        private void TrySpawnOrder()
        {
            int slot = -1;
            for (int i = 0; i < Slots; i++)
            {
                if (!Orders[i].Active) { slot = i; break; }
            }
            if (slot < 0) return;
            if (ActiveCount() >= Mathf.Min(Slots, GameTuning.MaxActiveOrders)) return;

            var spec = GarmentSpec.Create(
                PickPattern(),
                PickColor(),
                UnityEngine.Random.value < _accessoryChance ? Pick(_accessories, AccessoryType.None) : AccessoryType.None);

            Orders.Set(slot, new OrderEntry
            {
                Active = true,
                Spec = spec,
                Duration = GameTuning.OrderLifetimeSeconds,
                Remaining = GameTuning.OrderLifetimeSeconds,
                Id = NextOrderId++,
            });
        }

        private static T Pick<T>(T[] pool, T fallback)
            => pool == null || pool.Length == 0 ? fallback : pool[UnityEngine.Random.Range(0, pool.Length)];

        // ---------------- 訂單內容只從「攤位上做得出來的東西」抽 ----------------
        //
        // 一台機器只做一種版型，所以如果訂單要褲子、但攤位上只有 T-shirt 縫紉機，
        // 那張訂單就永遠無解。這裡直接從實際擺出來的裝備推導可能的組合。
        // 攤位還沒擺出來時（例如測試場景）退回 Inspector 上的設定。

        private readonly List<PatternType> _patternBuffer = new();

        private bool HasStallDevices()
        {
            for (int i = 0; i < DeployableDevice.All.Count; i++)
            {
                var dev = DeployableDevice.All[i];
                if (dev != null && dev.Object != null && dev.StallOwned) return true;
            }
            return false;
        }

        private bool HasStallDevice(LevelElementType type)
        {
            for (int i = 0; i < DeployableDevice.All.Count; i++)
            {
                var dev = DeployableDevice.All[i];
                if (dev == null || dev.Object == null || !dev.StallOwned) continue;
                if (dev.DeviceType == type) return true;
            }
            return false;
        }

        private PatternType PickPattern()
        {
            _patternBuffer.Clear();

            for (int i = 0; i < DeployableDevice.All.Count; i++)
            {
                var dev = DeployableDevice.All[i];
                if (dev == null || dev.Object == null || !dev.StallOwned) continue;

                var sewing = dev.GetComponent<SewingMachine>();
                if (sewing == null) continue;
                if (sewing.OutputPattern == PatternType.None) continue;
                if (!_patternBuffer.Contains(sewing.OutputPattern)) _patternBuffer.Add(sewing.OutputPattern);
            }

            if (_patternBuffer.Count > 0)
                return _patternBuffer[UnityEngine.Random.Range(0, _patternBuffer.Count)];

            return Pick(_patterns, PatternType.TShirt);
        }

        private DyeColorType PickColor()
        {
            if (!HasStallDevices()) return Pick(_colors, DyeColorType.White);

            // 要染色得同時有果汁機（做染劑）和噴槍架（噴上去），少一個就只出白色訂單
            bool canDye = HasStallDevice(LevelElementType.Juicer)
                       && HasStallDevice(LevelElementType.ToolRackSprayGun);

            return canDye ? Pick(_colors, DyeColorType.White) : DyeColorType.White;
        }

        public int ActiveCount()
        {
            int n = 0;
            for (int i = 0; i < Slots; i++) if (Orders[i].Active) n++;
            return n;
        }

        /// <summary>
        /// 出貨。比對成功就得分並移除訂單；沒有匹配的訂單就扣款並回報失敗
        /// （箱子留在玩家手上由呼叫端決定，Mailbox 不會清空箱子）。
        /// </summary>
        public bool TryDeliver(GarmentSpec spec)
        {
            if (!HasStateAuthority) return false;

            int match = -1;
            float bestRemaining = float.MaxValue;
            for (int i = 0; i < Slots; i++)
            {
                var e = Orders[i];
                if (!e.Active || !e.Spec.Matches(spec)) continue;
                if (e.Remaining < bestRemaining)   // 先結掉最急的那張
                {
                    bestRemaining = e.Remaining;
                    match = i;
                }
            }

            if (match < 0)
            {
                LevelDirector.Instance?.AddMoney(-GameTuning.WrongDeliveryPenalty, $"錯誤出貨：{spec.Describe()}");
                RPC_DeliveryResult(false, spec.Describe());
                return false;
            }

            int reward = Orders[match].Reward;
            Orders.Set(match, default);
            LevelDirector.Instance?.AddMoney(reward, $"出貨成功：{spec.Describe()}");
            RPC_DeliveryResult(true, spec.Describe());
            return true;
        }

        private void ClearAll()
        {
            for (int i = 0; i < Slots; i++)
                if (Orders[i].Active) Orders.Set(i, default);
        }

        /// <summary>
        /// 外部（擺攤系統結算時）清空訂單板。只在 StateAuthority 呼叫。
        /// </summary>
        public void ClearAllOrders()
        {
            if (!HasStateAuthority) return;
            ClearAll();
        }

        /// <summary>
        /// 重設出單排程，讓下一張訂單從「第一張的延遲」重新算起。
        /// 擺攤模式在按下開張時呼叫，否則探索階段拖太久會讓訂單一開張就全部湧出來。
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public void ResetSpawnSchedule()
        {
            if (!HasStateAuthority) return;
            ClearAll();
            SpawnTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.FirstOrderDelaySeconds);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_DeliveryResult(NetworkBool success, string desc)
        {
            GameAudio.Play(success ? SfxId.ShipSuccess : SfxId.ShipFail);
            OnDeliveryResult?.Invoke(success, desc);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OrderExpired(string desc)
        {
            GameAudio.Play(SfxId.OrderTimeout);
            OnOrderExpired?.Invoke(desc);
        }
    }
}
