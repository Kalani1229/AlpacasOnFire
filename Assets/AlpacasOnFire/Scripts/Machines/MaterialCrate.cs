using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 素材箱：攤位上的羊毛來源。
    ///
    /// **不是從手提箱選單擺出來的。** 你在手提箱的選色面板選了哪幾種顏色，
    /// 場上就直接冒出那幾個顏色的箱子（`StallManager.SyncCrates()` 負責）。
    /// 選了紅就有紅箱子，取消紅就消失，不用再多一道「擺放素材箱」的步驟。
    ///
    /// 它是 DeployableDevice，所以佔一格、能被拿起來重擺、位置與顏色都會被
    /// 佈局記住（顏色存在 StallSlotRecord.Variant）。這一點很重要 ——
    /// 把箱子擺在織布機旁邊可以省下每趟的來回，動線優化才有意義。
    ///
    /// 互動分工 —— 兩個模式共用左鍵／Space，靠模式本身錯開，不會打架：
    ///   佈置模式 -> 拿起來重擺（DeployableDevice 優先權 4 壓過這裡的 1；
    ///               這時 IsBusiness 為 false，本類別的 CanInteract 直接回 false）
    ///   營業模式 -> 拿一份毛（DeployableDevice 的 CanInteract 看到
    ///               !IsArrangeMode 就讓開，只剩本類別接得住）
    ///
    /// 拿毛原本綁在右鍵。移到左鍵是因為「拿一份毛」是即時動作，
    /// 而右鍵現在只留給設定類動作（手提箱選材料、放置模式取消）。
    /// </summary>
    public class MaterialCrate : NetworkInteractable
    {
        /// <summary>場上所有的素材箱。顧客抽需求、開張裝料、收攤退料都要走訪它。</summary>
        public static readonly List<MaterialCrate> All = new();

        [Header("Material Crate")]
        [Tooltip("箱體本身，會被染成裝載的顏色。")]
        [SerializeField] private Renderer _bodyRenderer;
        [Tooltip("剩餘量條，沿 Y 縮放。")]
        [SerializeField] private Transform _fillBar;
        [SerializeField] private Transform _woolAnchor;

        /// <summary>裝哪一種顏色的毛。由 StallManager 在生成時指定。</summary>
        [Networked] public int ColorRaw { get; set; }

        /// <summary>還剩幾份。開張時從背包裝滿，營業中拿一份少一份。</summary>
        [Networked] public int Remaining { get; set; }

        /// <summary>已經裝載並鎖定（開張之後為 true）。</summary>
        [Networked] public NetworkBool Loaded { get; set; }

        private MaterialPropertyBlock _mpb;
        private int _renderedColor = -1;
        private int _renderedRemaining = -1;

        private readonly BarAnchor _fillBarAnchor = new(BarAnchor.Axis.Y);

        public DyeColorType Color => (DyeColorType)ColorRaw;
        public bool HasStock => Remaining > 0;
        public Transform WoolAnchor => _woolAnchor != null ? _woolAnchor : transform;

        public override int InteractionPriority => 1;

        private static bool IsBusiness =>
            StallManager.Instance != null && StallManager.Instance.IsBusinessMode;

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
            ApplyVisual(true);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        /// <summary>由 StallManager 在 Runner.Spawn 的初始化回呼裡設定。</summary>
        public void SetColor(DyeColorType color)
        {
            ColorRaw = (int)color;
        }

        /// <summary>開張：把背包裡這個顏色的**全部存量**搬進來。只在 StateAuthority。</summary>
        public int LoadFromStash()
        {
            if (!HasStateAuthority || Loaded) return 0;

            var stash = TeamStash.Instance;
            int taken = stash != null ? stash.TakeUpTo(Color, int.MaxValue) : 0;

            Remaining = taken;
            Loaded = true;
            return taken;
        }

        /// <summary>收攤／取消這個顏色：剩下的退回背包。只在 StateAuthority。</summary>
        public int UnloadToStash()
        {
            if (!HasStateAuthority || Remaining <= 0) return 0;

            var stash = TeamStash.Instance;
            int returned = Remaining;
            if (stash != null) stash.TryAdd(Color, returned);

            Remaining = 0;
            Loaded = false;
            return returned;
        }

        // ---------------- 左鍵／Space：拿一份毛 ----------------

        /// <summary>
        /// **不要求空手。** 手上有東西的話，Interact 會先把它放到腳邊再拿毛
        /// —— 拿一份毛不該逼玩家先找地方放東西。
        /// </summary>
        public override bool CanInteract(in InteractionContext ctx)
        {
            if (!IsBusiness || Remaining <= 0) return false;
            return ctx.Player != null;
        }

        /// <summary>
        /// CanInteract 回 false 的時候也要給提示 —— 玩家對著箱子按左鍵卻什麼都沒發生時，
        /// 要看得出是「還沒開張」「空了」還是「手上有東西」，不然會以為壞掉了。
        ///
        /// 這些提示同時擋掉了誤丟：面前有這個箱子（即使不能用），
        /// PlayerController 的左鍵第 3 條就不成立，手上的毛不會被扔出去。
        /// </summary>
        public override string GetPrompt(in InteractionContext ctx)
        {
            string colour = PlaceholderPalette.DyeName(Color);

            if (!IsBusiness)
                return Loaded ? $"{colour}毛箱（剩 {Remaining}）" : $"{colour}毛箱（開張時才裝料）";

            if (Remaining <= 0) return $"{colour}毛箱：空了";

            // 手上有東西時要**先講明會放下什麼**，不然玩家會覺得東西莫名其妙掉了
            if (!ctx.IsEmptyHanded)
                return $"[左鍵] 拿{colour}毛（先放下 {ctx.Held.DisplayName}，剩 {Remaining}）";

            return $"[左鍵] 拿{colour}毛（剩 {Remaining}）";
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !CanInteract(in ctx)) return;

            // 手上有東西就先騰出手。跟主動接住同一套手感：要拿的東西一定拿得到。
            ctx.Player.Carry.MakeRoomForPickup();

            var spec = GarmentSpec.Create(PatternType.None, Color);
            var wool = ItemFactory.SpawnIntoHands(Runner, ItemKind.Wool, spec, ctx.Player);
            if (wool == null) return;

            Remaining--;
            GameAudio.PlayAt(SfxId.Pickup, transform.position);
        }

        // ---------------- 外觀 ----------------

        public override void Render()
        {
            ApplyVisual(false);
        }

        private void ApplyVisual(bool force)
        {
            if (!force && _renderedColor == ColorRaw && _renderedRemaining == Remaining) return;
            _renderedColor = ColorRaw;
            _renderedRemaining = Remaining;

            _mpb ??= new MaterialPropertyBlock();

            if (_bodyRenderer != null)
            {
                // 裝過料而且空了 -> 轉灰；其餘顯示自己的顏色
                var c = PlaceholderPalette.Dye(Color);
                if (Loaded && Remaining <= 0) c *= 0.35f;

                _bodyRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _mpb.SetColor("_Color", c);
                _bodyRenderer.SetPropertyBlock(_mpb);
            }

            if (_fillBar != null)
            {
                bool show = Loaded && Remaining > 0;
                if (_fillBar.gameObject.activeSelf != show) _fillBar.gameObject.SetActive(show);

                if (show)
                {
                    // 剩餘量條：用一個參考上限換算高度，滿了就是 prefab 上的原始高度。
                    // 從**底部**長上來，不是從中間往兩邊撐開。
                    float t = Mathf.Clamp01(Remaining / (float)GameTuning.CrateFillBarReference);
                    _fillBarAnchor.Apply(_fillBar, t);
                }
            }
        }
    }
}
