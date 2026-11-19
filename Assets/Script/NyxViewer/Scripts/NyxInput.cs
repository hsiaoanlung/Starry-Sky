// Thin input wrapper that works with the new Input System package, the legacy Input Manager, or both.
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Nyx
{
    public static class NyxInput
    {
        public static bool GetKeyDown(KeyCode code)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return false;
            Key key;
            switch (code)
            {
                case KeyCode.Space: key = Key.Space; break;
                case KeyCode.LeftArrow: key = Key.LeftArrow; break;
                case KeyCode.RightArrow: key = Key.RightArrow; break;
                case KeyCode.Home: key = Key.Home; break;
                case KeyCode.P: key = Key.P; break;
                default:
                    if (code >= KeyCode.Alpha1 && code <= KeyCode.Alpha9)
                        key = Key.Digit1 + (code - KeyCode.Alpha1);
                    else
                        return false;
                    break;
            }
            return kb[key].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(code);
#else
            return false;
#endif
        }

        /// <summary>True while the left or right mouse button is held.</summary>
        public static bool Dragging
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                return m != null && (m.leftButton.isPressed || m.rightButton.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButton(0) || Input.GetMouseButton(1);
#else
                return false;
#endif
            }
        }

        /// <summary>Mouse movement this frame, roughly in legacy "Mouse X/Y" axis units.</summary>
        public static Vector2 MouseDelta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                return m != null ? m.delta.ReadValue() * 0.1f : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>Scroll wheel notches this frame (+ = away from user).</summary>
        public static float Scroll
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                return m != null ? m.scroll.ReadValue().y / 120f : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
                return Input.mouseScrollDelta.y;
#else
                return 0f;
#endif
            }
        }
    }
}
