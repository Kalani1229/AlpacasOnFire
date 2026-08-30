using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Player;
using AlpacasOnFire.UI;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 手提箱：整套攤位裝在裡面，取代背包系統。
    ///
    /// 它是 CarriableItem 的子類，所以「一次只能拿一件」的既有規則自動讓它佔用雙手，
    /// 丟接也照舊 —— 不需要在 PlayerCarry 裡加任何特例。
    ///
    /// 兩種互動情境（都掛在同一個 IInteractable 上，不新增按鍵）：
    ///  1. 拿在手上、面向前方地面按 Space -> 開箱（展開襯布）
    ///  2. 已展開、對著箱子按 Space      -> 打開裝備選單（裡面有收攤與開張）
    ///
    /// 為什麼拿在手上還能被準心選到：CarriableItem 只會關掉「非 trigger」的碰撞體，
    /// 所以 prefab 上額外掛的 trigger 碰撞體在手上時仍然存在，
    /// 而 PlayerInteractor 的查詢是 QueryTriggerInteraction.Collide。
    /// </summary>
    public class SuitcaseItem : CarriableItem
    {
        [Header("Suitcase")]
        [SerializeField] private Transform _lidVisual;
        [SerializeField] private Renderer _stateLight;

        private MaterialPropertyBlock _suitcaseMpb;

        /// <summary>
        /// 互動優先權的全域排序（數字大的贏）：
        ///   0 地面雜物　1 機台　2 隊友　4 DeployableDevice　5 手提箱　99 放置捕捉器
        /// 手提箱要最高（放置捕捉器除外），因為它是整個擺攤流程的入口，
        /// 選不到它就什麼都做不了。背後的東西不會被誤選 —— PlayerInteractor 本來就有
        /// 「必須大致在面前」的夾角檢查。
        /// </summary>
        public override int InteractionPriority => 5;

        private StallManager Stall => StallManager.Instance;

        /// <summary>這個箱子目前是不是「已展開」的狀態（襯布在地上，箱子躺在襯布中央）。</summary>
        public bool IsDeployed => !IsHeld && Stall != null && Stall.MatDeployed;

        // ---------------- 互動 ----------------

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (ctx.Player == null || Stall == null) return false;

            // 放置預覽進行中：這時候 Space 的意義是「放下機台」，交給襯布目標處理
            var agent = ctx.Player.GetComponent<PlayerStallAgent>();
            if (agent != null && agent.HasPending) return false;

            // 情境 1：拿在自己手上 -> 開箱
            if (IsHeld)
            {
                if (ctx.Held != this) return false;          // 別人手上的箱子不能操作
                return !Stall.MatDeployed;                   // 已經有攤位就不能再開一個
            }

            // 情境 2：躺在地上的已展開手提箱 -> 開選單
            if (IsDeployed) return true;

            // 情境 3：地上沒展開的箱子 -> 走 CarriableItem 的撿起邏輯
            return base.CanInteract(in ctx);
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (Stall == null) return null;

            if (IsHeld)
            {
                if (ctx.Held != this) return null;
                if (Stall.MatDeployed) return "攤位已經擺開了";

                var reason = Stall.CheckDeploySpot(ctx.Player.transform.position,
                                                   ctx.Player.transform.eulerAngles.y, out _, out _);
                return reason == PlacementResult.Ok
                    ? "[Space] 打開手提箱擺攤"
                    : $"這裡不能擺攤：{StallGeometry.Describe(reason)}";
            }

            if (IsDeployed)
                return Stall.IsBusinessMode ? "[Space] 攤位選單（營業中）" : "[Space] 攤位選單";

            return base.GetPrompt(in ctx);
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || Stall == null) return;

            // ---- 開箱 ----
            if (IsHeld)
            {
                if (ctx.Held != this || Stall.MatDeployed) return;

                var player = ctx.Player;
                if (!Stall.TryDeployMat(player.transform.position, player.transform.eulerAngles.y, out _))
                    return;

                // 箱子從手上落到襯布中央
                player.Carry.ReleaseHeld();
                DetachToGround(Stall.MatCenter);
                return;
            }

            // ---- 開選單 ----
            if (IsDeployed)
            {
                if (ctx.Player.Object.HasInputAuthority)
                    SuitcasePanel.Open(this);
                else
                    RPC_OpenSuitcasePanel(ctx.Player.Object.InputAuthority);
                return;
            }

            base.Interact(in ctx);
        }

        /// <summary>叫指定玩家的用戶端打開攤位選單（跟縫紉機選版型走同一套做法）。</summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OpenSuitcasePanel([RpcTarget] PlayerRef player)
        {
            SuitcasePanel.Open(this);
        }

        /// <summary>
        /// 收攤之後把箱子送回玩家手上。手不空的話就留在原地（回傳 false，由呼叫端提示）。
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public bool ReturnToHands(PlayerController player)
        {
            if (!HasStateAuthority || player == null) return false;
            if (player.Carry.HasItem) return false;
            return player.Carry.TryPickup(this);
        }

        // ---------------- 表現 ----------------

        public override void Render()
        {
            base.Render();

            bool open = IsDeployed;
            if (_lidVisual != null)
            {
                // 開箱後箱蓋掀起來，遠遠就看得出攤位開著
                var target = open ? Quaternion.Euler(-105f, 0f, 0f) : Quaternion.identity;
                _lidVisual.localRotation = Quaternion.Slerp(_lidVisual.localRotation, target,
                                                            1f - Mathf.Exp(-12f * Time.deltaTime));
            }

            if (_stateLight != null)
            {
                var stall = Stall;
                Color c = !open ? new Color(0.35f, 0.35f, 0.38f)
                        : stall != null && stall.IsBusinessMode ? new Color(0.35f, 0.85f, 1f)
                        : new Color(0.98f, 0.80f, 0.30f);

                _suitcaseMpb ??= new MaterialPropertyBlock();
                _stateLight.GetPropertyBlock(_suitcaseMpb);
                _suitcaseMpb.SetColor("_BaseColor", c);
                _suitcaseMpb.SetColor("_Color", c);
                _stateLight.SetPropertyBlock(_suitcaseMpb);
            }
        }

        public override string DisplayName => "手提箱";
    }
}
