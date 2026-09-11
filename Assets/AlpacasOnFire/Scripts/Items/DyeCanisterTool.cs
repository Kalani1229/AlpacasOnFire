using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Items
{
    /// <summary>
    /// 染劑罐 —— 取代原本的噴槍。
    ///
    /// 果汁機榨出來就是一罐顏料，拿在手上按住滑鼠右鍵、對著畫面中央的準心刷人偶身上的衣服，
    /// **刷到哪裡哪裡就變色**（見 GarmentPaintSurface），顏料會慢慢用完。
    /// 塗滿一定比例之後整件衣服就算是那個顏色。
    ///
    /// 沒有中間工具、沒有填裝步驟：果汁機的產出直接就是畫筆。
    /// </summary>
    public class DyeCanisterTool : CarriableItem, IHoldTool
    {
        [Header("Dye")]
        [SerializeField] private Renderer _levelIndicator;

        /// <summary>剩下多少顏料（0~PaintCapacity）。</summary>
        [Networked] public float Charge { get; set; }
        [Networked] public NetworkBool IsPainting { get; set; }

        private MaterialPropertyBlock _mpb;
        private float _sfxCooldown;

        public float Charge01 => GameTuning.PaintCapacity <= 0f
            ? 0f
            : Mathf.Clamp01(Charge / GameTuning.PaintCapacity);

        public DyeColorType Color => Spec.Color;
        public bool HasPaint => Charge > 0.01f;

        public override void Spawned()
        {
            base.Spawned();
            // 果汁機剛榨出來的是滿的
            if (HasStateAuthority && Charge <= 0f) Charge = GameTuning.PaintCapacity;
        }

        // ---------------- 按住右鍵塗抹 ----------------

        public void ToolTick(in InteractionContext ctx, bool held, float deltaTime)
        {
            if (!HasStateAuthority) return;

            if (!held || !HasPaint)
            {
                IsPainting = false;
                return;
            }

            IsPainting = true;

            // 就算沒刷到有效目標，顏料一樣會消耗
            Charge = Mathf.Max(0f, Charge - GameTuning.PaintDrainPerSecond * deltaTime);

            var surface = FindSurface(ctx, out var hitPoint);
            if (surface != null && surface.TryWorldToUv(hitPoint, out var uv))
            {
                surface.PaintAt(uv, Color, GameTuning.PaintBrushRadiusUv);

                // 塗到門檻就把整件衣服判定成這個顏色
                if (surface.Coverage01 >= GameTuning.PaintCoverageRequired)
                    surface.GetComponentInParent<Machines.Mannequin>()?.ApplyPaintedColor(Color);
            }

            if (Charge <= 0f)
            {
                // 用完就丟掉罐子，手會空出來
                IsPainting = false;
                ctx.Player.Carry.ConsumeHeld();
            }
        }

        /// <summary>
        /// 從準心射出去，找人偶身上的可塗抹表面。
        ///
        /// 兩個重點：
        ///  - 起點要補上攝影機的側向偏移。射線原本從角色頭部發出，但攝影機在側後方偏了
        ///    CameraSideOffset，兩條線平行卻錯開 —— 不補的話準心會跟實際塗到的位置對不上。
        ///    偏移量是常數，狀態權威端算得出來，所以不用同步。
        ///  - 只認有 GarmentPaintSurface 的命中點，其餘一律穿透 ——
        ///    所以羊駝自己的身體擋在中間也照樣塗得到。
        /// </summary>
        private GarmentPaintSurface FindSurface(in InteractionContext ctx, out Vector3 hitPoint)
        {
            hitPoint = default;

            var right = Vector3.Cross(Vector3.up, ctx.Direction).normalized;
            var origin = ctx.Origin + right * GameTuning.CameraSideOffset;

            var hits = Physics.RaycastAll(origin, ctx.Direction,
                                          GameTuning.PaintRange + GameTuning.CameraDistance,
                                          ~0, QueryTriggerInteraction.Collide);
            float bestDist = float.MaxValue;
            GarmentPaintSurface best = null;

            foreach (var h in hits)
            {
                var surface = h.collider.GetComponentInParent<GarmentPaintSurface>();
                if (surface == null || !surface.Active) continue;
                if (h.distance >= bestDist) continue;

                bestDist = h.distance;
                best = surface;
                hitPoint = h.point;
            }
            return best;
        }

        // ---------------- 外觀 ----------------

        public override void Render()
        {
            base.Render();

            if (IsPainting)
            {
                _sfxCooldown -= Time.deltaTime;
                if (_sfxCooldown <= 0f)
                {
                    _sfxCooldown = 0.2f;
                    GameAudio.PlayAt(SfxId.Spray, transform.position, 0.35f);
                }
            }

            if (_levelIndicator == null) return;

            _mpb ??= new MaterialPropertyBlock();
            var c = HasPaint ? PlaceholderPalette.Dye(Color) : new Color(0.35f, 0.35f, 0.35f);
            _levelIndicator.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _levelIndicator.SetPropertyBlock(_mpb);

            // 用剩餘量壓高度，一眼看得出還剩多少。
            // 這條是直的，所以是**從底部往上長**（水平的條子則是從左往右）——
            // 總之不能是從中心往兩邊撐開，那會看起來像浮在半空。
            _levelBar.Apply(_levelIndicator.transform, Charge01, min: 0.05f);
        }

        private readonly BarAnchor _levelBar = new(BarAnchor.Axis.Y);

        public override string DisplayName
            => $"{PlaceholderPalette.DyeName(Color)}顏料（{Charge01 * 100f:F0}%）";
    }
}
