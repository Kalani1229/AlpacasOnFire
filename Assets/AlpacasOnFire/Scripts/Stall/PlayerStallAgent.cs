using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Player;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 每個玩家的擺攤狀態：目前手上拿著哪一台裝備、預覽的朝向、以及它原本在哪一格。
    /// 掛在玩家 prefab 上（由 PlaceholderAssetBuilder 加上去），不修改 PlayerController。
    ///
    /// 分工：
    ///  - [Networked] 的待放置類型／朝向／原格：所有人一致，狀態權威據此執行放置
    ///  - 幽靈模型與滾輪／Q 的讀取：只有輸入權那一端做，走 RPC 回報
    ///  - 真正的目標格：在 StateAuthority 上用 InteractionContext 重算，不採信用戶端傳來的座標
    ///
    /// 所有座標都是整數格子，所以本機預覽算出來的格子與權威端算出來的一定是同一格。
    /// </summary>
    public class PlayerStallAgent : NetworkBehaviour
    {
        [Networked] public int PendingTypeRaw { get; set; }
        [Networked] public int PendingFacing { get; set; }

        /// <summary>拿起來之前它在哪一格。Q 取消時要放回這裡。</summary>
        [Networked] public NetworkBool HasOrigin { get; set; }
        [Networked] public int OriginCellX { get; set; }
        [Networked] public int OriginCellZ { get; set; }
        [Networked] public int OriginFacing { get; set; }

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

        /// <summary>
        /// 進入放置預覽。originCell 是它被拿起來之前的格子（Q 取消時放回去）。
        /// 只在 StateAuthority 呼叫。
        /// </summary>
        public void BeginPlacement(LevelElementType type, int facing, int originCellX, int originCellZ)
        {
            if (!HasStateAuthority) return;

            PendingTypeRaw = (int)type;
            PendingFacing = StallGrid.NormalizeFacing(facing);

            HasOrigin = true;
            OriginCellX = originCellX;
            OriginCellZ = originCellZ;
            OriginFacing = PendingFacing;
        }

        private void ClearPending()
        {
            if (!HasStateAuthority) return;
            PendingTypeRaw = 0;
            PendingFacing = 0;
            HasOrigin = false;
        }

        /// <summary>
        /// Q 取消：裝備回到原本的格子。
        /// 原格如果同時被別的裝備佔走了（多人同時搬），就找一個空格塞回去 ——
        /// 絕對不能讓裝備憑空消失。
        /// </summary>
        public void CancelPlacement()
        {
            if (!HasStateAuthority) return;
            if (!HasPending) return;

            var stall = Stall;
            var type = PendingType;

            if (stall != null && HasOrigin && stall.MatDeployed)
            {
                if (!stall.TryPlaceDevice(type, OriginCellX, OriginCellZ, OriginFacing, out _))
                {
                    StallGrid.RotatedFootprint(StallCatalog.Footprint(type), OriginFacing,
                                               out int w, out int d);
                    if (stall.BuildOccupancy().TryFindFree(w, d, out int cx, out int cz))
                    {
                        stall.TryPlaceDevice(type, cx, cz, OriginFacing, out _);
                        Debug.LogWarning($"[擺攤] 取消放置時原格 ({OriginCellX},{OriginCellZ}) 已被佔用，" +
                                         $"{type} 改放到 ({cx},{cz})。");
                    }
                    else
                    {
                        Debug.LogError($"[擺攤] 取消放置時找不到任何空格可以放回 {type}。");
                    }
                }
            }

            ClearPending();
        }

        /// <summary>
        /// 算出目前準心指向哪一格。**本機幽靈與狀態權威的實際放置都呼叫這一支**。
        /// 回傳的格子一律被夾在襯布範圍內（幽靈才有東西可顯示），
        /// 是否合法看 result。
        /// </summary>
        public bool TryResolveCell(Vector3 origin, Vector3 direction,
                                   out int cellX, out int cellZ, out PlacementResult result)
        {
            cellX = 0;
            cellZ = 0;
            result = PlacementResult.NothingPending;

            var stall = Stall;
            if (stall == null || !HasPending) return false;
            if (!stall.IsArrangeMode) { result = PlacementResult.NotDeploying; return false; }

            if (!StallGeometry.ProjectAim(origin, direction, stall.MatCenter, out var point))
            {
                result = PlacementResult.OutsideMat;
                return false;
            }

            bool inside = StallGrid.WorldToCell(point, stall.MatCenter, stall.MatYaw, out cellX, out cellZ);
            StallGrid.Clamp(ref cellX, ref cellZ);

            if (!inside)
            {
                result = PlacementResult.OutsideMat;
                return false;
            }

            result = stall.ValidateCell(PendingType, cellX, cellZ, PendingFacing);
            return result == PlacementResult.Ok;
        }

        /// <summary>Space 被按下（由 PlacementTarget 導過來）。只在 StateAuthority 執行。</summary>
        public void ConfirmPlacement(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !HasPending) return;

            var stall = Stall;
            if (stall == null) return;

            if (!TryResolveCell(ctx.Origin, ctx.Direction, out int cx, out int cz, out var result))
            {
                RPC_PlacementRejected(StallGeometry.Describe(result));
                GameAudio.PlayAt(SfxId.PlaceRejected, ctx.Origin);
                return;
            }

            if (!stall.TryPlaceDevice(PendingType, cx, cz, PendingFacing, out var placeResult))
            {
                RPC_PlacementRejected(StallGeometry.Describe(placeResult));
                return;
            }

            ClearPending();
        }

        public string BuildPrompt(in InteractionContext ctx)
        {
            if (!HasPending) return null;

            TryResolveCell(ctx.Origin, ctx.Direction, out _, out _, out var result);

            string name = StallCatalog.DisplayName(PendingType);
            string facing = $"朝{StallGrid.FacingName(PendingFacing)}（{StallCatalog.FacingMeaning(PendingType)}）";

            return result == PlacementResult.Ok
                ? $"[Space] 放下{name}　{facing}　[滾輪] 轉 90°　[Q] 取消"
                : $"{StallGeometry.Describe(result)}　[滾輪] 轉 90°　[Q] 取消";
        }

        // ---------------- RPC（用戶端 -> 狀態權威）----------------

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RotatePending(int steps)
        {
            if (!HasPending) return;
            PendingFacing = StallGrid.NormalizeFacing(PendingFacing + steps);
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
                bool ok = TryResolveCell(origin, dir, out int cx, out int cz, out _);

                StallGrid.RotatedFootprint(StallCatalog.Footprint(PendingType), PendingFacing,
                                           out int w, out int d);

                _ghost.Apply(PendingType,
                             StallGrid.CellToWorld(cx, cz, stall.MatCenter, stall.MatYaw),
                             StallGrid.FacingToRotation(PendingFacing, stall.MatYaw),
                             w, d, ok);
            }
            else if (_ghost != null)
            {
                _ghost.Hide();
            }
        }
    }
}
