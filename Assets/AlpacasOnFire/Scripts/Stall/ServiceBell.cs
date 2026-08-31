using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 開張鈴。敲下去就開張，然後鈴鐺自己縮起來消失。
    ///
    /// 為什麼是世界物件而不是 HUD 按鈕：這個專案的游標從頭到尾是鎖住的
    /// （`LocalInputProvider` 把 `LookEnabled` 直接綁在游標鎖上），
    /// 常駐的 HUD 按鈕根本點不到 —— 所有可點的 UI 都必須是會解鎖游標的模態面板。
    /// 做成走過去按 Space 的世界物件，就完全不需要游標，也跟其他互動一致。
    ///
    /// 為什麼不進網格：它是「控制」不是「裝備」。固定在襯布背緣才好找、
    /// 也不會被玩家在佈置時不小心搬走。所以它沒有 DeployableDevice，
    /// 不佔格子、不進佈局記憶。
    ///
    /// 生命週期由 StallManager 管：進佈置模式就生一個、開張或收攤就收掉。
    /// 一場營業結束回到佈置模式時會再出現，讓你可以再開一場。
    /// </summary>
    public class ServiceBell : NetworkInteractable
    {
        [Header("Service Bell")]
        [SerializeField] private Transform _dome;      // 鈴身，敲下去會壓一下
        [SerializeField] private Renderer _domeRenderer;

        /// <summary>已經被敲過了，正在縮起來。</summary>
        [Networked] public NetworkBool Rung { get; set; }
        [Networked] private TickTimer ShrinkTimer { get; set; }

        private MaterialPropertyBlock _mpb;
        private float _shrinkElapsed = -1f;
        private Vector3 _baseScale = Vector3.one;

        /// <summary>比隊友（2）高、比手提箱（5）低。它旁邊就是手提箱，要分得開。</summary>
        public override int InteractionPriority => 3;

        private StallManager Stall => StallManager.Instance;

        public override void Spawned()
        {
            _baseScale = transform.localScale;
            _shrinkElapsed = -1f;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !Rung) return;
            if (!ShrinkTimer.Expired(Runner)) return;

            // 縮完了就真的消失
            Runner.Despawn(Object);
        }

        // ---------------- 互動 ----------------

        /// <summary>本機是不是房主。開張是房主限定（沿用既有規則）。</summary>
        private bool LocalIsHost => Runner != null && Runner.IsServer;

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (Rung) return false;
            var stall = Stall;
            if (stall == null || !stall.IsArrangeMode) return false;

            // 非房主也讓它回 true，這樣提示字才會出現（告訴他在等房主）
            return true;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (Rung) return null;

            var stall = Stall;
            if (stall == null) return null;

            if (!stall.CanOpenForBusiness(out string reason))
                return $"還不能開張：{reason}";

            return LocalIsHost ? "[Space] 敲鈴開張" : "等房主敲鈴開張";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || Rung) return;

            var stall = Stall;
            if (stall == null) return;

            // 房主判定：Interact 只在狀態權威上跑，而狀態權威所在的機器就是房主，
            // 所以「這個玩家的輸入權 == 本機玩家」等於「敲鈴的是房主」。
            if (ctx.Player.Object.InputAuthority != Runner.LocalPlayer)
            {
                RPC_RejectedForPlayer(ctx.Player.Object.InputAuthority, "只有房主可以敲鈴開張");
                return;
            }

            if (!stall.CanOpenForBusiness(out string reason))
            {
                RPC_RejectedForPlayer(ctx.Player.Object.InputAuthority, $"還不能開張：{reason}");
                GameAudio.PlayAt(SfxId.PlaceRejected, transform.position);
                return;
            }

            Rung = true;
            ShrinkTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.StallBellShrinkDuration);
            RPC_Rang();

            stall.OpenForBusiness();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Rang()
        {
            GameAudio.PlayAt(SfxId.BellRing, transform.position);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_RejectedForPlayer([RpcTarget] PlayerRef player, string message)
        {
            StallManager.LocalNotice(message);
        }

        // ---------------- 表現 ----------------

        public override void Render()
        {
            if (Rung)
            {
                if (_shrinkElapsed < 0f) _shrinkElapsed = 0f;
                _shrinkElapsed += Time.deltaTime;

                float t = Mathf.Clamp01(_shrinkElapsed / GameTuning.StallBellShrinkDuration);

                // 先被敲扁一下，再整個縮掉 —— 敲擊的手感就靠這一下
                float squash = t < 0.25f ? Mathf.Lerp(1f, 0.7f, t / 0.25f) : 1f;
                float shrink = t < 0.25f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.25f) / 0.75f);

                transform.localScale = new Vector3(
                    _baseScale.x * shrink * (2f - squash),
                    _baseScale.y * shrink * squash,
                    _baseScale.z * shrink * (2f - squash));
                return;
            }

            // 還沒敲：條件不滿足時鈴身轉灰，一眼看得出還不能開張
            if (_domeRenderer == null) return;

            var stall = Stall;
            bool ready = stall != null && stall.CanOpenForBusiness(out _);
            var c = ready ? new Color(0.98f, 0.82f, 0.30f) : new Color(0.42f, 0.42f, 0.45f);

            _mpb ??= new MaterialPropertyBlock();
            _domeRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _domeRenderer.SetPropertyBlock(_mpb);

            // 可以開張時上下輕輕浮動，把玩家的注意力帶過去
            if (_dome != null)
            {
                float bob = ready ? Mathf.Sin(Time.time * 3.2f) * 0.03f : 0f;
                _dome.localPosition = new Vector3(0f, GameTuning.StallBellHeight + 0.12f + bob, 0f);
            }
        }
    }
}
