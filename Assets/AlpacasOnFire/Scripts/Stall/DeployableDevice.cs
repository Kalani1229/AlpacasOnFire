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
    ///   - 佈置模式：這裡回傳高優先權且 CanInteract = true  -> Space 是「拿起裝備」
    ///   - 營業模式：這裡 CanInteract = false               -> Space 落回機台本體的正常操作
    ///
    /// 位置同步用**整數格子座標 + 朝向索引**，世界座標由 StallGrid 推算。
    /// 所以這些裝備身上不需要 NetworkTransform，也不會有浮點誤差造成的兩端不一致。
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

        // ---- 位置：整數格子座標 + 朝向索引，不同步浮點世界座標 ----
        [Networked] public int CellX { get; set; }
        [Networked] public int CellZ { get; set; }
        [Networked] public int FacingRaw { get; set; }

        /// <summary>開箱時的彈出順序，用來錯開動畫。純視覺用途。</summary>
        [Networked] public int PopOrder { get; set; }

        /// <summary>
        /// 型別專屬參數。目前只有素材箱用（存顏色）。
        /// 拿起來重擺時要一起帶著走，不然箱子重放之後顏色會不見。
        /// </summary>
        [Networked] public int Variant { get; set; }

        public LevelElementType DeviceType =>
            DeviceTypeRaw != 0 ? (LevelElementType)DeviceTypeRaw : _deviceType;

        public int Facing => StallGrid.NormalizeFacing(FacingRaw);
        public Vector2Int Footprint => StallCatalog.Footprint(DeviceType);

        /// <summary>機台的根物件（DeployHandle 掛在子物件上，收回時要 Despawn 根）。</summary>
        public NetworkObject OwnerObject => Object;
        public Transform OwnerTransform => Object != null ? Object.transform : transform;

        private StallManager Stall => StallManager.Instance;

        private float _popElapsed = -1f;
        private bool _popSoundPlayed;

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);

            ApplyGridTransform(0f, 1f);

            // 彈出動畫：每個用戶端各自播，不同步。純視覺，差幾毫秒沒有影響。
            _popElapsed = 0f;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        /// <summary>由 StallManager 在 Runner.Spawn 的初始化回呼裡設定。</summary>
        public void MarkDeployed(LevelElementType type, int cellX, int cellZ, int facing, int popOrder,
                                 int variant = 0)
        {
            StallOwned = true;
            DeviceTypeRaw = (int)type;
            CellX = cellX;
            CellZ = cellZ;
            FacingRaw = StallGrid.NormalizeFacing(facing);
            PopOrder = popOrder;
            Variant = variant;
        }

        /// <summary>目前這台裝備的佈局紀錄（收攤時寫回手提箱用）。</summary>
        public StallSlotRecord ToRecord()
            => StallSlotRecord.Create(DeviceType, CellX, CellZ, Facing, Variant);

        // ---------------- 位置：由格子座標推算 ----------------

        public override void FixedUpdateNetwork()
        {
            // 權威端也要維持正確的 transform，碰撞體與互動射線才對得上
            if (_popElapsed < 0f) ApplyGridTransform(0f, 1f);
        }

        public override void Render()
        {
            if (_popElapsed >= 0f)
            {
                _popElapsed += Time.deltaTime;

                float delay = PopOrder * GameTuning.StallPopStagger;
                float t = Mathf.Clamp01((_popElapsed - delay) / GameTuning.StallPopDuration);

                if (_popElapsed - delay < 0f)
                {
                    // 還沒輪到自己：先縮成看不見，避免整批同時出現
                    ApplyGridTransform(0f, 0.001f);
                    return;
                }

                // 音效在「輪到自己彈出來的那一刻」放，不是動畫結束才放
                if (!_popSoundPlayed)
                {
                    _popSoundPlayed = true;
                    GameAudio.PlayAt(SfxId.StallPopOut, OwnerTransform.position, 0.5f);
                }

                ApplyGridTransform(PopHeight(t), PopScale(t));

                if (t >= 1f)
                {
                    _popElapsed = -1f;
                    ApplyGridTransform(0f, 1f);
                }
                return;
            }

            ApplyGridTransform(0f, 1f);
        }

        /// <summary>往上拋一個小拋物線再落回格子。</summary>
        private static float PopHeight(float t)
            => GameTuning.StallPopHeight * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.35f);

        /// <summary>從 0 放大到 1，尾段有一點回彈。</summary>
        private static float PopScale(float t)
        {
            float overshoot = GameTuning.StallPopOvershoot;
            if (t < 0.7f) return Mathf.Lerp(0.05f, overshoot, t / 0.7f);
            return Mathf.Lerp(overshoot, 1f, (t - 0.7f) / 0.3f);
        }

        /// <summary>
        /// 把整數格子座標換算成世界 transform。**這是位置的唯一來源** ——
        /// 每個用戶端都用同一組整數算，結果一定一致。
        /// </summary>
        private void ApplyGridTransform(float heightOffset, float scale)
        {
            var stall = Stall;
            if (stall == null || !stall.MatDeployed || !StallOwned) return;

            var root = OwnerTransform;
            if (root == null) return;

            root.position = StallGrid.CellToWorld(CellX, CellZ, stall.MatCenter, stall.MatYaw)
                          + Vector3.up * heightOffset;
            root.rotation = StallGrid.FacingToRotation(FacingRaw, stall.MatYaw);
            root.localScale = Vector3.one * scale;
        }

        // ---------------- IInteractable ----------------

        public Transform InteractionAnchor =>
            _interactionAnchor != null ? _interactionAnchor : transform;

        /// <summary>
        /// 佈置模式時要壓過機台本體（1）、地上的雜物（0）與隊友（2），
        /// 但不能壓過手提箱（5），因為手提箱是收攤的入口。
        /// </summary>
        public int InteractionPriority => 4;

        public bool CanInteract(in InteractionContext ctx)
        {
            if (!StallOwned) return false;
            if (Stall == null || !Stall.IsArrangeMode) return false;   // 營業中不能移動
            if (ctx.Player == null) return false;

            // 已經拿著一台裝備時，Space 的意義是「放下」，不是「再拿一台」
            var agent = ctx.Player.GetComponent<PlayerStallAgent>();
            if (agent != null && agent.HasPending) return false;

            return true;
        }

        public string GetPrompt(in InteractionContext ctx)
        {
            if (!CanInteract(in ctx)) return null;
            return $"[Space] 拿起{StallCatalog.DisplayName(DeviceType)}";
        }

        public void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;
            if (!CanInteract(in ctx)) return;

            var agent = ctx.Player.GetComponent<PlayerStallAgent>();
            if (agent == null) return;

            // 拿起來 = 從格子上移除 + 進入放置預覽（記住原本的格子，Q 取消時要放回去）
            var type = DeviceType;
            int cx = CellX, cz = CellZ, facing = Facing, variant = Variant;

            GameAudio.PlayAt(SfxId.DevicePickup, OwnerTransform.position);
            Runner.Despawn(OwnerObject);

            agent.BeginPlacement(type, facing, cx, cz, variant);
        }
    }
}
