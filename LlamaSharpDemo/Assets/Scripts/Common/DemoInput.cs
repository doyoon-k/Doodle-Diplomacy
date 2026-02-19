using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public static class DemoInput
{
    public static bool GetKeyDown(KeyCode keyCode)
    {
#if ENABLE_INPUT_SYSTEM
        if (GetKeyDownFromInputSystem(keyCode))
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(keyCode);
#else
        return false;
#endif
    }

    public static float GetAxisRaw(string axisName)
    {
#if ENABLE_INPUT_SYSTEM
        if (TryGetAxisRawFromInputSystem(axisName, out float inputSystemValue))
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Mathf.Abs(inputSystemValue) <= 0.0001f)
            {
                return Input.GetAxisRaw(axisName);
            }
#endif
            return inputSystemValue;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetAxisRaw(axisName);
#else
        return 0f;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static bool GetKeyDownFromInputSystem(KeyCode keyCode)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        switch (keyCode)
        {
            case KeyCode.Space: return keyboard.spaceKey.wasPressedThisFrame;
            case KeyCode.Escape: return keyboard.escapeKey.wasPressedThisFrame;
            case KeyCode.Return: return keyboard.enterKey.wasPressedThisFrame;
            case KeyCode.Tab: return keyboard.tabKey.wasPressedThisFrame;

            case KeyCode.UpArrow: return keyboard.upArrowKey.wasPressedThisFrame;
            case KeyCode.DownArrow: return keyboard.downArrowKey.wasPressedThisFrame;
            case KeyCode.LeftArrow: return keyboard.leftArrowKey.wasPressedThisFrame;
            case KeyCode.RightArrow: return keyboard.rightArrowKey.wasPressedThisFrame;

            case KeyCode.LeftShift: return keyboard.leftShiftKey.wasPressedThisFrame;
            case KeyCode.RightShift: return keyboard.rightShiftKey.wasPressedThisFrame;
            case KeyCode.LeftControl: return keyboard.leftCtrlKey.wasPressedThisFrame;
            case KeyCode.RightControl: return keyboard.rightCtrlKey.wasPressedThisFrame;
            case KeyCode.LeftAlt: return keyboard.leftAltKey.wasPressedThisFrame;
            case KeyCode.RightAlt: return keyboard.rightAltKey.wasPressedThisFrame;

            case KeyCode.A: return keyboard.aKey.wasPressedThisFrame;
            case KeyCode.B: return keyboard.bKey.wasPressedThisFrame;
            case KeyCode.C: return keyboard.cKey.wasPressedThisFrame;
            case KeyCode.D: return keyboard.dKey.wasPressedThisFrame;
            case KeyCode.E: return keyboard.eKey.wasPressedThisFrame;
            case KeyCode.F: return keyboard.fKey.wasPressedThisFrame;
            case KeyCode.G: return keyboard.gKey.wasPressedThisFrame;
            case KeyCode.H: return keyboard.hKey.wasPressedThisFrame;
            case KeyCode.I: return keyboard.iKey.wasPressedThisFrame;
            case KeyCode.J: return keyboard.jKey.wasPressedThisFrame;
            case KeyCode.K: return keyboard.kKey.wasPressedThisFrame;
            case KeyCode.L: return keyboard.lKey.wasPressedThisFrame;
            case KeyCode.M: return keyboard.mKey.wasPressedThisFrame;
            case KeyCode.N: return keyboard.nKey.wasPressedThisFrame;
            case KeyCode.O: return keyboard.oKey.wasPressedThisFrame;
            case KeyCode.P: return keyboard.pKey.wasPressedThisFrame;
            case KeyCode.Q: return keyboard.qKey.wasPressedThisFrame;
            case KeyCode.R: return keyboard.rKey.wasPressedThisFrame;
            case KeyCode.S: return keyboard.sKey.wasPressedThisFrame;
            case KeyCode.T: return keyboard.tKey.wasPressedThisFrame;
            case KeyCode.U: return keyboard.uKey.wasPressedThisFrame;
            case KeyCode.V: return keyboard.vKey.wasPressedThisFrame;
            case KeyCode.W: return keyboard.wKey.wasPressedThisFrame;
            case KeyCode.X: return keyboard.xKey.wasPressedThisFrame;
            case KeyCode.Y: return keyboard.yKey.wasPressedThisFrame;
            case KeyCode.Z: return keyboard.zKey.wasPressedThisFrame;

            case KeyCode.Alpha0: return keyboard.digit0Key.wasPressedThisFrame;
            case KeyCode.Alpha1: return keyboard.digit1Key.wasPressedThisFrame;
            case KeyCode.Alpha2: return keyboard.digit2Key.wasPressedThisFrame;
            case KeyCode.Alpha3: return keyboard.digit3Key.wasPressedThisFrame;
            case KeyCode.Alpha4: return keyboard.digit4Key.wasPressedThisFrame;
            case KeyCode.Alpha5: return keyboard.digit5Key.wasPressedThisFrame;
            case KeyCode.Alpha6: return keyboard.digit6Key.wasPressedThisFrame;
            case KeyCode.Alpha7: return keyboard.digit7Key.wasPressedThisFrame;
            case KeyCode.Alpha8: return keyboard.digit8Key.wasPressedThisFrame;
            case KeyCode.Alpha9: return keyboard.digit9Key.wasPressedThisFrame;

            default:
                return false;
        }
    }

    private static bool TryGetAxisRawFromInputSystem(string axisName, out float value)
    {
        value = 0f;
        if (string.IsNullOrWhiteSpace(axisName))
        {
            return false;
        }

        if (axisName.Equals("Horizontal", StringComparison.OrdinalIgnoreCase))
        {
            value = ReadHorizontal();
            return true;
        }

        if (axisName.Equals("Vertical", StringComparison.OrdinalIgnoreCase))
        {
            value = ReadVertical();
            return true;
        }

        return false;
    }

    private static float ReadHorizontal()
    {
        float value = 0f;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            {
                value -= 1f;
            }

            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            {
                value += 1f;
            }
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            float stickX = gamepad.leftStick.ReadValue().x;
            if (Mathf.Abs(stickX) > Mathf.Abs(value))
            {
                value = stickX;
            }
        }

        return Mathf.Clamp(value, -1f, 1f);
    }

    private static float ReadVertical()
    {
        float value = 0f;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            {
                value -= 1f;
            }

            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            {
                value += 1f;
            }
        }

        Gamepad gamepad = Gamepad.current;
        if (gamepad != null)
        {
            float stickY = gamepad.leftStick.ReadValue().y;
            if (Mathf.Abs(stickY) > Mathf.Abs(value))
            {
                value = stickY;
            }
        }

        return Mathf.Clamp(value, -1f, 1f);
    }
#endif
}
