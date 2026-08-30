using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 讓一台既有機台變成「可以從手提箱擺出來、也可以收回去」的裝備。
    ///
    /// **機台自己的生產邏輯一行都沒有動。** 做法是把這個元件掛在機台 prefab 的
    /// 子物件「DeployHandle」上，並且給它自己的 trigger 碰撞體：
    ///
    ///   - PlayerInteractor 用 GetComponentInParent&lt;IInteractable&gt;() 收集候選人，
    ///     打到機台本體的碰撞體會拿到 SewingMachine，打到 DeployHandle 會拿到這個元件，
    ///     兩個都會進候選清單，最後由 InteractionPriority 決定誰贏。
    ///   - 佈置模式：這裡回傳高優先權且 CanInteract = true  -> Space 是「拿起機台」
    ///   - 營業模式：這裡 CanInteract = false               -> Space 落回機台本體的正常操作
    ///
    /// 所以「同一顆 Space 在兩種模式下意義不同」是靠優先權自然分流的，
    /// 不需要在既有機台裡塞任何模式判斷。
    /// </summary>
    public class DeployableDevice : NetworkBehaviour, IInteractable
    {
        /// <summary>場上所有的可擺放裝備（避免每次驗證都 FindObjectsOfType）。</summary>
        public static readonly List<DeployableDevice> All = new();

        [Header("Deployable")]
        [SerializeField] private LevelElementType _deviceType = LevelElementType.SewingMachine;
        [SerializeField] private Transform _interactionAnchor;

        /// <summary>被擺出來的裝備才歸攤位管（場景裡手動擺的機台不受影響）。</summary>
        [Networked] public NetworkBool StallOwned { get; set; }
        [Networked] public int DeviceTypeRaw { get; set; }

        public LevelElementType DeviceType =>
            DeviceTypeRaw != 0 ? (LevelElementType)DeviceTypeRaw : _deviceType;

        /// <summary>機台的根物件（DeployHandle 掛在子物件上，收回時要 Despawn 根）。</summary>
        public NetworkObject OwnerObject => Object;
        public Transform OwnerTransform => Object != null ? Object.transform : transform;

        private StallManager Stall => StallManager.Instance;

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        /// <summary>由 StallManager 在 Runner.Spawn 的初始化回呼裡設定。</summary>
        public void MarkDeployed(LevelElementType type)
        {
            StallOwned = true;
            DeviceTypeRaw = (int)type;
        }

        // ---------------- IInteractable ----------------

        public Transform InteractionAnchor =>
            _interactionAnchor != null ? _interactionAnchor : transform;

        /// <summary>
        /// 佈置模式時要壓過機台本體（1）、地上的雜物（0）與隊友（2），
        /// 但不能壓過手提箱（5），因為手提箱是開選單的入口。
        /// </summary>
        public int InteractionPriority => 4;

        public bool CanInteract(in InteractionContext ctx)
        {
            if (!StallOwned) return false;
            if (Stall == null || !Stall.IsArrangeMode) return false;   // 營業中不能移動
            if (ctx.Player == null) return false;

            // 已經有待放置的機台時，Space 的意義是「放下」，不是「再拿一台」
            var agent = ctx.Player.GetComponent<PlayerStallAgent>();
            if (agent != null && agent.HasPending) return false;

            return true;
        }

        public string GetPrompt(in InteractionContext ctx)
        {
            if (!CanInteract(in ctx)) return null;
            return $"[Space] 拿起{StallCatalog.DisplayName(DeviceType)}重新擺放";
        }

        public void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;
            if (!CanInteract(in ctx)) return;

            var agent = ctx.Player.GetComponent<PlayerStallAgent>();
            if (agent == null) return;

            // 拿起來 = 收回箱中 + 進入這台機台的放置預覽
            var type = DeviceType;
            var pos = OwnerTransform.position;
            float yaw = OwnerTransform.eulerAngles.y;

            GameAudio.PlayAt(SfxId.DevicePickup, pos);
            Runner.Despawn(OwnerObject);

            agent.BeginPlacement(type, yaw);
        }
    }
}
