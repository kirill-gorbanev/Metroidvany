using UnityEngine;
using UnityEngine.InputSystem;

namespace Fortress.Walker
{
    /// <summary>Ввод бродилки на Input System (в проекте включён только новый ввод).</summary>
    public static class WalkerInput
    {
        static Keyboard K => Keyboard.current;
        static Mouse M => Mouse.current;

        public static Vector2 Move
        {
            get
            {
                if (K == null) return Vector2.zero;
                float x = (K.dKey.isPressed ? 1 : 0) - (K.aKey.isPressed ? 1 : 0);
                float y = (K.wKey.isPressed ? 1 : 0) - (K.sKey.isPressed ? 1 : 0);
                var v = new Vector2(x, y);
                return v.sqrMagnitude > 1 ? v.normalized : v;
            }
        }

        public static bool Run => K != null && K.leftShiftKey.isPressed;
        public static bool JumpDown => K != null && K.spaceKey.wasPressedThisFrame;
        public static bool JumpHeld => K != null && K.spaceKey.isPressed;
        public static bool Crouch => K != null && (K.leftCtrlKey.isPressed || K.cKey.isPressed);
        public static bool InteractDown => K != null && K.eKey.wasPressedThisFrame;
        public static bool RespawnDown => K != null && K.rKey.wasPressedThisFrame;
        public static bool FlyToggleDown => K != null && K.fKey.wasPressedThisFrame;
        public static bool HelpToggleDown => K != null && K.hKey.wasPressedThisFrame;
        public static bool EscDown => K != null && K.escapeKey.wasPressedThisFrame;
        /// <summary>Нажатая цифра 1..6 -> индекс 0..5, иначе -1.</summary>
        public static int PointDown
        {
            get
            {
                if (K == null) return -1;
                if (K.digit1Key.wasPressedThisFrame) return 0;
                if (K.digit2Key.wasPressedThisFrame) return 1;
                if (K.digit3Key.wasPressedThisFrame) return 2;
                if (K.digit4Key.wasPressedThisFrame) return 3;
                if (K.digit5Key.wasPressedThisFrame) return 4;
                if (K.digit6Key.wasPressedThisFrame) return 5;
                return -1;
            }
        }

        public static bool ClickDown => M != null && M.leftButton.wasPressedThisFrame;
        public static Vector2 Look => M != null ? M.delta.ReadValue() : Vector2.zero;
        public static float Scroll => M != null ? M.scroll.ReadValue().y : 0f;
    }
}
