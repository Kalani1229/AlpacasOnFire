using AlpacasOnFire.Core;
using AlpacasOnFire.Orders;
using AlpacasOnFire.Stall;
using Fusion;
using UnityEngine;

namespace AlpacasOnFire.Npc
{
    /// <summary>
    /// 顧客。**掛在 WoolNpc 的同一個 prefab 上** —— 顧客就是場上那群羊被徵召來的，
    /// 不是另外一種生物（設計文件明講「同一群」）。
    ///
    /// `WoolNpc.IsCustomer` 為 true 時這支接管移動，WoolNpc 那邊會讓開。
    /// 走到交貨窗口旁邊排隊，頭上浮一個看板顯示想要什麼，耐心倒數，
    /// 逾時就轉身走掉並扣款。
    /// </summary>
    [RequireComponent(typeof(WoolNpc))]
    [RequireComponent(typeof(NetworkCharacterController))]
    public class Customer : NetworkBehaviour
    {
        private enum Phase : byte { Walking = 0, Waiting = 1, Leaving = 2 }

        [Header("Customer")]
        [Tooltip("頭上的看板根節點。沒有需求時整個關掉。")]
        [SerializeField] private Transform _signRoot;
        [Tooltip("看板上的兩格色塊：第 0 格主色、第 1 格點綴色。")]
        [SerializeField] private Renderer[] _signSwatches;
        [Tooltip("耐心條，沿 X 縮放。")]
        [SerializeField] private Transform _patienceBar;

        [Networked] public NetworkBool Active { get; set; }
        [Networked] public GarmentSpec Wanted { get; set; }
        [Networked] public int Price { get; set; }
        [Networked] public int PhaseRaw { get; set; }
        [Networked] public Vector3 QueueSpot { get; set; }
        [Networked] public Vector3 ExitPoint { get; set; }
        [Networked] private TickTimer PatienceTimer { get; set; }
        [Networked] public float PatienceDuration { get; set; }

        private WoolNpc _npc;
        private NetworkCharacterController _ncc;
        private MaterialPropertyBlock _mpb;

        private Phase CurrentPhase => (Phase)PhaseRaw;

        public float Patience01
        {
            get
            {
                if (PatienceDuration <= 0f) return 0f;
                float remaining = PatienceTimer.RemainingTime(Runner) ?? 0f;
                return Mathf.Clamp01(remaining / PatienceDuration);
            }
        }

        public override void Spawned()
        {
            _npc = GetComponent<WoolNpc>();
            _ncc = GetComponent<NetworkCharacterController>();
        }

        // ---------------- 徵召 / 離開 ----------------

        /// <summary>由 CustomerQueue 呼叫。只在 StateAuthority。</summary>
        public void Recruit(GarmentSpec wanted, int price, Vector3 queueSpot, Vector3 exitPoint)
        {
            if (!HasStateAuthority) return;

            Active = true;
            Wanted = wanted;
            Price = price;
            QueueSpot = queueSpot;
            ExitPoint = exitPoint;
            PhaseRaw = (int)Phase.Walking;

            PatienceDuration = GameTuning.CustomerPatienceSeconds;
            PatienceTimer = TickTimer.CreateFromSeconds(Runner, PatienceDuration);

            if (_npc != null) _npc.IsCustomer = true;
        }

        /// <summary>成交或放棄之後轉身走人。只在 StateAuthority。</summary>
        public void Leave(bool served)
        {
            if (!HasStateAuthority || !Active) return;

            PhaseRaw = (int)Phase.Leaving;
            PatienceTimer = default;

            if (!served)
            {
                LevelDirector.Instance?.AddMoney(-GameTuning.CustomerLeavePenalty, "顧客等太久");
                GameAudio.PlayAt(SfxId.OrderTimeout, transform.position);
            }
        }

        /// <summary>走遠之後恢復成一般的羊。</summary>
        private void Release()
        {
            if (!HasStateAuthority) return;

            Active = false;
            Wanted = default;
            Price = 0;
            PhaseRaw = (int)Phase.Walking;
            if (_npc != null) _npc.IsCustomer = false;
        }

        // ---------------- 模擬 ----------------

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority) return;

            if (!Active)
            {
                // 不是顧客的時候完全不碰 NCC，交給 WoolNpc 的閒晃邏輯
                return;
            }

            _ncc.maxSpeed = GameTuning.CustomerWalkSpeed;

            switch (CurrentPhase)
            {
                case Phase.Walking: TickWalking(); break;
                case Phase.Waiting: TickWaiting(); break;
                default:            TickLeaving(); break;
            }
        }

        private void TickWalking()
        {
            var to = QueueSpot - transform.position;
            to.y = 0f;

            if (to.magnitude <= GameTuning.NpcArriveThreshold)
            {
                PhaseRaw = (int)Phase.Waiting;
                _ncc.Move(Vector3.zero);
                return;
            }
            _ncc.Move(to.normalized);
        }

        private void TickWaiting()
        {
            _ncc.Move(Vector3.zero);

            // 站著等的時候面向櫃台，看起來才像在等人服務
            if (PatienceTimer.Expired(Runner)) Leave(served: false);
        }

        private void TickLeaving()
        {
            var to = ExitPoint - transform.position;
            to.y = 0f;

            if (to.magnitude <= 1.5f)
            {
                Release();
                return;
            }
            _ncc.Move(to.normalized);
        }

        // ---------------- 表現 ----------------

        public override void Render()
        {
            bool show = Active && CurrentPhase != Phase.Leaving;

            if (_signRoot != null && _signRoot.gameObject.activeSelf != show)
                _signRoot.gameObject.SetActive(show);

            if (!show) return;

            // 看板永遠面向本機攝影機，不然從側面看不到想要什麼
            var cam = Camera.main;
            if (cam != null && _signRoot != null)
            {
                var dir = _signRoot.position - cam.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.001f)
                    _signRoot.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }

            _mpb ??= new MaterialPropertyBlock();

            if (_signSwatches != null)
            {
                var spec = Wanted;
                for (int i = 0; i < _signSwatches.Length; i++)
                {
                    var r = _signSwatches[i];
                    if (r == null) continue;

                    // 單色需求只亮第一格 —— 玩家一眼看得出要不要放第二份毛
                    bool visible = i == 0 || spec.HasAccent;
                    if (r.enabled != visible) r.enabled = visible;
                    if (!visible) continue;

                    var c = PlaceholderPalette.Dye(i == 0 ? spec.Color : spec.AccentColor);
                    r.GetPropertyBlock(_mpb);
                    _mpb.SetColor("_BaseColor", c);
                    _mpb.SetColor("_Color", c);
                    r.SetPropertyBlock(_mpb);
                }
            }

            // 倒數條從左端固定、右端往回退 —— 不是從兩端往中間收合
            if (_patienceBar != null)
                _patienceBarAnchor.Apply(_patienceBar, Patience01);
        }

        private readonly BarAnchor _patienceBarAnchor = new(BarAnchor.Axis.X);
    }
}
