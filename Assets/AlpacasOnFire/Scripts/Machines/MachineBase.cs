using AlpacasOnFire.Core;
using AlpacasOnFire.Interaction;
using AlpacasOnFire.Items;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Machines
{
    /// <summary>
    /// 需要「處理一段時間」的機台共用基底（縫紉機、果汁機）。
    ///
    /// 產出的處理方式（本階段的設計決定）：
    ///  - 完成品**不會噴到地上**，會留在機台裡，玩家對機台按 Space 才拿得到
    ///    （原本噴出來的成品很難撿）
    ///  - 例外：如果機台**旁邊任一面**有一條朝外送的輸送帶，就直接把成品放到輸送帶靠機台那一端
    ///  - 成品沒被拿走之前**機台會卡住**，不能開始下一批
    ///
    /// 狀態用顏色表示：待命（綠）→ 處理中（黃）→ 完成待取（各機台自訂）。
    /// 除了狀態燈之外，機台本體也會被染上完成色，遠遠就看得出來有東西可以拿。
    /// </summary>
    public abstract class MachineBase : NetworkInteractable
    {
        [Header("Machine")]
        [SerializeField] protected Transform _outputAnchor;
        [SerializeField] protected Transform _progressBar;
        [SerializeField] protected Renderer _statusLight;
        [Tooltip("留空的話會自動找名為 Body 的子物件。完成待取時整台會被染色。")]
        [SerializeField] protected Renderer _bodyRenderer;

        [Networked] public NetworkBool Processing { get; set; }
        [Networked] public TickTimer ProcessTimer { get; set; }
        [Networked] public float ProcessDuration { get; set; }

        /// <summary>做好了但還沒被拿走。為 true 時機台不能開始下一批。</summary>
        [Networked] public NetworkBool HasOutput { get; set; }
        [Networked] public GarmentSpec OutputSpec { get; set; }

        private MaterialPropertyBlock _mpb;
        private Color _bodyBaseColor = Color.white;
        private bool _bodyBaseCached;

        private readonly BarAnchor _progressBar01 = new(BarAnchor.Axis.X);

        public Transform OutputAnchor => _outputAnchor != null ? _outputAnchor : transform;

        /// <summary>可不可以開始新的一批。成品沒拿走就不行。</summary>
        public bool CanStartJob => !Processing && !HasOutput;

        public float Progress01
        {
            get
            {
                if (!Processing || ProcessDuration <= 0f) return 0f;
                float remaining = ProcessTimer.RemainingTime(Runner) ?? 0f;
                return Mathf.Clamp01(1f - remaining / ProcessDuration);
            }
        }

        // ---------------- 子類別要提供的 ----------------

        /// <summary>這台機器產出什麼種類的物品。</summary>
        protected abstract ItemKind OutputItemKind { get; }

        /// <summary>做好的東西長什麼樣（版型／顏色／飾品）。</summary>
        protected abstract GarmentSpec BuildOutputSpec();

        /// <summary>提示字用的成品名稱。</summary>
        protected virtual string DescribeOutput() => OutputSpec.Describe();

        // ---------------- 生命週期 ----------------

        public override void Spawned()
        {
            CacheBodyRenderer();
        }

        private void CacheBodyRenderer()
        {
            if (_bodyRenderer == null)
            {
                var body = transform.Find("Body");
                if (body != null) _bodyRenderer = body.GetComponent<Renderer>();
            }

            if (_bodyRenderer != null && !_bodyBaseCached)
            {
                var mat = _bodyRenderer.sharedMaterial;
                if (mat != null)
                {
                    if (mat.HasProperty("_BaseColor")) _bodyBaseColor = mat.GetColor("_BaseColor");
                    else if (mat.HasProperty("_Color")) _bodyBaseColor = mat.GetColor("_Color");
                }
                _bodyBaseCached = true;
            }
        }

        // ---------------- 處理流程 ----------------

        protected void BeginProcess(float seconds)
        {
            if (!HasStateAuthority) return;
            Processing = true;
            ProcessDuration = seconds;
            ProcessTimer = TickTimer.CreateFromSeconds(Runner, seconds);
            GameAudio.PlayAt(SfxId.MachineStart, transform.position);
        }

        /// <summary>
        /// 換一個目標總時長，但**已經過的時間算數**。只在 StateAuthority 呼叫。
        ///
        /// 織布機用它做「中途加第二份毛」：目標從單色 4 秒改成雙色 7 秒，
        /// 第 3 秒放進去的話還要 4 秒，而不是重新跑 7 秒。
        ///
        /// 剩餘時間有下限（WeaveRetargetFloor）——
        /// 不然剛好在最後一瞬間塞進第二份毛會變成瞬間完成，
        /// 玩家看不到那件衣服是怎麼變成雙色的。
        ///
        /// 這是純新增的方法，沒有任何既有機台會呼叫它。
        /// </summary>
        protected void RetargetProcess(float newTotalSeconds)
        {
            if (!HasStateAuthority || !Processing) return;

            float elapsed = ProcessDuration - (ProcessTimer.RemainingTime(Runner) ?? 0f);
            float remaining = Mathf.Max(GameTuning.WeaveRetargetFloor, newTotalSeconds - elapsed);

            ProcessDuration = newTotalSeconds;
            ProcessTimer = TickTimer.CreateFromSeconds(Runner, remaining);
        }

        /// <summary>
        /// 這一批已經跑了多久（秒）。目前沒有人用 —— 進度條都走 Progress01（比例），
        /// 這支留給需要「絕對秒數」的顯示（例如提示字要寫還剩幾秒）。
        /// </summary>
        public float ElapsedSeconds =>
            Processing ? Mathf.Max(0f, ProcessDuration - (ProcessTimer.RemainingTime(Runner) ?? 0f)) : 0f;

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || !Processing) return;
            if (!ProcessTimer.Expired(Runner)) return;

            Processing = false;
            ProcessTimer = default;
            CompleteProcess();
            GameAudio.PlayAt(SfxId.MachineDone, transform.position);
        }

        private void CompleteProcess()
        {
            var spec = BuildOutputSpec();

            // 旁邊有朝外送的輸送帶 -> 直接放上去，不用人來拿
            if (TryEjectToConveyor(spec)) return;

            HasOutput = true;
            OutputSpec = spec;
        }

        /// <summary>
        /// 找相鄰、而且**方向是離開這台機器**的輸送帶。
        /// 找得到就把成品生在輸送帶靠機台的那一端。
        /// </summary>
        private bool TryEjectToConveyor(GarmentSpec spec)
        {
            var conveyor = FindOutgoingConveyor();
            if (conveyor == null) return false;

            var item = ItemFactory.Spawn(Runner, OutputItemKind, spec, conveyor.EntryPoint);
            return item != null;
        }

        private Conveyor FindOutgoingConveyor()
        {
            // 用「機台中心到輸送帶中心」判定，不看出料口在哪一面 ——
            // 規則是「機台**旁**擺了一條朝外送的輸送帶」，四個方向都算。
            // 相鄰格 1.5 公尺、斜角 2.12 公尺，門檻 1.9 剛好只認正交相鄰。
            float bestSqr = GameTuning.MachineConveyorLinkRadius * GameTuning.MachineConveyorLinkRadius;
            Conveyor best = null;

            for (int i = 0; i < Conveyor.All.Count; i++)
            {
                var c = Conveyor.All[i];
                if (c == null || c.Object == null) continue;

                var toConveyor = c.transform.position - transform.position;
                toConveyor.y = 0f;
                float sqr = toConveyor.sqrMagnitude;
                if (sqr < 0.0001f || sqr > bestSqr) continue;

                // 必須是「從機台往外送」，不然東西會被推回機台
                if (Vector3.Dot(c.Direction.normalized, toConveyor.normalized) < 0.5f) continue;

                bestSqr = sqr;
                best = c;
            }
            return best;
        }

        /// <summary>玩家把做好的東西拿走。只在 StateAuthority 呼叫。</summary>
        protected bool TryTakeOutput(in InteractionContext ctx)
        {
            if (!HasStateAuthority || !HasOutput) return false;
            if (!ctx.IsEmptyHanded) return false;

            var item = ItemFactory.SpawnIntoHands(Runner, OutputItemKind, OutputSpec, ctx.Player);
            if (item == null) return false;

            HasOutput = false;
            OutputSpec = default;
            GameAudio.PlayAt(SfxId.Pickup, transform.position);
            return true;
        }

        // ---------------- 外觀 ----------------

        public override void Render()
        {
            if (_progressBar != null)
            {
                bool show = Processing;
                if (_progressBar.gameObject.activeSelf != show) _progressBar.gameObject.SetActive(show);

                // 由左往右長，不是從中心往兩邊撐開（Cube 的軸心在中心，直接縮放會是後者）
                if (show) _progressBar01.Apply(_progressBar, Progress01);
            }

            _mpb ??= new MaterialPropertyBlock();

            if (_statusLight != null)
                Tint(_statusLight, StatusColor());

            // 完成待取時整台染色，遠遠就看得出來
            if (_bodyRenderer != null)
            {
                CacheBodyRenderer();
                var c = HasOutput ? Color.Lerp(_bodyBaseColor, DoneColor, 0.65f) : _bodyBaseColor;
                Tint(_bodyRenderer, c);
            }
        }

        private void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            r.SetPropertyBlock(_mpb);
        }

        protected virtual Color StatusColor()
        {
            if (HasOutput) return DoneColor;
            if (Processing) return BusyColor;
            return IdleColor;
        }

        protected virtual Color IdleColor => new Color(0.25f, 0.85f, 0.35f);
        protected virtual Color BusyColor => new Color(0.95f, 0.75f, 0.15f);

        /// <summary>「做好了、快來拿」的顏色。果汁機會改成染劑的顏色。</summary>
        protected virtual Color DoneColor => new Color(0.35f, 0.75f, 1f);
    }
}
