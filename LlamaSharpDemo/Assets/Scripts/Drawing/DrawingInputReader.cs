using UnityEngine;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

internal static class DrawingInputReader
{
    public static bool IsPointerOverUi()
    {
        if (EventSystem.current == null)
        {
            return false;
        }

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && EventSystem.current.IsPointerOverGameObject(Mouse.current.deviceId))
        {
            return true;
        }
#endif

        return EventSystem.current.IsPointerOverGameObject();
    }

    public static bool TryGetPointerScreenPosition(Camera drawingCamera, out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            screenPosition = Mouse.current.position.ReadValue();
        }
        else
        {
            screenPosition = default;
            return false;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (
#if ENABLE_INPUT_SYSTEM
            Mouse.current == null &&
#endif
            true)
        {
            screenPosition = Input.mousePosition;
        }
#else
        if (
#if ENABLE_INPUT_SYSTEM
            Mouse.current == null
#else
            true
#endif
            )
        {
            screenPosition = default;
            return false;
        }
#endif

        if (float.IsNaN(screenPosition.x) || float.IsNaN(screenPosition.y))
        {
            return false;
        }

        Rect pixelRect = drawingCamera != null && drawingCamera.pixelRect.width > 0f && drawingCamera.pixelRect.height > 0f
            ? drawingCamera.pixelRect
            : new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));

        if (pixelRect.width <= 0f || pixelRect.height <= 0f)
        {
            return false;
        }

        return screenPosition.x >= pixelRect.xMin &&
               screenPosition.x <= pixelRect.xMax &&
               screenPosition.y >= pixelRect.yMin &&
               screenPosition.y <= pixelRect.yMax;
    }

    public static bool GetPointerDownThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonDown(0);
#else
        return false;
#endif
    }

    public static bool GetPointerHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(0);
#else
        return false;
#endif
    }

    public static bool GetPointerUpThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButtonUp(0);
#else
        return false;
#endif
    }

    public static bool GetUndoShortcutPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            bool controlPressed = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            bool shiftPressed = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                return !controlPressed && !shiftPressed && Keyboard.current.zKey.wasPressedThisFrame;
            }
#endif
            if (controlPressed && Keyboard.current.zKey.wasPressedThisFrame)
            {
                return true;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        bool controlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#if UNITY_EDITOR
        if (Application.isEditor)
        {
            return !controlPressed && !shiftPressed && Input.GetKeyDown(KeyCode.Z);
        }
#endif
        return controlPressed && Input.GetKeyDown(KeyCode.Z);
#else
        return false;
#endif
    }

    public static bool GetRedoShortcutPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            bool controlPressed = Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed;
            bool shiftPressed = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
#if UNITY_EDITOR
            if (Application.isEditor)
            {
                if (!controlPressed && Keyboard.current.yKey.wasPressedThisFrame)
                {
                    return true;
                }

                if (!controlPressed && shiftPressed && Keyboard.current.zKey.wasPressedThisFrame)
                {
                    return true;
                }

                return false;
            }
#endif
            if (controlPressed && Keyboard.current.yKey.wasPressedThisFrame)
            {
                return true;
            }

            if (controlPressed && shiftPressed && Keyboard.current.zKey.wasPressedThisFrame)
            {
                return true;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        bool controlPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shiftPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#if UNITY_EDITOR
        if (Application.isEditor)
        {
            return (!controlPressed && Input.GetKeyDown(KeyCode.Y)) ||
                   (!controlPressed && shiftPressed && Input.GetKeyDown(KeyCode.Z));
        }
#endif
        return (controlPressed && Input.GetKeyDown(KeyCode.Y)) ||
               (controlPressed && shiftPressed && Input.GetKeyDown(KeyCode.Z));
#else
        return false;
#endif
    }
}
