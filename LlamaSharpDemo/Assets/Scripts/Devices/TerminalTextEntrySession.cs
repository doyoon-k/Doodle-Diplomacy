using System;
using UnityEngine;

namespace DoodleDiplomacy.Devices
{
    public sealed class TerminalTextEntrySession : IDisposable
    {
        private readonly TerminalDisplay _terminal;
        private Action<string> _onChanged;
        private Action<string> _onSubmitted;
        private Action _onCancelled;
        private string _queuedText = string.Empty;
        private int _lastTextInputFrame = -1;
        private int _characterLimit;
#if ENABLE_LEGACY_INPUT_MANAGER
        private bool _hasPreviousImeCompositionMode;
        private IMECompositionMode _previousImeCompositionMode;
#endif

#if ENABLE_INPUT_SYSTEM
        private UnityEngine.InputSystem.Keyboard _subscribedKeyboard;
        private string _inputSystemComposition = string.Empty;
#endif

        public TerminalTextEntrySession(TerminalDisplay terminal)
        {
            _terminal = terminal;
        }

        public bool IsActive { get; private set; }
        public string Value { get; private set; } = string.Empty;
        public string Composition { get; private set; } = string.Empty;
        public string RenderedValue => Value + Composition;

        public void Enable()
        {
            SubscribeTextInput();
        }

        public void Disable()
        {
            End();
            UnsubscribeTextInput();
        }

        public void Begin(
            string initialValue,
            int characterLimit,
            Action<string> onChanged,
            Action<string> onSubmitted,
            Action onCancelled)
        {
            IsActive = true;
            Value = initialValue ?? string.Empty;
            Composition = string.Empty;
            _characterLimit = Mathf.Max(0, characterLimit);
            _onChanged = onChanged;
            _onSubmitted = onSubmitted;
            _onCancelled = onCancelled;
            _queuedText = string.Empty;
            EnableImeComposition();
        }

        public void AttachTerminalInput(string prefix, bool visible = false)
        {
            if (!IsActive || _terminal == null)
            {
                return;
            }

            EnableImeComposition();
            _terminal.BeginTextInput(
                prefix,
                Value,
                _characterLimit,
                Submit,
                HandleTerminalValueChanged,
                visible);
        }

        public bool Tick()
        {
            if (!IsActive)
            {
                return false;
            }

            if (TerminalKeyboardInput.WasPressed(KeyCode.Escape))
            {
                _onCancelled?.Invoke();
                return true;
            }

            if (_terminal != null && _terminal.IsTextInputActive)
            {
                if (_terminal.IsTyping())
                {
                    return true;
                }

                SyncTerminalState();
                _terminal.FocusTextInput();
                return true;
            }

            if (_terminal != null && _terminal.IsTyping())
            {
                QueueLegacyTextInput();
                return true;
            }

            bool changed = false;
            bool consumedBackspace = false;
            string textInput = ConsumeTextInput();
            for (int i = 0; i < textInput.Length; i++)
            {
                char character = textInput[i];
                if (character == '\b')
                {
                    consumedBackspace = true;
                    changed |= RemoveLastCharacter();
                    continue;
                }

                if (character == '\n' ||
                    character == '\r' ||
                    character == '\t' ||
                    char.IsControl(character) ||
                    (_characterLimit > 0 && Value.Length >= _characterLimit))
                {
                    continue;
                }

                Value += character;
                changed = true;
            }

            if (!consumedBackspace && TerminalKeyboardInput.WasPressed(KeyCode.Backspace))
            {
                changed |= RemoveLastCharacter();
            }

            string composition = GetCurrentComposition();
            if (!string.Equals(Composition, composition, StringComparison.Ordinal))
            {
                Composition = composition;
                changed = true;
            }

            if (changed)
            {
                _onChanged?.Invoke(Value);
            }

            if (_terminal == null && TerminalKeyboardInput.WasSubmitPressedThisFrame())
            {
                Submit(Value);
            }

            return true;
        }

        public void End()
        {
            if (!IsActive)
            {
                _terminal?.HideTextInput();
                return;
            }

            IsActive = false;
            _queuedText = string.Empty;
            Composition = string.Empty;
            _onChanged = null;
            _onSubmitted = null;
            _onCancelled = null;
            RestoreImeComposition();
            _terminal?.HideTextInput();
        }

        public void Dispose()
        {
            Disable();
        }

        private void Submit(string value)
        {
            if (!IsActive)
            {
                return;
            }

            Value = value ?? string.Empty;
            Composition = string.Empty;
            _onSubmitted?.Invoke(Value);
        }

        private void HandleTerminalValueChanged(string value)
        {
            if (!IsActive)
            {
                return;
            }

            if (!SyncTerminalState())
            {
                Value = value ?? string.Empty;
                Composition = string.Empty;
                _onChanged?.Invoke(Value);
            }
        }

        private bool SyncTerminalState()
        {
            if (_terminal == null)
            {
                return false;
            }

            SplitCommittedAndDisplayValue(
                _terminal.TextInputValue,
                _terminal.TextInputDisplayValue,
                out string committed,
                out string composition);
            bool changed =
                !string.Equals(Value, committed, StringComparison.Ordinal) ||
                !string.Equals(Composition, composition, StringComparison.Ordinal);
            if (!changed)
            {
                return false;
            }

            Value = committed;
            Composition = composition;
            _onChanged?.Invoke(Value);
            return true;
        }

        private static void SplitCommittedAndDisplayValue(
            string committedValue,
            string displayValue,
            out string committed,
            out string composition)
        {
            committed = committedValue ?? string.Empty;
            string display = displayValue ?? committed;
            if (display.StartsWith(committed, StringComparison.Ordinal))
            {
                composition = display.Substring(committed.Length);
                return;
            }

            committed = display;
            composition = string.Empty;
        }

        private string ConsumeTextInput()
        {
            string textInput = string.Empty;
#if ENABLE_INPUT_SYSTEM
            SubscribeTextInput();
            textInput = _queuedText;
            _queuedText = string.Empty;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (string.IsNullOrEmpty(textInput))
            {
                textInput = Input.inputString ?? string.Empty;
            }
#endif
            return textInput;
        }

        private void QueueLegacyTextInput()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
#if ENABLE_INPUT_SYSTEM
            if (_lastTextInputFrame == Time.frameCount)
            {
                return;
            }
#endif
            string textInput = Input.inputString ?? string.Empty;
            if (!string.IsNullOrEmpty(textInput))
            {
                _queuedText += textInput;
                _lastTextInputFrame = Time.frameCount;
            }
#endif
        }

        private bool RemoveLastCharacter()
        {
            if (string.IsNullOrEmpty(Value))
            {
                return false;
            }

            Value = Value.Substring(0, Value.Length - 1);
            return true;
        }

        private string GetCurrentComposition()
        {
#if ENABLE_INPUT_SYSTEM
            return _inputSystemComposition ?? string.Empty;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.compositionString ?? string.Empty;
#else
            return string.Empty;
#endif
        }

        private void EnableImeComposition()
        {
#if ENABLE_INPUT_SYSTEM
            SubscribeTextInput();
            UnityEngine.InputSystem.Keyboard.current?.SetIMEEnabled(true);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!_hasPreviousImeCompositionMode)
            {
                _previousImeCompositionMode = Input.imeCompositionMode;
                _hasPreviousImeCompositionMode = true;
            }

            Input.imeCompositionMode = IMECompositionMode.On;
#endif
        }

        private void RestoreImeComposition()
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard.current?.SetIMEEnabled(false);
            _inputSystemComposition = string.Empty;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (_hasPreviousImeCompositionMode)
            {
                Input.imeCompositionMode = _previousImeCompositionMode;
                _hasPreviousImeCompositionMode = false;
            }
#endif
        }

        private void SubscribeTextInput()
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (_subscribedKeyboard == keyboard)
            {
                return;
            }

            UnsubscribeTextInput();
            if (keyboard == null)
            {
                return;
            }

            _subscribedKeyboard = keyboard;
            _subscribedKeyboard.onTextInput += HandleTextInput;
            _subscribedKeyboard.onIMECompositionChange += HandleImeCompositionChanged;
#endif
        }

        private void UnsubscribeTextInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (_subscribedKeyboard == null)
            {
                return;
            }

            _subscribedKeyboard.onTextInput -= HandleTextInput;
            _subscribedKeyboard.onIMECompositionChange -= HandleImeCompositionChanged;
            _subscribedKeyboard = null;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private void HandleTextInput(char character)
        {
            if (!IsActive)
            {
                return;
            }

            _queuedText += character;
            _inputSystemComposition = string.Empty;
            _lastTextInputFrame = Time.frameCount;
        }

        private void HandleImeCompositionChanged(
            UnityEngine.InputSystem.LowLevel.IMECompositionString composition)
        {
            if (!IsActive)
            {
                return;
            }

            _inputSystemComposition = composition.Count == 0
                ? string.Empty
                : composition.ToString();
        }
#endif
    }

    public static class TerminalKeyboardInput
    {
        public static bool WasSubmitPressedThisFrame()
        {
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                pressed |= keyboard.enterKey != null && keyboard.enterKey.wasPressedThisFrame;
                pressed |= keyboard.numpadEnterKey != null && keyboard.numpadEnterKey.wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            pressed |= Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
            return pressed;
        }

        public static bool WasPressed(KeyCode keyCode)
        {
            bool pressed = false;
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                UnityEngine.InputSystem.Controls.KeyControl keyControl =
                    GetInputSystemKeyControl(keyboard, keyCode);
                pressed |= keyControl != null && keyControl.wasPressedThisFrame;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            pressed |= keyCode != KeyCode.None && Input.GetKeyDown(keyCode);
#endif
            return pressed;
        }

#if ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Controls.KeyControl GetInputSystemKeyControl(
            UnityEngine.InputSystem.Keyboard keyboard,
            KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.UpArrow => keyboard.upArrowKey,
                KeyCode.DownArrow => keyboard.downArrowKey,
                KeyCode.W => keyboard.wKey,
                KeyCode.S => keyboard.sKey,
                KeyCode.E => keyboard.eKey,
                KeyCode.Return => keyboard.enterKey,
                KeyCode.KeypadEnter => keyboard.numpadEnterKey,
                KeyCode.Space => keyboard.spaceKey,
                KeyCode.Backspace => keyboard.backspaceKey,
                KeyCode.Escape => keyboard.escapeKey,
                _ => null
            };
        }
#endif
    }
}
