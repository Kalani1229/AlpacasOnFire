using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlpacasOnFire.UI
{
    /// <summary>
    /// 遊戲內 UI 的根節點。放一個在場景裡（關卡建置工具會自動加），
    /// 它會建立 Canvas 並掛上 HUD / 暫停選單 / 結算畫面 / 選版型介面。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class GameUIRoot : MonoBehaviour
    {
        public static GameUIRoot Instance { get; private set; }

        public Canvas Canvas { get; private set; }
        public GameHud Hud { get; private set; }
        public PauseMenu Pause { get; private set; }
        public ResultsScreen Results { get; private set; }

        private void Awake()
        {
            Instance = this;

            Canvas = GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("[EventSystem]", typeof(EventSystem));
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            Hud = gameObject.AddComponent<GameHud>();
            Pause = gameObject.AddComponent<PauseMenu>();
            Results = gameObject.AddComponent<ResultsScreen>();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>沒有 GameUIRoot 的場景（例如測試關卡）自動補一個。</summary>
        public static GameUIRoot EnsureExists()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("[GameUI]", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            return go.AddComponent<GameUIRoot>();
        }
    }
}
