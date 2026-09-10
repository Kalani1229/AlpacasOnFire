using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Player;
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
    /// **手提箱同時是佈局的儲存處。** 收攤時把場上每一台裝備的格子座標與朝向寫回
    /// `Layout`，下次開箱就照這份資料把裝備彈回原位。玩家調好的動線跟著箱子走，
    /// 換地方擺攤不用重排 —— 這是這一版最重要的一件事。
    ///
    /// 兩種互動情境（都掛在同一個 IInteractable 上，不新增按鍵、不開選單）：
    ///  1. 拿在手上、面向前方地面按 Space -> 開箱（襯布展開 + 所有裝備一次彈出）
    ///  2. 已展開、對著箱子按 Space      -> 收攤（寫回佈局 + 收走所有裝備）
    ///
    /// 為什麼拿在手上還能被準心選到：CarriableItem 只會關掉「非 trigger」的碰撞體，
    /// 所以 prefab 上額外掛的 trigger 碰撞體在手上時仍然存在，
    /// 而 PlayerInteractor 的查詢是 QueryTriggerInteraction.Collide。
    /// </summary>
    public class SuitcaseItem : CarriableItem, ISecondaryInteractable
    {
        /// <summary>一場能帶幾種顏色。v6 的核心限制。</summary>
        public const int ColorSlots = 3;

        [Header("Suitcase")]
        [SerializeField] private Transform _lidVisual;
        [SerializeField] private Renderer _stateLight;
        [Tooltip("箱體上顯示三色選擇的小色塊。長度要是 3。")]
        [SerializeField] private Renderer[] _colorSlotVisuals;

        /// <summary>
        /// 記住的佈局：每台裝備的格子座標 + 朝向。
        /// 全部是整數，所以同步過去不會有誤差，兩端推算出來的世界座標一定一致。
        /// </summary>
        [Networked, Capacity(StallCatalog.MaxSlots)]
        public NetworkArray<StallSlotRecord> Layout { get; }

        /// <summary>佈局裡實際有幾筆。0 代表還沒用過，開箱時會套用預設佈局。</summary>
        [Networked] public int LayoutCount { get; set; }

        // ---------------- v6：料倉 ----------------

        /// <summary>
        /// 這一場帶哪三種顏色。**-1 代表空槽**（不能用 0，因為 DyeColorType.White 就是 0）。
        /// 收攤時刻意保留 —— 下次開箱不用重選，跟「記住佈局」同一個精神。
        /// </summary>
        [Networked, Capacity(ColorSlots)] public NetworkArray<int> SelectedColors { get; }

        /// <summary>已經開張、顏色鎖定（收攤時解開）。實際的羊毛存放在素材箱身上。</summary>
        [Networked] public NetworkBool Loaded { get; set; }

        private MaterialPropertyBlock _suitcaseMpb;

        /// <summary>
        /// 互動優先權的全域排序（數字大的贏）：
        ///   0 地面雜物　1 機台　2 隊友　4 DeployableDevice　5 手提箱　99 放置捕捉器
        /// 手提箱要最高（放置捕捉器除外），因為它是整個擺攤流程的入口。
        /// </summary>
        public override int InteractionPriority => 5;

        private StallManager Stall => StallManager.Instance;

        /// <summary>這個箱子目前是不是「已展開」的狀態。</summary>
        public bool IsDeployed => !IsHeld && Stall != null && Stall.MatDeployed;

        public override void Spawned()
        {
            base.Spawned();

            // NetworkArray<int> 預設是 0，而 DyeColorType.White 也是 0 ——
            // 不初始化成 -1 的話，一開場就會變成「三槽都選了白色」。
            // Spawned() 一個手提箱只跑一次，所以無條件清空是對的。
            if (!HasStateAuthority) return;
            for (int i = 0; i < ColorSlots; i++) SelectedColors.Set(i, -1);
        }

        // ---------------- v6：料倉 ----------------

        /// <summary>手提箱這一場要不要當料倉用（由 loadout 決定）。</summary>
        public bool IsStash => StallCatalog.Active.SuitcaseIsStash;

        public bool HasColor(int slot) => slot >= 0 && slot < ColorSlots && SelectedColors[slot] >= 0;
        public DyeColorType ColorAt(int slot) => (DyeColorType)Mathf.Max(0, SelectedColors[slot]);
        public int SelectedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < ColorSlots; i++) if (HasColor(i)) n++;
                return n;
            }
        }

        public bool IsSelected(DyeColorType color)
        {
            for (int i = 0; i < ColorSlots; i++)
                if (SelectedColors[i] == (int)color) return true;
            return false;
        }

        /// <summary>選中的三色目前在背包裡總共有多少（開張條件用）。</summary>
        public int SelectedStashTotal()
        {
            var stash = TeamStash.Instance;
            if (stash == null) return 0;

            int total = 0;
            for (int i = 0; i < ColorSlots; i++)
                if (HasColor(i)) total += stash.Count(ColorAt(i));
            return total;
        }

        /// <summary>
        /// 選／取消一個顏色。只在 StateAuthority 呼叫（走 RPC 進來）。
        /// 已鎖定（開張後）不能改。
        ///
        /// 改完之後 StallManager.SyncCrates() 會在下一個 tick 把場上的素材箱補上或收掉 ——
        /// 這裡不直接生箱子，讓「選色」與「箱子存在與否」的同步只有一個來源。
        /// </summary>
        public bool ToggleColor(DyeColorType color)
        {
            if (!HasStateAuthority || Loaded) return false;

            // 已選 -> 取消
            for (int i = 0; i < ColorSlots; i++)
            {
                if (SelectedColors[i] != (int)color) continue;
                SelectedColors.Set(i, -1);
                return true;
            }

            // 未選 -> 找一個空槽
            for (int i = 0; i < ColorSlots; i++)
            {
                if (SelectedColors[i] >= 0) continue;
                SelectedColors.Set(i, (int)color);
                return true;
            }

            return false;   // 三槽都滿了
        }

        /// <summary>開張：鎖定顏色選擇。實際裝料由各個素材箱自己做。</summary>
        public void Lock()
        {
            if (!HasStateAuthority) return;
            Loaded = true;
        }

        /// <summary>收攤：解鎖顏色選擇。**三色刻意不清空**，下次開箱直接沿用。</summary>
        public void Unlock()
        {
            if (!HasStateAuthority) return;
            Loaded = false;
        }

        // ---------------- 佈局存取 ----------------

        /// <summary>
        /// 讀出要用的佈局。沒存過就給預設佈局（一份跑得通白色 T-shirt 產線的排法）。
        /// 回傳實際筆數，內容寫進 buffer。
        /// </summary>
        public int ReadLayout(StallSlotRecord[] buffer)
        {
            if (buffer == null) return 0;

            if (LayoutCount <= 0)
            {
                int n = Mathf.Min(StallCatalog.DefaultLayout.Length, buffer.Length);
                for (int i = 0; i < n; i++) buffer[i] = StallCatalog.DefaultLayout[i];
                Debug.Log($"[擺攤] 手提箱還沒有存過佈局，套用預設佈局（{n} 台）。");
                return n;
            }

            int count = Mathf.Min(LayoutCount, buffer.Length);
            for (int i = 0; i < count; i++) buffer[i] = Layout[i];
            Debug.Log($"[擺攤] 手提箱讀出上次的佈局（{count} 台）。");
            return count;
        }

        /// <summary>收攤時把目前場上的佈局寫回箱子。只在 StateAuthority 呼叫。</summary>
        public void WriteLayout(StallSlotRecord[] records, int count)
        {
            if (!HasStateAuthority || records == null) return;

            count = Mathf.Clamp(count, 0, Mathf.Min(records.Length, StallCatalog.MaxSlots));
            for (int i = 0; i < count; i++) Layout.Set(i, records[i]);
            for (int i = count; i < StallCatalog.MaxSlots; i++) Layout.Set(i, default);

            LayoutCount = count;
            Debug.Log($"[擺攤] 佈局已寫回手提箱（{count} 台）。");
        }

        // ---------------- 互動 ----------------

        public override bool CanInteract(in InteractionContext ctx)
        {
            if (ctx.Player == null || Stall == null) return false;

            // 放置預覽進行中：Space 的意義是「放下裝備」，交給 PlacementTarget 處理
            var agent = ctx.Player.GetComponent<PlayerStallAgent>();
            if (agent != null && agent.HasPending) return false;

            // 情境 1：拿在自己手上 -> 開箱
            if (IsHeld)
            {
                if (ctx.Held != this) return false;          // 別人手上的箱子不能操作
                return !Stall.MatDeployed;                   // 已經有攤位就不能再開一個
            }

            // 情境 2：已展開的手提箱 -> Space 是收攤（營業中不行）
            if (IsDeployed) return !Stall.IsBusinessMode;

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
            {
                if (Stall.IsBusinessMode) return "營業中不能收攤";

                return IsStash
                    ? $"[Space] 收攤　[右鍵] 選材料（{SelectedCount}/{ColorSlots}）"
                    : "[Space] 收攤（記住目前的佈局）";
            }

            return base.GetPrompt(in ctx);
        }

        public override void Interact(in InteractionContext ctx)
        {
            if (!HasStateAuthority || Stall == null) return;

            // ---- 開箱：襯布展開 + 所有裝備一次彈出 ----
            if (IsHeld)
            {
                if (ctx.Held != this || Stall.MatDeployed) return;

                var player = ctx.Player;
                if (!Stall.TryDeployMat(player.transform.position, player.transform.eulerAngles.y, this))
                    return;

                // 箱子從手上落到襯布背緣外側（不佔用任何格子）
                player.Carry.ReleaseHeld();
                DetachToGround(StallGeometry.SuitcaseRestPosition(Stall.MatCenter, Stall.MatYaw));
                return;
            }

            // ---- 收攤 ----
            if (IsDeployed && !Stall.IsBusinessMode)
            {
                Stall.CollectStall(ctx.Player, this);
                return;
            }

            base.Interact(in ctx);
        }

        // ---------------- ISecondaryInteractable（右鍵）----------------

        /// <summary>
        /// 右鍵在兩種模式下意義不同：
        ///   佈置模式 -> 開選色面板（選這一場帶哪三種毛）
        ///   營業模式 -> 循環切換要拿哪一色
        /// 兩者都只有 v6 的料倉模式才有；Classic loadout 的手提箱右鍵沒作用。
        /// </summary>
        public bool CanSecondaryInteract(in InteractionContext ctx)
        {
            if (!IsStash || !IsDeployed || Stall == null) return false;
            if (!Stall.IsArrangeMode) return false;   // 營業中顏色已鎖定

            // 放置預覽進行中不要搶右鍵
            var agent = ctx.Player != null ? ctx.Player.GetComponent<PlayerStallAgent>() : null;
            if (agent != null && agent.HasPending) return false;

            return true;
        }

        public string GetSecondaryPrompt(in InteractionContext ctx)
        {
            if (!CanSecondaryInteract(in ctx)) return null;
            return $"[右鍵] 選材料（{SelectedCount}/{ColorSlots}）";
        }

        public void SecondaryInteract(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !CanSecondaryInteract(in ctx)) return;

            // 選色面板只在觸發互動的那個用戶端打開（跟縫紉機選版型同一套做法）
            if (ctx.Player.Object.HasInputAuthority)
                SuitcaseColorPanel.Open(this);
            else
                RPC_OpenColorPanel(ctx.Player.Object.InputAuthority);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OpenColorPanel([RpcTarget] PlayerRef player)
        {
            SuitcaseColorPanel.Open(this);
        }

        /// <summary>選色面板按下色塊之後回報給狀態權威。</summary>
        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        public void RPC_ToggleColor(int colorRaw)
        {
            if (Loaded) return;
            if (colorRaw < 0) return;

            var colour = (DyeColorType)colorRaw;

            // 背包裡沒有這個顏色就不給選 —— 選了也裝不到東西
            var stash = TeamStash.Instance;
            if (stash != null && !IsSelected(colour) && stash.Count(colour) <= 0) return;

            ToggleColor(colour);
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

            RenderColorSlots(open);
        }

        /// <summary>
        /// 箱體上的三個小色塊：選了什麼、還剩多少、目前要拿哪一格。
        /// 玩家不必開面板就看得出攤位的材料狀況。
        /// </summary>
        private void RenderColorSlots(bool open)
        {
            if (_colorSlotVisuals == null || _colorSlotVisuals.Length == 0) return;

            bool show = open && IsStash;
            _suitcaseMpb ??= new MaterialPropertyBlock();

            for (int i = 0; i < _colorSlotVisuals.Length; i++)
            {
                var r = _colorSlotVisuals[i];
                if (r == null) continue;

                bool visible = show && i < ColorSlots && HasColor(i);
                if (r.enabled != visible) r.enabled = visible;
                if (!visible) continue;

                // 只顯示「這一場選了哪三色」。實際剩餘量看場上的素材箱，
                // 兩邊都顯示反而讓玩家不知道該看哪一個。
                var c = PlaceholderPalette.Dye(ColorAt(i));

                r.GetPropertyBlock(_suitcaseMpb);
                _suitcaseMpb.SetColor("_BaseColor", c);
                _suitcaseMpb.SetColor("_Color", c);
                r.SetPropertyBlock(_suitcaseMpb);
            }
        }

        public override string DisplayName =>
            LayoutCount > 0 ? $"手提箱（記住 {LayoutCount} 台的排法）" : "手提箱";
    }
}
