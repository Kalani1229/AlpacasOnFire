using System.Collections.Generic;
using AlpacasOnFire.Core;
using AlpacasOnFire.Machines;
using AlpacasOnFire.Player;
using UnityEngine;
using UnityEngine.UI;

namespace AlpacasOnFire.UI
{
    /// <summary>縫紉機的選版型介面。只在觸發互動的那個用戶端打開。</summary>
    public class PatternSelectPanel : MonoBehaviour
    {
        public static PatternSelectPanel Instance { get; private set; }

        private static readonly PatternType[] Patterns =
        {
            PatternType.TShirt, PatternType.Pants, PatternType.Hat,
        };

        private GameObject _root;
        private SewingMachine _machine;

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
            var panel = UIFactory.Panel("PatternSelect", transform, new Color(0.05f, 0.06f, 0.08f, 0.92f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(560f, 320f));
            _root = panel.gameObject;

            UIFactory.Label("Title", panel.transform, "選擇版型", 32, TextAnchor.UpperCenter, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -22f), new Vector2(500f, 42f));

            float x = -180f;
            foreach (var p in Patterns)
            {
                var pattern = p;
                UIFactory.TextButton($"Btn_{p}", panel.transform, PlaceholderPalette.PatternName(p),
                    new Vector2(x, 10f), new Vector2(150f, 120f), () => Choose(pattern));
                x += 180f;
            }

            UIFactory.TextButton("Btn_Cancel", panel.transform, "取消 (Esc)",
                new Vector2(0f, -110f), new Vector2(200f, 46f), Close);

            _root.SetActive(false);
        }

        public static void Open(SewingMachine machine)
        {
            var inst = Instance;
            if (inst == null)
            {
                GameUIRoot.EnsureExists();
                inst = Instance;
                if (inst == null) return;
            }
            inst._machine = machine;
            inst._root.SetActive(true);
            LocalInputProvider.Instance?.SetCursorLocked(false);
        }

        public void Close()
        {
            _root.SetActive(false);
            _machine = null;
            if (PauseMenu.Instance == null || !PauseMenu.Instance.IsOpen)
                LocalInputProvider.Instance?.SetCursorLocked(true);
        }

        public bool IsOpen => _root != null && _root.activeSelf;

        private void Choose(PatternType pattern)
        {
            if (_machine != null && _machine.Object != null)
                _machine.RPC_SelectPattern((int)pattern);
            Close();
        }

        private void Update()
        {
            if (!IsOpen) return;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Close();
        }
    }
}
