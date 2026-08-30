using System;
using AlpacasOnFire.Core;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Orders
{
    /// <summary>
    /// 關卡總管：計時、隊伍金額、星級結算。
    /// 金額與時間都是 [Networked]，所有用戶端看到的一致。
    /// </summary>
    public class LevelDirector : NetworkBehaviour
    {
        public static LevelDirector Instance { get; private set; }

        /// <summary>UI 用：金額變動（delta, 原因文字, 是否為扣款）。</summary>
        public static event Action<int, string, bool> OnMoneyChanged;
        /// <summary>UI 用：關卡結束（總金額, 星數）。</summary>
        public static event Action<int, int> OnLevelFinished;

        [Header("Level")]
        [SerializeField] private float _durationSeconds = GameTuning.LevelDurationSeconds;
        [SerializeField] private int _star1 = GameTuning.Star1Threshold;
        [SerializeField] private int _star2 = GameTuning.Star2Threshold;
        [SerializeField] private int _star3 = GameTuning.Star3Threshold;

        [Header("擺攤模式")]
        [Tooltip("開啟後不會自動開始計時，改由 StallManager 在「開張」時呼叫 BeginStallRound()；" +
                 "星級結算也會關閉（程式碼保留，只是不觸發）。")]
        [SerializeField] private bool _stallMode = false;

        /// <summary>擺攤模式：計時由擺攤階段驅動，星級結算關閉。</summary>
        public bool StallMode => _stallMode;

        [Networked] public int Money { get; set; }
        [Networked] public NetworkBool Running { get; set; }
        [Networked] public NetworkBool Finished { get; set; }
        [Networked] public int Stars { get; set; }
        [Networked] private TickTimer LevelTimer { get; set; }

        public float RemainingSeconds => LevelTimer.RemainingTime(Runner) ?? 0f;
        public float Duration => _durationSeconds;
        public int Star1 => _star1;
        public int Star2 => _star2;
        public int Star3 => _star3;

        public override void Spawned()
        {
            Instance = this;
            if (HasStateAuthority)
            {
                Money = 0;
                Finished = false;

                // 擺攤模式：探索階段不限時，等房主按「開張」才開始計時
                Running = !_stallMode;
                LevelTimer = _stallMode ? default : TickTimer.CreateFromSeconds(Runner, _durationSeconds);
            }
        }

        /// <summary>
        /// 擺攤模式：開始一場營業。只在 StateAuthority 呼叫（由 StallManager.OpenForBusiness 觸發）。
        /// 每場的營業額都從 0 重新算，累積由 StallManager.Capital 負責。
        /// </summary>
        public void BeginStallRound(float seconds)
        {
            if (!HasStateAuthority) return;
            Money = 0;
            Stars = 0;
            Finished = false;
            Running = true;
            LevelTimer = TickTimer.CreateFromSeconds(Runner, seconds);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !Running) return;
            if (!LevelTimer.Expired(Runner)) return;

            Running = false;

            // 擺攤模式：不做星級結算，也不設 Finished
            //（Finished 會讓 OrderBoard 清空訂單、讓 ResultsScreen 跳出星級畫面）。
            // 這一場的收尾交給 StallManager，它會看到 Running 變 false 就進入 Settling。
            if (_stallMode) return;

            Finished = true;
            Stars = StarsFor(Money);
            RPC_LevelFinished(Money, Stars);
        }

        public int StarsFor(int money)
        {
            if (money >= _star3) return 3;
            if (money >= _star2) return 2;
            if (money >= _star1) return 1;
            return 0;
        }

        /// <summary>加減金額。只在 StateAuthority 呼叫。</summary>
        public void AddMoney(int delta, string reason)
        {
            if (!HasStateAuthority) return;
            Money += delta;
            RPC_MoneyChanged(delta, reason);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_MoneyChanged(int delta, string reason)
            => OnMoneyChanged?.Invoke(delta, reason, delta < 0);

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_LevelFinished(int money, int stars)
        {
            GameAudio.Play(SfxId.LevelEnd);
            OnLevelFinished?.Invoke(money, stars);
        }
    }
}
