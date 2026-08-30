using System;
using AlpacasOnFire.Core;
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
                Pick(_patterns, PatternType.TShirt),
                Pick(_colors, DyeColorType.White),
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
