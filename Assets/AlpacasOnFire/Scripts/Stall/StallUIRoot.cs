using AlpacasOnFire.UI;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.Stall
{
    /// <summary>
    /// 擺攤系統自己的 UI 根節點。
    ///
    /// 刻意不去修改既有的 GameUIRoot：它掛在場景的 [GameUI] 上，是 Phase 1 已驗證的東西。
    /// 這裡自己建一個 Canvas（排序在 HUD 之上），由 StallManager.Spawned() 呼叫 EnsureExists()，
    /// 所以任何放了 StallManager 的場景都會自動長出擺攤 UI，不必手動拉。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class StallUIRoot : MonoBehaviour
    {
        public static StallUIRoot Instance { get; private set; }

        public StallHud Hud { get; private set; }
        public StallResultsPanel Results { get; private set; }
        public StashHud Stash { get; private set; }
        public SuitcaseColorPanel ColorPanel { get; private set; }

        private void Awake()
        {
            Instance = this;

            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;   // 蓋在 GameHud 之上

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();

            // 裝備選單已經整個移除：開箱時所有裝備就在場上了，沒有東西要選。
            Hud = gameObject.AddComponent<StallHud>();
            Results = gameObject.AddComponent<StallResultsPanel>();

            // v6 共用背包的色塊列。沒有 TeamStash 的場景（例如 Stall_Test）它會自己隱藏，
            // 所以掛在這裡不會影響舊場景。
            Stash = gameObject.AddComponent<StashHud>();

            // v6 選色面板。沒有料倉的場景永遠不會打開它，掛著不影響舊場景。
            ColorPanel = gameObject.AddComponent<SuitcaseColorPanel>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static StallUIRoot EnsureExists()
        {
            if (Instance != null) return Instance;

            // GameUIRoot 負責 EventSystem 與 HUD；這裡只補自己的 Canvas
            GameUIRoot.EnsureExists();

            var go = new GameObject("[StallUI]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            return go.AddComponent<StallUIRoot>();
        }
    }
}
