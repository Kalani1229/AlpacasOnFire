using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Player;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Casino
{
    /// <summary>
    /// 遜咖賭場的每日共用狀態。**掛在 [GameSystems] 上**（選單「羊駝很忙/賭場：…」會幫你掛）。
    ///
    /// 為什麼不放在櫃檯上：櫃檯跟著夜晚生滅，放在它身上的狀態每天天亮就丟了。
    /// [GameSystems] 跟場景同壽，今天這副牌被買掉幾張、櫃檯現在是誰在用，都放這裡。
    ///
    /// 這支也負責：
    ///   - 夜晚 Spawn 刮刮樂櫃檯、天亮 Despawn（只有主機做）
    ///   - **購買的權威流程**（第 6 節）：client 只送「我要買哪幾張」，
    ///     主機驗證、扣款、**一次抽完所有結果**、立刻把獎金入帳，再把結果一次送回給買的人。
    ///     刮開的動畫純粹是演出 —— 斷線、關面板、崩潰都不會吃掉獎金。
    ///
    /// 沒有 run 模式的場景（Stall_Test）不會掛這支；就算掛了也什麼都不做。
    /// </summary>
    public class CasinoState : NetworkBehaviour
    {
        public static CasinoState Instance { get; private set; }

        public const int ScratchCardCount = 10;

        /// <summary>今天這一副牌，哪幾張已經被買走了。全隊共用。</summary>
        [Networked, Capacity(ScratchCardCount)]
        public NetworkArray<NetworkBool> CardTaken { get; }

        /// <summary>今晚的刮刮樂櫃檯（主機 Spawn 的）。</summary>
        [Networked] public NetworkId ScratchCounterId { get; set; }

        /// <summary>還剩幾張可以買。</summary>
        public int CardsLeft
        {
            get
            {
                if (Object == null || !Object.IsValid) return 0;
                int n = 0;
                for (int i = 0; i < ScratchCardCount; i++) if (!CardTaken[i]) n++;
                return n;
            }
        }

        public bool IsCardTaken(int index)
            => Object != null && Object.IsValid && index >= 0 && index < ScratchCardCount && CardTaken[index];

        /// <summary>
        /// 主機端：還沒被「揭曉」回報的大獎（玩家 -> 卡號 -> 金額）。
        /// 廣播延到買的人自己刮開那張才發 —— 一買就廣播的話，等於當場劇透他自己。
        /// 這只是「什麼時候說」，錢早就入帳了，所以存在本機欄位就好。
        /// </summary>
        private readonly Dictionary<PlayerRef, Dictionary<int, int>> _pendingBigWins = new();

        private bool _warnedNoPrefab;

        // ---------------------------------------------------------------- 生命週期

        public override void Spawned()
        {
            Instance = this;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>全部設回沒被買走。由 StallManager.BeginDay() 呼叫。只在 StateAuthority。</summary>
        public void ResetDay()
        {
            if (!HasStateAuthority) return;
            for (int i = 0; i < ScratchCardCount; i++) CardTaken.Set(i, false);
            _pendingBigWins.Clear();
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;
            SyncScratchCounter();
        }

        /// <summary>夜晚確保櫃檯存在，其他時候收掉。只有 run 模式。</summary>
        private void SyncScratchCounter()
        {
            var stall = StallManager.Instance;
            bool want = stall != null && stall.Object != null && stall.Object.IsValid
                        && stall.RunMode && stall.IsNight;

            var counter = FindCounter();

            if (!want)
            {
                if (counter != null) Runner.Despawn(counter.Object);
                ScratchCounterId = default;
                return;
            }

            if (counter != null) return;

            var catalog = GameCatalog.Instance;
            var prefab = catalog != null ? catalog.GetElement(LevelElementType.ScratchCardCounter) : null;
            var netPrefab = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            if (netPrefab == null)
            {
                if (!_warnedNoPrefab)
                {
                    _warnedNoPrefab = true;
                    Debug.LogError("[賭場] GameCatalog 裡沒有刮刮樂櫃檯的 prefab。" +
                                   "請執行選單「羊駝很忙 / 賭場：建置刮刮樂櫃檯並掛上賭場狀態」。");
                }
                return;
            }

            // 位置由櫃檯自己在 Spawned() 裡算（每一端算出來一樣），這裡給一樣的值只是讓第一幀就在對的地方
            ScratchCardCounter.HomePlacement(stall, out var pos, out var rot);
            var obj = Runner.Spawn(netPrefab, pos, rot);
            if (obj != null) ScratchCounterId = obj.Id;
        }

        public ScratchCardCounter FindCounter()
        {
            if (!ScratchCounterId.IsValid || Runner == null) return null;
            if (!Runner.TryFindObject(ScratchCounterId, out var obj) || obj == null || !obj.IsValid) return null;
            return obj.GetComponent<ScratchCardCounter>();
        }

        // ---------------------------------------------------------------- 購買（第 6 節）

        /// <summary>
        /// client -> 主機：我要買這幾張（bit i = 第 i 張）。**只送選擇，不送任何結果。**
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_BuyScratchCards(int mask, RpcInfo info = default)
        {
            var source = info.Source;
            if (source == PlayerRef.None && Runner != null) source = Runner.LocalPlayer;   // 主機自己按
            BuyScratchCards(source, mask);
        }

        private void BuyScratchCards(PlayerRef buyer, int mask)
        {
            if (!HasStateAuthority) return;

            var stall = StallManager.Instance;
            var player = FindPlayer(buyer);
            var counter = FindCounter();

            string reject = null;
            if (stall == null || !stall.RunMode || !stall.IsNight) reject = "天亮了，刮刮樂收攤了";
            else if (player == null) reject = "找不到你";
            else if (counter == null || counter.Occupant != player.Object.Id) reject = "你沒有在櫃檯前";

            mask &= (1 << ScratchCardCount) - 1;
            int count = 0;
            if (reject == null)
            {
                for (int i = 0; i < ScratchCardCount; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    if (CardTaken[i]) { reject = "有幾張已經被別人買走了"; break; }
                    count++;
                }
                if (reject == null && count == 0) reject = "還沒選卡";
            }

            int cost = count * GameTuning.ScratchCardPrice;
            if (reject == null && !stall.TrySpendCapital(cost, $"刮刮樂 {count} 張"))
                reject = "錢不夠";

            if (reject != null)
            {
                RPC_ScratchRejected(buyer, reject);
                return;
            }

            // ---- 一次抽完所有張，錢當場結清 ----
            int packed = 0;
            int won = 0;
            var pending = new Dictionary<int, int>();
            for (int i = 0; i < ScratchCardCount; i++)
            {
                if ((mask & (1 << i)) == 0) continue;

                int tier = RollTier();
                packed |= tier << (i * 3);
                CardTaken.Set(i, true);

                int amount = GameTuning.ScratchCardPrice * GameTuning.ScratchPrizeMultipliers[tier];
                won += amount;
                if (GameTuning.ScratchPrizeMultipliers[tier] >= GameTuning.ScratchBroadcastMultiplier)
                    pending[i] = amount;
            }

            stall.AddCapital(won, "刮刮樂獎金");
            if (pending.Count > 0) _pendingBigWins[buyer] = pending;

            Debug.Log($"[賭場] {PlayerLabel(player)} 買了 {count} 張，花 {cost}、拿回 {won}。");

            // 結果**一次**送回給買的人（每張 3 bits，10 張 30 bits 打包進一個 int）
            RPC_ScratchResult(buyer, mask, packed, cost, won);
        }

        /// <summary>依 GameTuning 的機率表抽一個獎項等級。只在主機上跑。</summary>
        private static int RollTier()
        {
            float r = Random.value;
            var odds = GameTuning.ScratchPrizeOdds;
            float acc = 0f;
            for (int t = 0; t < odds.Length; t++)
            {
                acc += odds[t];
                if (r < acc) return t;
            }
            return 0;   // 浮點誤差剩下的那一點點算沒中
        }

        /// <summary>從打包的結果取出第 i 張的獎項等級。</summary>
        public static int UnpackTier(int packed, int index) => (packed >> (index * 3)) & 0x7;

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ScratchResult([RpcTarget] PlayerRef buyer, int mask, int packedTiers, int spent, int won)
        {
            ScratchCardPanel.Instance?.OnPurchaseResult(mask, packedTiers, spent, won);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ScratchRejected([RpcTarget] PlayerRef buyer, string reason)
        {
            ScratchCardPanel.Instance?.OnPurchaseRejected(reason);
        }

        // ---------------------------------------------------------------- 大獎廣播（第 8 節）

        /// <summary>
        /// client -> 主機：我刮開了第 index 張。主機只在**那張真的是他買到的大獎**時才廣播 ——
        /// client 能決定的只有「什麼時候說」，說什麼由主機手上的結果決定。
        /// </summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ScratchRevealed(int index, RpcInfo info = default)
        {
            var source = info.Source;
            if (source == PlayerRef.None && Runner != null) source = Runner.LocalPlayer;

            if (!_pendingBigWins.TryGetValue(source, out var pending)) return;
            if (!pending.TryGetValue(index, out int amount)) return;
            pending.Remove(index);

            var player = FindPlayer(source);
            int colorIndex = player != null ? player.ColorIndex : 0;
            bool jackpot = amount >= GameTuning.ScratchCardPrice * GameTuning.ScratchPrizeMultipliers[GameTuning.ScratchPrizeMultipliers.Length - 1];
            RPC_BigWin(source, colorIndex, amount, jackpot);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_BigWin(PlayerRef winner, int colorIndex, int amount, NetworkBool jackpot)
        {
            // 自己看到「你」，別人看到 P1～P4
            bool me = Runner != null && winner == Runner.LocalPlayer;
            string who = me ? "你" : $"P{colorIndex + 1} ";
            StallManager.LocalNotice(jackpot ? $"{who}刮中頭獎 {amount}！！" : $"{who}刮中 {amount}！");
            GameAudio.Play(SfxId.BellRing);
        }

        // ---------------------------------------------------------------- 工具

        private static PlayerController FindPlayer(PlayerRef pref)
        {
            for (int i = 0; i < PlayerController.All.Count; i++)
            {
                var p = PlayerController.All[i];
                if (p == null || p.Object == null || !p.Object.IsValid) continue;
                if (p.Object.InputAuthority == pref) return p;
            }
            return null;
        }

        private static string PlayerLabel(PlayerController p) => p != null ? $"P{p.ColorIndex + 1}" : "?";
    }
}
