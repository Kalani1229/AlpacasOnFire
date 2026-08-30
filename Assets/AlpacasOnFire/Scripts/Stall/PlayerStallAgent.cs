using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 每個玩家的擺攤狀態：目前正在放置哪一台機台、預覽轉了幾度。
    /// 掛在玩家 prefab 上（由 PlaceholderAssetBuilder 加上去），不修改 PlayerController。
    ///
    /// 分工：
    ///  - [Networked] 的待放置類型與角度：所有人一致，狀態權威據此執行放置
    ///  - 幽靈模型與滾輪／Q 的讀取：只有輸入權那一端做，走 RPC 回報
    ///  - 真正的放置座標：在 StateAuthority 上用 InteractionContext 重算，不信任用戶端傳來的座標
    /// </summary>
    public class PlayerStallAgent : NetworkBehaviour
    {
        [Networked] public int PendingTypeRaw { get; set; }
        [Networked] public float PendingYaw { get; set; }

        public bool HasPending => PendingTypeRaw != 0;
        public LevelElementType PendingType => (LevelElementType)PendingTypeRaw;

        private PlayerController _player;
        private PlacementGhost _ghost;
        private PlacementTarget _target;
        private StallManager Stall => StallManager.Instance;

        public override void Spawned()
        {
            _player = GetComponent<PlayerController>();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            DestroyHelpers();
        }

        private void DestroyHelpers()
        {
            if (_ghost != null) { Destroy(_ghost.gameObject); _ghost = null; }
            if (_target != null) { Destroy(_target.gameObject); _target = null; }
        }

        // ---------------- 放置流程 ----------------

        /// <summary>進入放置預覽。只在 StateAuthority 呼叫。</summary>
        public void BeginPlacement(LevelElementType type, float yaw = 0f)
        {
            if (!HasStateAuthority) return;
            PendingTypeRaw = (int)type;
            PendingYaw = yaw;
        }

        /// <summary>取消放置預覽。只在 StateAuthority 呼叫。</summary>
        public void CancelPlacement()
        {
            if (!HasStateAuthority) return;
            PendingTypeRaw = 0;
            PendingYaw = 0f;
        }

        /// <summary>
        /// 算出目前預覽的落點。**本機幽靈與狀態權威的實際放置都呼叫這一支**，
        /// 輸入是同一組（頭部位置、瞄準方向、襯布中心），所以兩邊結果一致。
        /// </summary>
        public bool TryResolvePoint(Vector3 origin, Vector3 direction,
                                    out Vector3 rawPoint, out Vector3 snapped, out PlacementResult result)
        {
            rawPoint = default;
            snapped = default;
            result = PlacementResult.NothingPending;

            var stall = Stall;
            if (stall == null || !HasPending) return false;
            if (!stall.IsArrangeMode) { result = PlacementResult.NotDeploying; return false; }

            if (!StallGeometry.ProjectAim(origin, direction, stall.MatCenter, out rawPoint))
            {
                result = PlacementResult.OutsideMat;
                return false;
            }

            result = stall.ValidatePlacement(rawPoint, null, out snapped);
            if (result != PlacementResult.Ok) snapped = rawPoint;
            return result == PlacementResult.Ok;
        }

        /// <summary>Space 被按下（由 PlacementTarget 導過來）。只在 StateAuthority 執行。</summary>
        public void ConfirmPlacement(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !HasPending) return;

            var stall = Stall;
            if (stall == null) return;

            TryResolvePoint(ctx.Origin, ctx.Direction, out _, out _, out var result);
            if (result != PlacementResult.Ok)
            {
                RPC_PlacementRejected(StallGeometry.Describe(result));
                GameAudio.PlayAt(SfxId.PlaceRejected, ctx.Origin);
                return;
            }

            StallGeometry.ProjectAim(ctx.Origin, ctx.Direction, stall.MatCenter, out var point);
            if (!stall.TryPlaceDevice(PendingType, point, PendingYaw, out var placeResult))
            {
                RPC_PlacementRejected(StallGeometry.Describe(placeResult));
                return;
            }

            CancelPlacement();
        }

        public string BuildPrompt(in InteractionContext ctx)
        {
            if (!HasPending) return null;

            TryResolvePoint(ctx.Origin, ctx.Direction, out _, out _, out var result);
            string name = StallCatalog.DisplayName(PendingType);

            return result == PlacementResult.Ok
                ? $"[Space] 放下{name}　[滾輪] 旋轉　[Q] 取消"
                : $"{StallGeometry.Describe(result)}　[滾輪] 旋轉　[Q] 取消";
        }

        // ---------------- RPC（用戶端 -> 狀態權威）----------------

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestPlacement(int typeRaw)
        {
            var stall = Stall;
            if (stall == null || !stall.IsArrangeMode) return;
            if (typeRaw == 0) return;
            BeginPlacement((LevelElementType)typeRaw, transform.eulerAngles.y);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RotatePending(int steps)
        {
            if (!HasPending) return;
            PendingYaw = Mathf.Repeat(PendingYaw + steps * GameTuning.StallRotationStep, 360f);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_CancelPending()
        {
            CancelPlacement();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
        private void RPC_PlacementRejected(string reason)
        {
            StallManager.LocalNotice(reason);
        }

        // ---------------- 本機輸入（滾輪旋轉 / Q 取消）----------------

        private void Update()
        {
            if (Object == null || !HasInputAuthority || !HasPending) return;

            // 有 UI 面板開著的時候不要吃輸入（LookEnabled 就是既有的那個開關）
            var input = LocalInputProvider.Instance;
            if (input != null && !input.LookEnabled) return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    RPC_RotatePending(scroll > 0f ? 1 : -1);
            }

            var kb = Keyboard.current;
            // Q：預覽中代表取消。這時候手上是空的，既有的丟／接邏輯本來就不會做事，不會打架。
            if (kb != null && kb.qKey.wasPressedThisFrame)
                RPC_CancelPending();
        }

        // ---------------- 表現層 ----------------

        public override void Render()
        {
            var stall = Stall;
            bool active = HasPending && stall != null && stall.IsArrangeMode;

            // 捕捉器：所有端都要建（狀態權威也需要它，才會在遠端玩家按 Space 時收到 Interact）
            if (active)
            {
                if (_target == null && _player != null) _target = PlacementTarget.Create(_player, this);
            }
            else if (_target != null)
            {
                Destroy(_target.gameObject);
                _target = null;
            }

            // 幽靈模型只有自己看得到，避免隊友畫面上出現一堆半透明方塊
            if (active && HasInputAuthority && _player != null)
            {
                _ghost ??= PlacementGhost.Create();

                var origin = _player.HeadAnchor.position;
                var dir = _player.AimDirection;
                bool ok = TryResolvePoint(origin, dir, out var raw, out var snapped, out _);
                _ghost.Apply(PendingType, ok ? snapped : raw, PendingYaw, ok);
            }
            else if (_ghost != null)
            {
                _ghost.Hide();
            }
        }
    }
}
