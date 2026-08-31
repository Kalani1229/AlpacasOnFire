using AlpacasOnFire.Core;
using AlpacasOnFire.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlpacasOnFire.Player
{
    /// <summary>
    /// 本機輸入的唯一來源。滑鼠水平位移直接累加到 Yaw —— 這個 Yaw 同時是
    /// 角色朝向與鏡頭朝向（共用朝向模型），所以鏡頭可以立刻反應，不必等網路回傳。
    /// </summary>
    public class LocalInputProvider : MonoBehaviour
    {
        public static LocalInputProvider Instance { get; private set; }

        public float Yaw { get; private set; }
        public float Pitch { get; private set; } = GameTuning.CameraDefaultPitch;
        public bool LookEnabled { get; set; } = true;

        private Vector2 _move;
        private NetworkButtonsLatch _latch;

        private void Awake()
        {
            Instance = this;
            SetCursorLocked(true);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetCursorLocked(bool locked)
        {
            LookEnabled = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            // ---- 視角（共用朝向）----
            if (LookEnabled && mouse != null)
            {
                var d = mouse.delta.ReadValue();
                Yaw += d.x * GameTuning.MouseSensitivityX;
                Pitch = Mathf.Clamp(Pitch - d.y * GameTuning.MouseSensitivityY,
                                    GameTuning.CameraPitchMin, GameTuning.CameraPitchMax);
                if (Yaw > 360f) Yaw -= 360f;
                if (Yaw < -360f) Yaw += 360f;
            }

            // ---- 移動 ----
            _move = Vector2.zero;
            if (kb != null && LookEnabled)
            {
                // WASD 與方向鍵都能移動
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    _move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  _move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) _move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  _move.x -= 1f;
                if (_move.sqrMagnitude > 1f) _move.Normalize();
            }

            // ---- 按鍵（用 latch 保證單幀點擊不會被 tick 漏掉）----
            //
            // 互動：Space 或滑鼠左鍵（兩個都留著，鍵盤黨與滑鼠黨都順手）
            // 持續使用工具：滑鼠右鍵按住（塗抹就是對著畫面中央的準心刷）
            if (LookEnabled)
            {
                bool interact = (kb != null && kb.spaceKey.isPressed)
                             || (mouse != null && mouse.leftButton.isPressed);
                bool throwCatch = kb != null && kb.qKey.isPressed;
                bool useTool = mouse != null && mouse.rightButton.isPressed;

                _latch.Hold(GameButton.Interact,   interact);
                _latch.Hold(GameButton.ThrowCatch, throwCatch);
                _latch.Hold(GameButton.UseTool,    useTool);

                if ((kb != null && kb.spaceKey.wasPressedThisFrame)
                 || (mouse != null && mouse.leftButton.wasPressedThisFrame))
                    _latch.Latch(GameButton.Interact);

                if (kb != null && kb.qKey.wasPressedThisFrame)
                    _latch.Latch(GameButton.ThrowCatch);

                if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                    _latch.Latch(GameButton.UseTool);
            }
            else
            {
                _latch.Clear();
            }
        }

        /// <summary>由 GameLauncher.OnInput 在每個 tick 呼叫。</summary>
        public NetInput Poll()
        {
            var input = new NetInput
            {
                Move = _move,
                Yaw = Yaw,
                Pitch = Pitch,
            };
            _latch.WriteTo(ref input);
            _latch.ConsumeLatches();
            return input;
        }

        /// <summary>把「按住」與「這一幀按下」兩種狀態合併成一次 tick 的按鍵位元。</summary>
        private struct NetworkButtonsLatch
        {
            private bool _holdInteract, _holdThrow, _holdTool;
            private bool _latchInteract, _latchThrow, _latchTool;

            public void Hold(GameButton b, bool value)
            {
                switch (b)
                {
                    case GameButton.Interact:   _holdInteract = value; break;
                    case GameButton.ThrowCatch: _holdThrow = value; break;
                    case GameButton.UseTool:    _holdTool = value; break;
                }
            }

            public void Latch(GameButton b)
            {
                switch (b)
                {
                    case GameButton.Interact:   _latchInteract = true; break;
                    case GameButton.ThrowCatch: _latchThrow = true; break;
                    case GameButton.UseTool:    _latchTool = true; break;
                }
            }

            public void WriteTo(ref NetInput input)
            {
                input.SetButton(GameButton.Interact,   _holdInteract || _latchInteract);
                input.SetButton(GameButton.ThrowCatch, _holdThrow || _latchThrow);
                input.SetButton(GameButton.UseTool,    _holdTool || _latchTool);
            }

            public void ConsumeLatches()
            {
                _latchInteract = false;
                _latchThrow = false;
                _latchTool = false;
            }

            public void Clear()
            {
                _holdInteract = _holdThrow = _holdTool = false;
                ConsumeLatches();
            }
        }
    }
}
