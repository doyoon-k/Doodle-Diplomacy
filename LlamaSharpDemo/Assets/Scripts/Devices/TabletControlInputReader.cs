using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DoodleDiplomacy.Devices
{
    internal readonly struct TabletControlPointerState
    {
        public TabletControlPointerState(
            Vector2 screenPosition,
            bool pressedThisFrame,
            bool held,
            bool releasedThisFrame,
            bool isInsideCameraBounds)
        {
            ScreenPosition = screenPosition;
            PressedThisFrame = pressedThisFrame;
            Held = held;
            ReleasedThisFrame = releasedThisFrame;
            IsInsideCameraBounds = isInsideCameraBounds;
        }

        public Vector2 ScreenPosition { get; }
        public bool PressedThisFrame { get; }
        public bool Held { get; }
        public bool ReleasedThisFrame { get; }
        public bool IsInsideCameraBounds { get; }
    }

    internal static class TabletControlInputReader
    {
        public static bool TryRead(UnityEngine.Camera referenceCamera, out TabletControlPointerState pointerState)
        {
            Vector2 pointerScreenPos;
            bool pointerDown;
            bool pointerHeld;
            bool pointerUp;

#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                pointerScreenPos = mouse.position.ReadValue();
                pointerDown = mouse.leftButton.wasPressedThisFrame;
                pointerHeld = mouse.leftButton.isPressed;
                pointerUp = mouse.leftButton.wasReleasedThisFrame;
                pointerState = new TabletControlPointerState(
                    pointerScreenPos,
                    pointerDown,
                    pointerHeld,
                    pointerUp,
                    IsPointerWithinCameraBounds(referenceCamera, pointerScreenPos));
                return true;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            pointerScreenPos = Input.mousePosition;
            pointerDown = Input.GetMouseButtonDown(0);
            pointerHeld = Input.GetMouseButton(0);
            pointerUp = Input.GetMouseButtonUp(0);
            pointerState = new TabletControlPointerState(
                pointerScreenPos,
                pointerDown,
                pointerHeld,
                pointerUp,
                IsPointerWithinCameraBounds(referenceCamera, pointerScreenPos));
            return true;
#else
            pointerState = default;
            return false;
#endif
        }

        private static bool IsPointerWithinCameraBounds(UnityEngine.Camera referenceCamera, Vector2 pointerScreenPos)
        {
            if (float.IsNaN(pointerScreenPos.x) || float.IsNaN(pointerScreenPos.y))
            {
                return false;
            }

            Rect pixelRect = referenceCamera != null && referenceCamera.pixelRect.width > 0f && referenceCamera.pixelRect.height > 0f
                ? referenceCamera.pixelRect
                : new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));
            if (pixelRect.width <= 0f || pixelRect.height <= 0f)
            {
                return false;
            }

            return pointerScreenPos.x >= pixelRect.xMin &&
                   pointerScreenPos.x <= pixelRect.xMax &&
                   pointerScreenPos.y >= pixelRect.yMin &&
                   pointerScreenPos.y <= pixelRect.yMax;
        }
    }
}
