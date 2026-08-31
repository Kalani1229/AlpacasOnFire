using AlpacasOnFire.Networking;
using AlpacasOnFire.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace AlpacasOnFire.UI
{
    /// <summary>暫停選單。多人時不會真的暫停模擬（Fusion 是連線遊戲），只是打開選單並解鎖游標。</summary>
    public class PauseMenu : MonoBehaviour
    {
        public static PauseMenu Instance { get; private set; }

        private GameObject _root;
        public bool IsOpen => _root != null && _root.activeSelf;

        private void Awake()
        {
            Instance = this;
            Build();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Build()
        {
            var dim = UIFactory.Panel("PauseDim", transform, new Color(0f, 0f, 0f, 0.6f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _root = dim.gameObject;

            var panel = UIFactory.Panel("PausePanel", dim.transform, new Color(0.08f, 0.09f, 0.12f, 0.96f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(420f, 340f));

            UIFactory.Label("Title", panel.transform, "暫停", 40, TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(380f, 50f));

            UIFactory.TextButton("Resume", panel.transform, "繼續遊戲", new Vector2(0f, 40f), new Vector2(300f, 54f), Close);
            UIFactory.TextButton("Restart", panel.transform, "重新開始", new Vector2(0f, -26f), new Vector2(300f, 54f), Restart);
            UIFactory.TextButton("Quit", panel.transform, "離開", new Vector2(0f, -92f), new Vector2(300f, 54f), Quit);

            _root.SetActive(false);
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.escapeKey.wasPressedThisFrame) return;
            if (PatternSelectPanel.Instance != null && PatternSelectPanel.Instance.IsOpen) return;
            if (ResultsScreen.Instance != null && ResultsScreen.Instance.IsOpen) return;
            // 擺攤結算畫面也要讓 Esc 先關自己，不要疊一層暫停選單上去
            if (Stall.StallResultsPanel.Instance != null && Stall.StallResultsPanel.Instance.IsOpen) return;

            if (IsOpen) Close(); else Open();
        }

        public void Open()
        {
            _root.SetActive(true);
            LocalInputProvider.Instance?.SetCursorLocked(false);
        }

        public void Close()
        {
            _root.SetActive(false);
            LocalInputProvider.Instance?.SetCursorLocked(true);
        }

        private void Restart()
        {
            GameLauncher.Instance?.Shutdown();
            var scene = SceneManager.GetActiveScene();
            SceneManager.LoadScene(scene.buildIndex);
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
}
