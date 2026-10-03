using AlpacasOnFire.Interaction;
using AlpacasOnFire.Player;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Casino
{
    /// <summary>
    /// 小遊戲機台的共用基底（刮刮樂櫃檯；之後的烏龜賽跑、夾娃娃機也繼承它）。
    ///
    /// 三層框架的第一層：
    ///   機台（這支，網路物件）    —— 誰在用（佔用鎖）、能不能用、右鍵打開面板
    ///   面板基底 MinigamePanel    —— 純本機 UI：開關、游標、Esc、擋輸入
    ///   各遊戲的面板與規則         —— 繼承上面兩個
    ///
    /// **右鍵進入**（跟手提箱選色同一套規則：左鍵是即時動作、右鍵是開面板）。
    /// 左鍵不做事，但要給提示，不要靜默無反應。
    ///
    /// 佔用鎖：一次只能一個人玩。釋放的時機 ——
    ///   - 玩家關掉面板（RPC_Leave）
    ///   - 玩家斷線（佔用者的物件找不到了）
    ///   - 機台不能用了（例如天亮；夜晚機台直接被 Despawn，鎖跟著消失）
    /// </summary>
    public abstract class MinigameMachine : NetworkInteractable, ISecondaryInteractable
    {
        /// <summary>現在是誰在玩（玩家的 NetworkObject.Id）。無效 = 沒人。</summary>
        [Networked] public NetworkId Occupant { get; set; }

        // ---------------- 子類別要提供的 ----------------

        /// <summary>現在能不能玩（例如刮刮樂只在夜晚）。</summary>
        protected abstract bool Available { get; }

        /// <summary>右鍵提示的動作名稱，例如「玩刮刮樂（剩 7 張）」。</summary>
        protected abstract string PlayLabel { get; }

        /// <summary>在**本機**打開這台機台的面板。只會在佔用者自己的 client 上被呼叫。</summary>
        protected abstract void OpenPanelLocal();

        // ---------------- 佔用 ----------------

        public bool IsOccupied => Occupant.IsValid;

        public bool IsOccupiedBy(PlayerController p)
            => p != null && p.Object != null && Occupant.IsValid && Occupant == p.Object.Id;

        /// <summary>本機玩家是不是正在用這台。面板用它判斷要不要自動關閉。</summary>
        public bool LocalIsOccupant
        {
            get
            {
                var local = PlayerController.Local;
                return Object != null && Object.IsValid && IsOccupiedBy(local);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !Occupant.IsValid) return;

            // 斷線：佔用者的物件不見了 -> 釋放。不能用了（天亮）-> 釋放
            bool gone = !Runner.TryFindObject(Occupant, out var obj) || obj == null || !obj.IsValid;
            if (gone || !Available) Occupant = default;
        }

        // ---------------- 左鍵：只給提示 ----------------

        public override int InteractionPriority => 3;

        public override bool CanInteract(in InteractionContext ctx) => ctx.Player != null && Available;

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (!CanInteract(in ctx)) return null;
            if (IsOccupied && !IsOccupiedBy(ctx.Player)) return "有人在玩";
            return $"[右鍵] {PlayLabel}";
        }

        public override void Interact(in InteractionContext ctx) { }

        // ---------------- 右鍵：進入 ----------------

        /// <summary>有人在玩也回 true —— 提示要寫得出「有人在玩」，按下去才拒絕。</summary>
        public bool CanSecondaryInteract(in InteractionContext ctx) => ctx.Player != null && Available;

        public string GetSecondaryPrompt(in InteractionContext ctx)
        {
            if (!CanSecondaryInteract(in ctx)) return null;
            if (IsOccupied && !IsOccupiedBy(ctx.Player)) return "有人在玩";
            return $"[右鍵] {PlayLabel}";
        }

        /// <summary>只在狀態權威上呼叫。上鎖，然後只在那個玩家的 client 打開面板。</summary>
        public void SecondaryInteract(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !CanSecondaryInteract(in ctx)) return;
            var player = ctx.Player;
            if (player.Object == null) return;

            if (IsOccupied && !IsOccupiedBy(player))
            {
                StallManager.Instance?.NoticeTo(player.Object.InputAuthority, "有人在玩，等一下");
                return;
            }

            Occupant = player.Object.Id;

            if (player.Object.HasInputAuthority) OpenPanelLocal();
            else RPC_OpenPanel(player.Object.InputAuthority);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OpenPanel([RpcTarget] PlayerRef player)
        {
            OpenPanelLocal();
        }

        /// <summary>面板關掉時由佔用者送出，釋放佔用鎖。</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_Leave(RpcInfo info = default)
        {
            var source = info.Source;
            if (source == PlayerRef.None && Runner != null) source = Runner.LocalPlayer;

            if (!Occupant.IsValid) return;
            if (!Runner.TryFindObject(Occupant, out var obj) || obj == null || obj.InputAuthority == source)
                Occupant = default;
        }
    }
}
