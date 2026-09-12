using AlpacasOnFire.Networking;
using Fusion;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AlpacasOnFire.UI
{
    /// <summary>
    /// 標題／選單畫面。放在獨立的 Title 場景，或直接掛在關卡場景上（把 GameLauncher 的自動啟動關掉）。
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class TitleMenu : MonoBehaviour
    {
        [Tooltip("按下開始之後要載入的場景。目前指向 v6 的羊駝村。")]
        [SerializeField] private string _gameSceneName = "Village_Test";

        private void Awake()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("[EventSystem]", typeof(EventSystem));
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Build();
        }

        private void Build()
        {
            UIFactory.Panel("Bg", transform, new Color(0.10f, 0.12f, 0.16f, 1f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            UIFactory.Label("Title", transform, "羊駝很忙", 84, TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -140f), new Vector2(900f, 120f));

            UIFactory.Label("Sub", transform, "Phase 1 — 佔位美術版本", 26, TextAnchor.UpperCenter,
                new Color(0.75f, 0.8f, 0.88f),
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -238f), new Vector2(900f, 40f));

            UIFactory.TextButton("Single", transform, "單人（離線測試）", new Vector2(0f, 40f), new Vector2(400f, 62f),
                () => Launch(GameMode.Single));
            UIFactory.TextButton("Host", transform, "建立房間（Host）", new Vector2(0f, -40f), new Vector2(400f, 62f),
                () => Launch(GameMode.Host));
            UIFactory.TextButton("Join", transform, "加入房間（Client）", new Vector2(0f, -120f), new Vector2(400f, 62f),
                () => Launch(GameMode.Client));
            UIFactory.TextButton("Quit", transform, "離開", new Vector2(0f, -220f), new Vector2(400f, 54f), Quit);
        }

        private void Launch(GameMode mode)
        {
            // 場景沒有登錄在 Build Settings 的話 LoadScene 只會丟一句很難懂的錯，
            // 畫面卡在標題、玩家以為按鈕壞了。先擋下來給一句看得懂的話。
            if (SceneUtility.GetBuildIndexByScenePath(_gameSceneName) < 0
                && !Application.CanStreamedLevelBeLoaded(_gameSceneName))
            {
                Debug.LogError($"[羊駝很忙] 場景「{_gameSceneName}」不在 Build Settings 裡，無法開始。" +
                               "請先執行選單「羊駝很忙 / v6 羊駝村 / 1. 建置羊駝村測試場景」" +
                               "（它會順手把場景登錄進去）。");
                return;
            }

            PendingLaunch.Mode = mode;
            PendingLaunch.HasPending = true;
            SceneManager.LoadScene(_gameSceneName);
        }

        private void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    /// <summary>從標題畫面帶到關卡場景的啟動模式。</summary>
    public static class PendingLaunch
    {
        public static bool HasPending;
        public static GameMode Mode = GameMode.Single;

        public static GameMode Consume(GameMode fallback)
        {
            if (!HasPending) return fallback;
            HasPending = false;
            return Mode;
        }
    }
}
