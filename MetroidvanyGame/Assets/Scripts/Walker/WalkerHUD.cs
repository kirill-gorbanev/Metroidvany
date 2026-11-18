using UnityEngine;

namespace Fortress.Walker
{
    /// <summary>Подсказки на экране: управление (H — скрыть), действие рядом, режим движения.</summary>
    public class WalkerHUD : MonoBehaviour
    {
        public PlayerWalker player;
        bool showHelp = true;
        GUIStyle box, center;

        void Update() { if (WalkerInput.HelpToggleDown) showHelp = !showHelp; }

        void OnGUI()
        {
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15, wordWrap = true };
                box.normal.textColor = Color.white;
                center = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold };
                center.normal.textColor = new Color(1f, 0.85f, 0.4f);
            }
            if (showHelp)
                GUI.Box(new Rect(12, 12, 430, 230),
                    "Крепость-тюрьма — бродилка\n" +
                    "WASD — идти, Shift — бег, Ctrl/C — присед, Пробел — прыжок\n" +
                    "Мышь — обзор, колесо — дистанция камеры\n" +
                    "E — действие (дверь, засов, решётка, кирка)\n" +
                    "Стремянка: лицом к ней W / Пробел, C — отпустить\n" +
                    "R — на старт (камера №1), F — полёт\n" +
                    "1 камера 2 зал 3 двор 4 стена 5 крыша 6 каторга\n" +
                    "Esc — курсор, клик — вернуть, H — скрыть", box);
            if (player == null) return;
            if (!string.IsNullOrEmpty(player.Prompt))
                GUI.Label(new Rect(0, Screen.height * 0.72f, Screen.width, 40), player.Prompt, center);
            if (!string.IsNullOrEmpty(player.Mode))
                GUI.Label(new Rect(0, Screen.height - 48, Screen.width, 30), player.Mode, center);
        }
    }
}
