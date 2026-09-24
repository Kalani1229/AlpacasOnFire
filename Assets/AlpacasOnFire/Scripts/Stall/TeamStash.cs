using System;
using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 全隊共用的大背包。掛在場景的 [GameSystems] 上（跟 LevelDirector / OrderBoard / StallManager 同一個
    /// NetworkObject），單例的寫法照 OrderBoard.Instance 的既有慣例。
    ///
    /// v6 的核心轉向是「素材長在會走動的 NPC 身上」—— 剃下來的毛不會掉在地上，
    /// 直接進這個共用背包，所以隊友剃的毛你也用得到，不用互相傳遞。
    ///
    /// 存量用 NetworkArray&lt;int&gt; 同步，索引就是 DyeColorType 的數值。
    /// 容量開 8 是留餘裕（目前只有 5 色），之後加顏色不用改同步結構。
    /// </summary>
    public class TeamStash : NetworkBehaviour
    {
        public static TeamStash Instance { get; private set; }

        /// <summary>HUD 用：存量有變動。參數是變動的顏色與變化量（正 = 增加）。</summary>
        public static event Action<DyeColorType, int> OnStashChanged;

        /// <summary>索引 = (int)DyeColorType。容量 8 是留餘裕，目前只用到 5。</summary>
        [Networked, Capacity(8)] public NetworkArray<int> Wool { get; }

        public override void Spawned()
        {
            Instance = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        // ---------------- 查詢 ----------------

        public int Count(DyeColorType color)
        {
            int i = (int)color;
            return i >= 0 && i < Wool.Length ? Wool[i] : 0;
        }

        public int TotalCount()
        {
            int n = 0;
            for (int i = 0; i < Wool.Length; i++) n += Wool[i];
            return n;
        }

        /// <summary>背包裡有存量的顏色（素材箱要靠這個決定可以切換到哪些顏色）。</summary>
        public bool HasAny(DyeColorType color) => Count(color) > 0;

        // ---------------- 存取 ----------------

        /// <summary>
        /// 放進背包。滿了就整筆拒收並回傳 false（不做部分收下，免得玩家搞不清楚到底進去多少）。
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public bool TryAdd(DyeColorType color, int amount = 1)
        {
            if (!HasStateAuthority || amount <= 0) return false;

            int i = (int)color;
            if (i < 0 || i >= Wool.Length) return false;

            int next = Wool[i] + amount;
            if (next > GameTuning.StashCapacityPerColor)
            {
                RPC_StashFull(color);
                return false;
            }

            Wool.Set(i, next);
            RPC_Changed(color, amount);
            return true;
        }

        /// <summary>從背包拿出來。不夠就整筆失敗。只在 StateAuthority 呼叫。</summary>
        public bool TryTake(DyeColorType color, int amount = 1)
        {
            if (!HasStateAuthority || amount <= 0) return false;

            int i = (int)color;
            if (i < 0 || i >= Wool.Length) return false;
            if (Wool[i] < amount) return false;

            Wool.Set(i, Wool[i] - amount);
            RPC_Changed(color, -amount);
            return true;
        }

        /// <summary>盡量拿，拿得到多少算多少，回傳實際拿到的數量（素材箱開張時扣款用）。</summary>
        public int TakeUpTo(DyeColorType color, int max)
        {
            if (!HasStateAuthority || max <= 0) return 0;

            int i = (int)color;
            if (i < 0 || i >= Wool.Length) return 0;

            int taken = Mathf.Min(max, Wool[i]);
            if (taken <= 0) return 0;

            Wool.Set(i, Wool[i] - taken);
            RPC_Changed(color, -taken);
            return taken;
        }

        // ---------------- RPC ----------------

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Changed(DyeColorType color, int delta)
        {
            OnStashChanged?.Invoke(color, delta);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_StashFull(DyeColorType color)
        {
            StallManager.LocalNotice($"{PlaceholderPalette.DyeName(color)}毛裝滿了（上限 {GameTuning.StashCapacityPerColor}）");
        }
    }
}
