using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 人偶：把衣服掛上去當塗抹對象；空手互動可以取回衣服。
    ///
    /// 上色是玩家拿著染劑罐**真的刷上去**，實際的塗抹狀態由同物件上的
    /// GarmentPaintSurface 管理，這裡只負責衣服本身（穿上／脫下／判定顏色）。
    ///
    /// 重要：塗到門檻之後**不會**把整件衣服刷成純色 —— 玩家畫的痕跡要留著。
    /// 改成用「邊框發光三秒」告訴玩家這件已經算是那個顏色了。
    /// 所以「布料底色」（沒被塗到的地方）和「這件被判定成什麼顏色」是兩件事：
    ///   FabricColorRaw = 掛上來時的顏色，畫布的底
    ///   Worn.Color     = 訂單比對用的顏色，塗到門檻就會變
    /// </summary>
    [RequireComponent(typeof(GarmentPaintSurface))]
    public class Mannequin : NetworkInteractable, IGarmentHost
    {
        [SerializeField] private Transform _garmentAnchor;
        [SerializeField] private Renderer _garmentRenderer;
        [SerializeField] private Transform _paintProgressBar;
        [Tooltip("達標時會發光的外框。比畫布大一圈、擺在畫布後面。")]
        [SerializeField] private Renderer _completionFrame;

        [Networked] public NetworkBool Wearing { get; set; }
        [Networked] public GarmentSpec Worn { get; set; }
        /// <summary>掛上來時的布料顏色 —— 沒被塗到的地方維持這個色。</summary>
        [Networked] public int FabricColorRaw { get; set; }
        /// <summary>達標提示的發光倒數。</summary>
        [Networked] public TickTimer GlowTimer { get; set; }
        /// <summary>發光要用的顏色（達標當下的顏料色）。</summary>
        [Networked] public int GlowColorRaw { get; set; }

        private GarmentPaintSurface _paint;
        private MaterialPropertyBlock _mpb;

        public override int InteractionPriority => 1;

        public GarmentPaintSurface PaintSurface
        {
            get
            {
                if (_paint == null) _paint = GetComponent<GarmentPaintSurface>();
                return _paint;
            }
        }

        public override void Spawned()
        {
            _paint = GetComponent<GarmentPaintSurface>();
            if (_completionFrame != null) _completionFrame.enabled = false;
        }

        // ---------------- IGarmentHost ----------------

        public Transform GarmentAnchor => _garmentAnchor != null ? _garmentAnchor : transform;
        public bool HasGarment => Wearing;
        public GarmentSpec Garment => Worn;

        public bool TryWear(GarmentSpec spec)
        {
            if (!HasStateAuthority || Wearing) return false;
            Wearing = true;
            Worn = spec;
            FabricColorRaw = spec.ColorRaw;   // 掛上來時是什麼色，畫布的底就是什麼色
            GlowTimer = default;
            PaintSurface?.ClearMask();
            return true;
        }

        public bool TryTakeOff(out GarmentSpec spec)
        {
            spec = Worn;
            if (!HasStateAuthority || !Wearing) return false;
            Wearing = false;
            Worn = default;
            GlowTimer = default;
            PaintSurface?.ClearMask();
            return true;
        }

        /// <summary>
        /// 塗抹覆蓋率到門檻了 —— 這件衣服從此被判定成這個顏色。
        /// **不會**清掉遮罩、也不會把整件刷成純色，玩家畫的痕跡要留著；
        /// 改成讓外框發光三秒當作「完成了」的回饋。只在 StateAuthority 呼叫。
        /// </summary>
        public void ApplyPaintedColor(DyeColorType color)
        {
            if (!HasStateAuthority || !Wearing) return;
            if (Worn.Color == color) return;

            var spec = Worn;

            // 單色的衣服被塗成別的顏色之後，點綴色要跟著走 ——
            // 不然主色變紅、點綴色停在原本的白，這件衣服會被當成「紅底白紋」，
            // 跟訂單要的純紅 T 恤比對不起來（v6 之前 accent 不存在，沒有這個問題）。
            bool wasSingleColour = !spec.HasAccent;
            spec.Color = color;
            if (wasSingleColour) spec.AccentColor = color;

            Worn = spec;

            GlowColorRaw = (int)color;
            GlowTimer = TickTimer.CreateFromSeconds(Runner, GameTuning.PaintCompleteGlowSeconds);
            GameAudio.PlayAt(SfxId.MachineDone, transform.position);
        }

        // ---------------- IInteractable ----------------

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (ctx.Held is IGarmentHostUser user)
                return user.TryUseOnHost(this, in ctx, false, out _);
            return ctx.IsEmptyHanded && Wearing;
        }

        public override string GetPrompt(in InteractionContext ctx)
        {
            if (ctx.Held is IGarmentHostUser user && user.TryUseOnHost(this, in ctx, false, out var prompt))
                return prompt;
            if (ctx.IsEmptyHanded && Wearing) return $"[Space] 取下 {Worn.Describe()}";
            if (ctx.Held is DyeCanisterTool) return "[右鍵按住] 把顏料刷上去";
            return "人偶（空）";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority) return;

            if (ctx.Held is IGarmentHostUser user && user.TryUseOnHost(this, in ctx, true, out _))
                return;

            if (ctx.IsEmptyHanded && Wearing && TryTakeOff(out var spec))
            {
                ItemFactory.SpawnIntoHands(Runner, ItemKind.Garment, spec, ctx.Player);
                GameAudio.PlayAt(SfxId.DressOff, transform.position);
            }
        }

        // ---------------- 外觀 ----------------

        public override void Render()
        {
            if (_garmentRenderer != null)
                _garmentRenderer.enabled = Wearing;

            // 畫布的底色是「掛上來時的顏色」，不是「現在被判定成的顏色」——
            // 這樣塗到門檻之後，沒刷到的地方還是白的，畫的痕跡看得出來
            var surface = PaintSurface;
            if (surface != null)
                surface.BaseColor = PlaceholderPalette.Dye((DyeColorType)FabricColorRaw);

            UpdateProgressBar(surface);
            UpdateCompletionGlow();
        }

        private void UpdateProgressBar(GarmentPaintSurface surface)
        {
            if (_paintProgressBar == null) return;

            float coverage = surface != null ? surface.Coverage01 : 0f;
            bool show = Wearing && coverage > 0.001f;
            if (_paintProgressBar.gameObject.activeSelf != show) _paintProgressBar.gameObject.SetActive(show);
            if (!show) return;

            float t = Mathf.Clamp01(coverage / GameTuning.PaintCoverageRequired);
            var s = _paintProgressBar.localScale;
            _paintProgressBar.localScale = new Vector3(Mathf.Max(0.02f, t), s.y, s.z);
        }

        /// <summary>達標時外框發光三秒，逐漸淡出。</summary>
        private void UpdateCompletionGlow()
        {
            if (_completionFrame == null) return;

            float remaining = GlowTimer.RemainingTime(Runner) ?? 0f;
            bool glowing = Wearing && remaining > 0f;

            if (_completionFrame.enabled != glowing) _completionFrame.enabled = glowing;
            if (!glowing) return;

            // 一邊閃一邊淡出，最後三秒結束時剛好消失
            float fade = Mathf.Clamp01(remaining / GameTuning.PaintCompleteGlowSeconds);
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(remaining * 6f));

            var c = PlaceholderPalette.Dye((DyeColorType)GlowColorRaw) * pulse;
            c.a = fade;

            _mpb ??= new MaterialPropertyBlock();
            _completionFrame.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _mpb.SetColor("_EmissionColor", c * 2f);
            _completionFrame.SetPropertyBlock(_mpb);
        }
    }
}
