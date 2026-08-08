using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Items
{
    /// <summary>
    /// 噴槍：按住 E 持續噴染劑。就算沒噴到有效目標也照樣消耗（規格書明訂）。
    /// 從果汁機產出的染劑罐填裝。
    /// </summary>
    public class SprayGunTool : CarriableItem, IItemUser, IHoldTool
    {
        [Networked] public float Charge { get; set; }          // 剩餘染劑量 0~SprayCapacity
        [Networked] public NetworkBool IsSpraying { get; set; }

        [SerializeField] private Renderer _tankIndicator;
        [SerializeField] private ParticleSystem _sprayFx;
        private MaterialPropertyBlock _mpb;
        private float _sfxCooldown;

        public float Charge01 => GameTuning.SprayCapacity <= 0f ? 0f : Mathf.Clamp01(Charge / GameTuning.SprayCapacity);
        public DyeColorType LoadedColor => Spec.Color;
        public bool HasDye => Charge > 0.01f && Spec.Color != DyeColorType.White;

        /// <summary>手上拿噴槍，對地上的染劑罐按 Space -> 填裝。</summary>
        public bool TryUseOnItem(CarriableItem target, in InteractionContext ctx, bool execute, out string prompt)
        {
            prompt = null;
            if (target == null || target.Kind != ItemKind.DyeCanister) return false;

            prompt = $"[Space] 填裝{PlaceholderPalette.DyeName(target.Spec.Color)}染劑";
            if (!execute) return true;

            var s = Spec;
            s.Color = target.Spec.Color;
            Spec = s;
            Charge = GameTuning.SprayCapacity;

            Runner.Despawn(target.Object);
            GameAudio.PlayAt(SfxId.MachineDone, transform.position);
            return true;
        }

        public void ToolTick(in InteractionContext ctx, bool held, float deltaTime)
        {
            if (!HasStateAuthority) return;

            if (!held || Charge <= 0f || Spec.Color == DyeColorType.White)
            {
                IsSpraying = false;
                return;
            }

            IsSpraying = true;
            Charge = Mathf.Max(0f, Charge - GameTuning.SprayDrainPerSecond * deltaTime);

            // 找前方的可穿衣對象（人偶或隊友），沒有的話染劑照樣噴掉
            var host = FindHost(ctx);
            host?.AddPaint(Spec.Color, GameTuning.SprayDrainPerSecond * deltaTime);
        }

        private IGarmentHost FindHost(in InteractionContext ctx)
        {
            var hits = Physics.SphereCastAll(ctx.Origin, 0.4f, ctx.Direction,
                                             GameTuning.SprayRange, ~0, QueryTriggerInteraction.Collide);
            IGarmentHost best = null;
            float bestDist = float.MaxValue;

            foreach (var h in hits)
            {
                var host = h.collider.GetComponentInParent<IGarmentHost>();
                if (host == null || !host.HasGarment) continue;

                var to = host.GarmentAnchor.position - ctx.Origin;
                if (Vector3.Dot(to.normalized, ctx.Direction) < GameTuning.SprayConeDot) continue;

                float d = to.sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = host; }
            }
            return best;
        }

        public override void Render()
        {
            base.Render();

            if (_sprayFx != null)
            {
                if (IsSpraying && !_sprayFx.isPlaying) _sprayFx.Play();
                else if (!IsSpraying && _sprayFx.isPlaying) _sprayFx.Stop();
            }

            if (IsSpraying)
            {
                _sfxCooldown -= Time.deltaTime;
                if (_sfxCooldown <= 0f)
                {
                    _sfxCooldown = 0.18f;
                    GameAudio.PlayAt(SfxId.Spray, transform.position, 0.4f);
                }
            }

            if (_tankIndicator == null) return;
            _mpb ??= new MaterialPropertyBlock();
            var c = Charge > 0f ? PlaceholderPalette.Dye(Spec.Color) : new Color(0.35f, 0.35f, 0.35f);
            _tankIndicator.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _tankIndicator.SetPropertyBlock(_mpb);
        }

        public override string DisplayName =>
            Charge > 0f ? $"噴槍（{PlaceholderPalette.DyeName(Spec.Color)} {Charge01 * 100f:F0}%）" : "噴槍（空）";
    }
}
