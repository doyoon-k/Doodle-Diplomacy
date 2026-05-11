using System;
using UnityEngine;

namespace DoodleDiplomacy.Core
{
    public sealed class RoundInputRouter
    {
        private readonly Func<GameState> _getCurrentState;
        private readonly Func<bool> _isSharedMonitorZoomActive;
        private readonly KeyCode _exitDrawingKey;
        private readonly KeyCode _exitInterpreterKey;
        private readonly KeyCode _exitMonitorZoomKey;
        private readonly Action _onExitSharedMonitorZoom;
        private readonly Action _onDrawingComplete;
        private readonly Action _onInterpreterClose;

        private bool _monitorClickConsumedUntilRelease;

        public RoundInputRouter(
            Func<GameState> getCurrentState,
            Func<bool> isSharedMonitorZoomActive,
            KeyCode exitDrawingKey,
            KeyCode exitInterpreterKey,
            KeyCode exitMonitorZoomKey,
            Action onExitSharedMonitorZoom,
            Action onDrawingComplete,
            Action onInterpreterClose)
        {
            _getCurrentState = getCurrentState;
            _isSharedMonitorZoomActive = isSharedMonitorZoomActive;
            _exitDrawingKey = exitDrawingKey;
            _exitInterpreterKey = exitInterpreterKey;
            _exitMonitorZoomKey = exitMonitorZoomKey;
            _onExitSharedMonitorZoom = onExitSharedMonitorZoom;
            _onDrawingComplete = onDrawingComplete;
            _onInterpreterClose = onInterpreterClose;
        }

        public void Tick()
        {
            RefreshMonitorClickLatch();

            if (_isSharedMonitorZoomActive != null
                && _isSharedMonitorZoomActive()
                && WasKeyPressed(_exitMonitorZoomKey))
            {
                _onExitSharedMonitorZoom?.Invoke();
                return;
            }

            GameState currentState = _getCurrentState != null
                ? _getCurrentState()
                : GameState.Title;

            if (currentState == GameState.Drawing && WasKeyPressed(_exitDrawingKey))
            {
                _onDrawingComplete?.Invoke();
            }

            if (currentState == GameState.Interpreter && WasKeyPressed(_exitInterpreterKey))
            {
                _onInterpreterClose?.Invoke();
            }
        }

        public bool TryConsumePrimaryClick()
        {
            if (_monitorClickConsumedUntilRelease)
            {
                return false;
            }

            _monitorClickConsumedUntilRelease = true;
            return true;
        }

        private void RefreshMonitorClickLatch()
        {
            if (!_monitorClickConsumedUntilRelease)
            {
                return;
            }

            if (!IsPrimaryPointerHeld())
            {
                _monitorClickConsumedUntilRelease = false;
            }
        }

        private static bool IsPrimaryPointerHeld()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                return mouse.leftButton.isPressed;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }

        private static bool WasKeyPressed(KeyCode keyCode)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            var keyControl = GetInputSystemKeyControl(keyboard, keyCode);
            return keyControl != null && keyControl.wasPressedThisFrame;
        }

        private static UnityEngine.InputSystem.Controls.KeyControl GetInputSystemKeyControl(
            UnityEngine.InputSystem.Keyboard keyboard,
            KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.Escape => keyboard.escapeKey,
                KeyCode.Space => keyboard.spaceKey,
                KeyCode.Return => keyboard.enterKey,
                KeyCode.KeypadEnter => keyboard.numpadEnterKey,
                KeyCode.Tab => keyboard.tabKey,
                KeyCode.Backspace => keyboard.backspaceKey,
                KeyCode.Delete => keyboard.deleteKey,
                KeyCode.UpArrow => keyboard.upArrowKey,
                KeyCode.DownArrow => keyboard.downArrowKey,
                KeyCode.LeftArrow => keyboard.leftArrowKey,
                KeyCode.RightArrow => keyboard.rightArrowKey,
                KeyCode.A => keyboard.aKey,
                KeyCode.B => keyboard.bKey,
                KeyCode.C => keyboard.cKey,
                KeyCode.D => keyboard.dKey,
                KeyCode.E => keyboard.eKey,
                KeyCode.F => keyboard.fKey,
                KeyCode.G => keyboard.gKey,
                KeyCode.H => keyboard.hKey,
                KeyCode.I => keyboard.iKey,
                KeyCode.J => keyboard.jKey,
                KeyCode.K => keyboard.kKey,
                KeyCode.L => keyboard.lKey,
                KeyCode.M => keyboard.mKey,
                KeyCode.N => keyboard.nKey,
                KeyCode.O => keyboard.oKey,
                KeyCode.P => keyboard.pKey,
                KeyCode.Q => keyboard.qKey,
                KeyCode.R => keyboard.rKey,
                KeyCode.S => keyboard.sKey,
                KeyCode.T => keyboard.tKey,
                KeyCode.U => keyboard.uKey,
                KeyCode.V => keyboard.vKey,
                KeyCode.W => keyboard.wKey,
                KeyCode.X => keyboard.xKey,
                KeyCode.Y => keyboard.yKey,
                KeyCode.Z => keyboard.zKey,
                KeyCode.Alpha0 => keyboard.digit0Key,
                KeyCode.Alpha1 => keyboard.digit1Key,
                KeyCode.Alpha2 => keyboard.digit2Key,
                KeyCode.Alpha3 => keyboard.digit3Key,
                KeyCode.Alpha4 => keyboard.digit4Key,
                KeyCode.Alpha5 => keyboard.digit5Key,
                KeyCode.Alpha6 => keyboard.digit6Key,
                KeyCode.Alpha7 => keyboard.digit7Key,
                KeyCode.Alpha8 => keyboard.digit8Key,
                KeyCode.Alpha9 => keyboard.digit9Key,
                _ => keyboard.escapeKey
            };
        }
    }
}
