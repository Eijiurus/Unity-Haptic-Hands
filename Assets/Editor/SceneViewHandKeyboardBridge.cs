using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SceneViewHandKeyboardBridge
{
    private static bool _left;
    private static bool _right;
    private static bool _forward;
    private static bool _back;
    private static bool _up;
    private static bool _down;
    private static bool _resetRequested;

    static SceneViewHandKeyboardBridge()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += OnEditorUpdate;
    }

    private static void OnSceneGUI(SceneView sceneView)
    {
        if (!EditorApplication.isPlaying) return;

        Event e = Event.current;
        if (e == null) return;

        if (e.type == EventType.KeyDown || e.type == EventType.KeyUp)
        {
            bool pressed = e.type == EventType.KeyDown;
            switch (e.keyCode)
            {
                case KeyCode.A:
                case KeyCode.LeftArrow: _left = pressed; e.Use(); break;
                case KeyCode.J:
                case KeyCode.Keypad4: _left = pressed; e.Use(); break;
                case KeyCode.D:
                case KeyCode.RightArrow: _right = pressed; e.Use(); break;
                case KeyCode.L:
                case KeyCode.Keypad6: _right = pressed; e.Use(); break;
                case KeyCode.W:
                case KeyCode.UpArrow: _forward = pressed; e.Use(); break;
                case KeyCode.I:
                case KeyCode.Keypad8: _forward = pressed; e.Use(); break;
                case KeyCode.S:
                case KeyCode.DownArrow: _back = pressed; e.Use(); break;
                case KeyCode.K:
                case KeyCode.Keypad2: _back = pressed; e.Use(); break;
                case KeyCode.Q:
                case KeyCode.PageUp: _up = pressed; e.Use(); break;
                case KeyCode.U:
                case KeyCode.Keypad9: _up = pressed; e.Use(); break;
                case KeyCode.E:
                case KeyCode.PageDown: _down = pressed; e.Use(); break;
                case KeyCode.O:
                case KeyCode.Keypad3: _down = pressed; e.Use(); break;
                case KeyCode.R:
                case KeyCode.P:
                case KeyCode.Keypad0:
                    if (pressed) _resetRequested = true;
                    e.Use();
                    break;
            }
        }
    }

    private static void OnEditorUpdate()
    {
        if (!EditorApplication.isPlaying) return;

        float x = (_right ? 1f : 0f) + (_left ? -1f : 0f);
        float y = (_up ? 1f : 0f) + (_down ? -1f : 0f);
        float z = (_forward ? 1f : 0f) + (_back ? -1f : 0f);

        Vector3 dir = new Vector3(x, y, z);
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        DataGloveHandDriver[] drivers = Object.FindObjectsByType<DataGloveHandDriver>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int i = 0; i < drivers.Length; i++)
        {
            if (drivers[i] != null)
                drivers[i].SetEditorKeyboardInput(dir, _resetRequested);
        }

        _resetRequested = false;
    }
}
