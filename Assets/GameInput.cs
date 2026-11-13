using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Náhrada starého Input Managera nad novým Input Systemom. Má rovnaké API ako UnityEngine.Input
/// (GetKey, GetKeyDown, GetMouseButton*, GetAxis*, mouseScrollDelta, inputString, anyKeyDown),
/// takže skripty využívajúce KeyCode a prepisovateľné klávesy (KeyBindings) fungujú bez zmeny logiky.
/// Mapa KeyCode → Key je pole, takže dotaz v Update nealokuje a nehľadá v slovníku.
/// </summary>
public static class GameInput
{
    private const float StickDeadZone = 0.19f;     // ako pôvodná joystick os v Input Manageri
    private const float MouseSensitivity = 0.1f;   // ako pôvodné osi "Mouse X/Y"

    private static readonly Key[] keyMap = BuildKeyMap();
    private static readonly StringBuilder typed = new StringBuilder(8);
    private static int typedFrame = -1;
    private static Keyboard hookedKeyboard;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState()
    {
        hookedKeyboard = null;
        typed.Length = 0;
        typedFrame = -1;
    }

    // Text sa zachytáva už od štartu, inak by sa prvý napísaný znak stratil, kým ho niekto prvýkrát zistí
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        HookKeyboard();
        InputSystem.onDeviceChange += (device, change) =>
        {
            if (device is Keyboard) HookKeyboard();
        };
    }

    static void HookKeyboard()
    {
        Keyboard kb = Keyboard.current;
        if (kb == hookedKeyboard) return;
        if (hookedKeyboard != null) hookedKeyboard.onTextInput -= OnTextInput;
        hookedKeyboard = kb;
        if (kb != null) kb.onTextInput += OnTextInput;
    }

    static void OnTextInput(char c)
    {
        // Znaky patria snímke, v ktorej prišli (ako Input.inputString)
        if (typedFrame != Time.frameCount)
        {
            typed.Length = 0;
            typedFrame = Time.frameCount;
        }
        typed.Append(c);
    }

    public static bool GetKey(KeyCode code)
    {
        if (IsMouseCode(code)) return GetMouseButton(code - KeyCode.Mouse0);
        Keyboard kb = Keyboard.current;
        if (kb == null) return false;
        Key key = Lookup(code);
        return key != Key.None && kb[key].isPressed;
    }

    public static bool GetKeyDown(KeyCode code)
    {
        if (IsMouseCode(code)) return GetMouseButtonDown(code - KeyCode.Mouse0);
        Keyboard kb = Keyboard.current;
        if (kb == null) return false;
        Key key = Lookup(code);
        return key != Key.None && kb[key].wasPressedThisFrame;
    }

    public static bool GetKeyUp(KeyCode code)
    {
        if (IsMouseCode(code)) return GetMouseButtonUp(code - KeyCode.Mouse0);
        Keyboard kb = Keyboard.current;
        if (kb == null) return false;
        Key key = Lookup(code);
        return key != Key.None && kb[key].wasReleasedThisFrame;
    }

    public static bool GetMouseButton(int button)
    {
        UnityEngine.InputSystem.Controls.ButtonControl b = MouseButton(button);
        return b != null && b.isPressed;
    }

    public static bool GetMouseButtonDown(int button)
    {
        UnityEngine.InputSystem.Controls.ButtonControl b = MouseButton(button);
        return b != null && b.wasPressedThisFrame;
    }

    public static bool GetMouseButtonUp(int button)
    {
        UnityEngine.InputSystem.Controls.ButtonControl b = MouseButton(button);
        return b != null && b.wasReleasedThisFrame;
    }

    /// <summary>Podporené osi: "Horizontal", "Vertical" (WASD / šípky / ľavá páčka) a "Mouse X", "Mouse Y".</summary>
    public static float GetAxisRaw(string axis)
    {
        switch (axis)
        {
            case "Horizontal": return MoveAxis(Key.D, Key.RightArrow, Key.A, Key.LeftArrow, true);
            case "Vertical": return MoveAxis(Key.W, Key.UpArrow, Key.S, Key.DownArrow, false);
            default: return GetAxis(axis);
        }
    }

    public static float GetAxis(string axis)
    {
        switch (axis)
        {
            case "Mouse X": return Mouse.current != null ? Mouse.current.delta.ReadValue().x * MouseSensitivity : 0f;
            case "Mouse Y": return Mouse.current != null ? Mouse.current.delta.ReadValue().y * MouseSensitivity : 0f;
            case "Horizontal":
            case "Vertical": return GetAxisRaw(axis);
            default: return 0f;
        }
    }

    /// <summary>Koliesko myši v "zárezoch" (±1 na zárez), rovnako ako pôvodné Input.mouseScrollDelta.</summary>
    public static Vector2 mouseScrollDelta
    {
        get
        {
            if (Mouse.current == null) return Vector2.zero;
            Vector2 s = Mouse.current.scroll.ReadValue();
            // Nový systém hlási na Windows 120 jednotiek na zárez
            if (Mathf.Abs(s.y) > 2f) s.y /= 120f;
            if (Mathf.Abs(s.x) > 2f) s.x /= 120f;
            return s;
        }
    }

    /// <summary>Znaky napísané v tejto snímke (kvôli klávesniciam, kde KeyCode nezodpovedá znaku, napr. ľ, š, č).</summary>
    public static string inputString => typedFrame == Time.frameCount && typed.Length > 0 ? typed.ToString() : string.Empty;

    public static bool anyKeyDown
    {
        get
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            Mouse m = Mouse.current;
            return m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame);
        }
    }

    public static bool anyKey
    {
        get
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.anyKey.isPressed) return true;
            Mouse m = Mouse.current;
            return m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed);
        }
    }

    static float MoveAxis(Key pos, Key posAlt, Key neg, Key negAlt, bool x)
    {
        float v = 0f;
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb[pos].isPressed || kb[posAlt].isPressed) v += 1f;
            if (kb[neg].isPressed || kb[negAlt].isPressed) v -= 1f;
        }

        Gamepad pad = Gamepad.current;
        if (v == 0f && pad != null)
        {
            Vector2 stick = pad.leftStick.ReadValue();
            float s = x ? stick.x : stick.y;
            if (Mathf.Abs(s) > StickDeadZone) v = s;
        }
        return v;
    }

    static UnityEngine.InputSystem.Controls.ButtonControl MouseButton(int button)
    {
        Mouse m = Mouse.current;
        if (m == null) return null;
        switch (button)
        {
            case 0: return m.leftButton;
            case 1: return m.rightButton;
            case 2: return m.middleButton;
            case 3: return m.backButton;
            case 4: return m.forwardButton;
            default: return null;
        }
    }

    static bool IsMouseCode(KeyCode code) => code >= KeyCode.Mouse0 && code <= KeyCode.Mouse4;

    static Key Lookup(KeyCode code)
    {
        int i = (int)code;
        return i >= 0 && i < keyMap.Length ? keyMap[i] : Key.None;
    }

    static Key[] BuildKeyMap()
    {
        var map = new Key[512];

        for (int i = 0; i < 26; i++) map[(int)KeyCode.A + i] = Key.A + i;
        for (int i = 1; i <= 9; i++) map[(int)KeyCode.Alpha0 + i] = Key.Digit1 + (i - 1);
        map[(int)KeyCode.Alpha0] = Key.Digit0;
        for (int i = 0; i <= 9; i++) map[(int)KeyCode.Keypad0 + i] = Key.Numpad0 + i;
        for (int i = 0; i < 12; i++) map[(int)KeyCode.F1 + i] = Key.F1 + i;

        map[(int)KeyCode.Space] = Key.Space;
        map[(int)KeyCode.Return] = Key.Enter;
        map[(int)KeyCode.KeypadEnter] = Key.NumpadEnter;
        map[(int)KeyCode.Escape] = Key.Escape;
        map[(int)KeyCode.Tab] = Key.Tab;
        map[(int)KeyCode.Backspace] = Key.Backspace;
        map[(int)KeyCode.Delete] = Key.Delete;
        map[(int)KeyCode.Insert] = Key.Insert;
        map[(int)KeyCode.Home] = Key.Home;
        map[(int)KeyCode.End] = Key.End;
        map[(int)KeyCode.PageUp] = Key.PageUp;
        map[(int)KeyCode.PageDown] = Key.PageDown;
        map[(int)KeyCode.UpArrow] = Key.UpArrow;
        map[(int)KeyCode.DownArrow] = Key.DownArrow;
        map[(int)KeyCode.LeftArrow] = Key.LeftArrow;
        map[(int)KeyCode.RightArrow] = Key.RightArrow;

        map[(int)KeyCode.LeftShift] = Key.LeftShift;
        map[(int)KeyCode.RightShift] = Key.RightShift;
        map[(int)KeyCode.LeftControl] = Key.LeftCtrl;
        map[(int)KeyCode.RightControl] = Key.RightCtrl;
        map[(int)KeyCode.LeftAlt] = Key.LeftAlt;
        map[(int)KeyCode.RightAlt] = Key.RightAlt;
        map[(int)KeyCode.LeftWindows] = Key.LeftMeta;
        map[(int)KeyCode.RightWindows] = Key.RightMeta;
        map[(int)KeyCode.CapsLock] = Key.CapsLock;
        map[(int)KeyCode.Numlock] = Key.NumLock;
        map[(int)KeyCode.ScrollLock] = Key.ScrollLock;
        map[(int)KeyCode.Print] = Key.PrintScreen;
        map[(int)KeyCode.Pause] = Key.Pause;

        map[(int)KeyCode.Minus] = Key.Minus;
        map[(int)KeyCode.Equals] = Key.Equals;
        map[(int)KeyCode.LeftBracket] = Key.LeftBracket;
        map[(int)KeyCode.RightBracket] = Key.RightBracket;
        map[(int)KeyCode.Semicolon] = Key.Semicolon;
        map[(int)KeyCode.Quote] = Key.Quote;
        map[(int)KeyCode.Comma] = Key.Comma;
        map[(int)KeyCode.Period] = Key.Period;
        map[(int)KeyCode.Slash] = Key.Slash;
        map[(int)KeyCode.Backslash] = Key.Backslash;
        map[(int)KeyCode.BackQuote] = Key.Backquote;

        map[(int)KeyCode.KeypadPlus] = Key.NumpadPlus;
        map[(int)KeyCode.KeypadMinus] = Key.NumpadMinus;
        map[(int)KeyCode.KeypadMultiply] = Key.NumpadMultiply;
        map[(int)KeyCode.KeypadDivide] = Key.NumpadDivide;
        map[(int)KeyCode.KeypadPeriod] = Key.NumpadPeriod;
        map[(int)KeyCode.KeypadEquals] = Key.NumpadEquals;

        return map;
    }
}
