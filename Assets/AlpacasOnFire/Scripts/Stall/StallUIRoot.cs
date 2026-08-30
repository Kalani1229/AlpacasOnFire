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

        public SuitcasePanel Suitcase { get; private set; }
        public StallHud Hud { get; private set; }
        public StallResultsPanel Results { get; private set; }

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

            Hud = gameObject.AddComponent<StallHud>();
            Results = gameObject.AddComponent<StallResultsPanel>();
            Suitcase = gameObject.AddComponent<SuitcasePanel>();
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
