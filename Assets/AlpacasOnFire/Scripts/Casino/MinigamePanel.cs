using System.Collections.Generic;
using AlpacasOnFire.Player;
using AlpacasOnFire.Stall;
using AlpacasOnFire.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.Casino
{
    /// <summary>
    /// 小遊戲面板的共用基底。**純本機 UI** —— 沒有任何網路狀態；
    /// 要改世界的事一律走機台／CasinoState 的 RPC，由主機決定。
    ///
    /// 照 SuitcaseColorPanel 已驗證的模式：只在觸發互動的那個 client 打開、
    /// 打開時解鎖游標（LocalInputProvider 會因此停止送移動與按鍵，等於擋掉輸入）、
    /// 關閉時鎖回去。
    ///
    /// 這裡統一處理的事：
    ///   - 版面：**不是全螢幕**。周圍把 3D 世界壓暗但看得見，面板約佔八成 ——
    ///     全螢幕會讓玩家「離開」世界，在合作遊戲裡等於消失
    ///   - Esc：交給子類別的 OnEscape()（預設就是關閉）；PauseMenu 靠 BlocksEscape 讓開
    ///   - 機台不見了、不能用了、自己不再是佔用者 -> 自動關閉
    ///   - HUD 資本額凍結：錢是在購買那一刻就入帳的，不凍結的話 HUD 會在刮開前就劇透
    ///   - 關閉時送 RPC_Leave 釋放佔用鎖
    /// </summary>
    public abstract class MinigamePanel : MonoBehaviour
    {
        // ---------------- 全域：給 PauseMenu 與 StallHud 用 ----------------

        private static readonly List<MinigamePanel> OpenPanels = new();
        private static int _lastCloseFrame = -1;

        /// <summary>關掉 domain reload 時靜態欄位會留到下一次 Play —— 每次進 Play 先清掉。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OpenPanels.Clear();
            _lastCloseFrame = -1;
            CapitalFrozen = false;
            FrozenCapital = 0;
            _canvas = null;
        }

        /// <summary>有任何小遊戲面板開著。</summary>
        public static bool AnyOpen => OpenPanels.Count > 0;

        /// <summary>
        /// PauseMenu 要不要讓開 Esc。**這一幀剛被 Esc 關掉的也算** ——
        /// 兩支 Update 的先後不固定，面板先關的話，PauseMenu 同一幀會看到「沒有面板開著」而打開自己。
        /// </summary>
        public static bool BlocksEscape => AnyOpen || _lastCloseFrame == Time.frameCount;

        /// <summary>HUD 的資本額要不要凍結、凍結在多少。只改顯示，不碰入帳。</summary>
        public static bool CapitalFrozen { get; private set; }
        public static int FrozenCapital { get; private set; }

        // ---------------- UI 根節點 ----------------

        private static Canvas _canvas;

        /// <summary>小遊戲共用的 Canvas，排在擺攤 UI（10）之上、結算之類的面板之下也無所謂 —— 面板開著時不會有別的。</summary>
        protected static Transform CanvasRoot
        {
            get
            {
                if (_canvas != null) return _canvas.transform;

                GameUIRoot.EnsureExists();   // EventSystem 在這裡
                var go = new GameObject("[CasinoUI]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                _canvas = go.GetComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 30;

                var scaler = go.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                return go.transform;
            }
        }

        /// <summary>找到（或建出）某一種面板的唯一實例。</summary>
        protected static T EnsurePanel<T>() where T : MinigamePanel
        {
            var root = CanvasRoot;
            var existing = root.GetComponent<T>();
            return existing != null ? existing : root.gameObject.AddComponent<T>();
        }

        // ---------------- 實例 ----------------

        private GameObject _dim;
        private float _openedAt;

        /// <summary>面板本體（約八成畫面）。子類別把內容建在這下面。</summary>
        protected RectTransform Frame { get; private set; }

        protected MinigameMachine Machine { get; private set; }

        public bool IsOpen => _dim != null && _dim.activeSelf;

        protected virtual void Awake()
        {
            // 壓暗：半透明黑，3D 世界看得見
            var dim = UIFactory.Panel("Dim", transform, new Color(0f, 0f, 0f, 0.5f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _dim = dim.gameObject;

            var frame = UIFactory.Panel("Frame", dim.transform, new Color(0.07f, 0.06f, 0.09f, 0.95f),
                new Vector2(0.1f, 0.1f), new Vector2(0.9f, 0.9f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            Frame = frame.rectTransform;

            Build(Frame);
            _dim.SetActive(false);
        }

        /// <summary>建出面板內容（只呼叫一次）。</summary>
        protected abstract void Build(RectTransform frame);

        /// <summary>打開時重設內容。</summary>
        protected abstract void OnOpened();

        /// <summary>關閉前的收尾（例如把沒刮的直接揭曉）。</summary>
        protected virtual void OnClosing() { }

        /// <summary>
        /// 按下 Esc。預設直接關閉；子類別可以先做別的事（例如刮刮樂先把剩下的揭曉）。
        /// </summary>
        protected virtual void OnEscape() => Close();

        /// <summary>本機打開面板。只會在佔用者自己的 client 上被呼叫（機台的 OpenPanelLocal）。</summary>
        protected void OpenFor(MinigameMachine machine)
        {
            Machine = machine;
            _openedAt = Time.unscaledTime;

            if (!IsOpen)
            {
                _dim.SetActive(true);
                if (!OpenPanels.Contains(this)) OpenPanels.Add(this);

                // 凍結 HUD 的資本額：記下打開前的數字
                var stall = StallManager.Instance;
                if (stall != null && stall.Object != null && stall.Object.IsValid)
                {
                    FrozenCapital = stall.RunMode ? stall.ProjectedCapital : stall.Capital;
                    CapitalFrozen = true;
                }
            }

            OnOpened();
            LocalInputProvider.Instance?.SetCursorLocked(false);
        }

        public void Close()
        {
            if (!IsOpen) return;

            OnClosing();

            _dim.SetActive(false);
            OpenPanels.Remove(this);
            _lastCloseFrame = Time.frameCount;
            if (OpenPanels.Count == 0) CapitalFrozen = false;   // 關閉時才讓 HUD 跳到真實值

            // 釋放佔用鎖（機台可能已經不在了，例如天亮被收掉）
            var m = Machine;
            if (m != null && m.Object != null && m.Object.IsValid) m.RPC_Leave();
            Machine = null;

            if (PauseMenu.Instance == null || !PauseMenu.Instance.IsOpen)
                LocalInputProvider.Instance?.SetCursorLocked(true);
        }

        protected virtual void Update()
        {
            if (!IsOpen) return;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                OnEscape();
                if (!IsOpen) return;
            }

            // 機台不見了／不能玩了 -> 關閉。
            // 「自己不是佔用者」給一秒寬限：打開面板的 RPC 可能比 Occupant 的同步先到。
            var m = Machine;
            bool machineGone = m == null || m.Object == null || !m.Object.IsValid;
            bool lostSeat = !machineGone && !m.LocalIsOccupant && Time.unscaledTime - _openedAt > 1f;
            if (machineGone || lostSeat) Close();
        }

        protected virtual void OnDestroy()
        {
            OpenPanels.Remove(this);
            if (OpenPanels.Count == 0) CapitalFrozen = false;
        }
    }
}
