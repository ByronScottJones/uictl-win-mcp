using System.Runtime.InteropServices;
using UICtl.Core.Interop;

namespace UICtl.Core;

public static class InputSynthesis
{
    public static void Move(Point to) => NativeMethods.SetCursorPos((int)Math.Round(to.X), (int)Math.Round(to.Y));

    public static void Click(Point at, MouseButton button, int clickCount)
    {
        Move(at);
        var (down, up) = button switch
        {
            MouseButton.Left => (Consts.MOUSEEVENTF_LEFTDOWN, Consts.MOUSEEVENTF_LEFTUP),
            MouseButton.Right => (Consts.MOUSEEVENTF_RIGHTDOWN, Consts.MOUSEEVENTF_RIGHTUP),
            MouseButton.Center => (Consts.MOUSEEVENTF_MIDDLEDOWN, Consts.MOUSEEVENTF_MIDDLEUP),
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };

        for (int i = 0; i < Math.Max(1, clickCount); i++)
        {
            SendMouseEvent(down);
            SendMouseEvent(up);
        }
    }

    public static void Scroll(Point at, int dx, int dy)
    {
        Move(at);
        if (dy != 0) SendMouseEvent(Consts.MOUSEEVENTF_WHEEL, mouseData: unchecked((uint)(dy * 120)));
        if (dx != 0) SendMouseEvent(Consts.MOUSEEVENTF_HWHEEL, mouseData: unchecked((uint)(dx * 120)));
    }

    /// <summary>KEYEVENTF_UNICODE sends the exact UTF-16 code unit regardless of keyboard layout, so no VK-code lookup is needed.</summary>
    public static void TypeText(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(UnicodeKeyInput(c, keyUp: false));
            inputs.Add(UnicodeKeyInput(c, keyUp: true));
        }
        SendInputs(inputs);
    }

    public static void SendKeyCombo(string combo)
    {
        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            throw new UiCtlException("empty key combo");

        var modifiers = parts[..^1].Select(ResolveModifier).ToList();
        ushort baseKey = ResolveKey(parts[^1]);

        var inputs = new List<INPUT>();
        foreach (var vk in modifiers) inputs.Add(VirtualKeyInput(vk, keyUp: false));
        inputs.Add(VirtualKeyInput(baseKey, keyUp: false));
        inputs.Add(VirtualKeyInput(baseKey, keyUp: true));
        for (int i = modifiers.Count - 1; i >= 0; i--) inputs.Add(VirtualKeyInput(modifiers[i], keyUp: true));

        SendInputs(inputs);
    }

    private static ushort ResolveModifier(string name) => name.ToLowerInvariant() switch
    {
        "ctrl" or "control" => Consts.VK_CONTROL,
        "shift" => Consts.VK_SHIFT,
        "alt" => Consts.VK_MENU,
        "win" or "windows" or "cmd" => Consts.VK_LWIN,
        _ => throw new UiCtlException($"unknown modifier \"{name}\""),
    };

    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["esc"] = 0x1B, ["escape"] = 0x1B,
        ["enter"] = 0x0D, ["return"] = 0x0D,
        ["tab"] = 0x09,
        ["space"] = 0x20,
        ["backspace"] = 0x08,
        ["delete"] = 0x2E, ["del"] = 0x2E,
        ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["home"] = 0x24, ["end"] = 0x23,
        ["pageup"] = 0x21, ["pagedown"] = 0x22,
        ["f1"] = 0x70, ["f2"] = 0x71, ["f3"] = 0x72, ["f4"] = 0x73,
        ["f5"] = 0x74, ["f6"] = 0x75, ["f7"] = 0x76, ["f8"] = 0x77,
        ["f9"] = 0x78, ["f10"] = 0x79, ["f11"] = 0x7A, ["f12"] = 0x7B,
    };

    private static ushort ResolveKey(string key)
    {
        if (NamedKeys.TryGetValue(key, out var vk)) return vk;
        if (key.Length == 1)
        {
            short scan = NativeMethods.VkKeyScanW(key[0]);
            if (scan != -1) return (ushort)(scan & 0xFF);
        }
        throw new UiCtlException($"unknown key \"{key}\"");
    }

    private static INPUT UnicodeKeyInput(char c, bool keyUp) => new()
    {
        type = Consts.INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = c,
                dwFlags = Consts.KEYEVENTF_UNICODE | (keyUp ? Consts.KEYEVENTF_KEYUP : 0),
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    private static INPUT VirtualKeyInput(ushort vk, bool keyUp) => new()
    {
        type = Consts.INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = keyUp ? Consts.KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    private static void SendMouseEvent(uint flags, uint mouseData = 0)
    {
        var input = new INPUT
        {
            type = Consts.INPUT_MOUSE,
            u = new INPUTUNION { mi = new MOUSEINPUT { dx = 0, dy = 0, mouseData = mouseData, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero } },
        };
        SendInputs([input]);
    }

    private static void SendInputs(IReadOnlyList<INPUT> inputs)
    {
        var array = inputs as INPUT[] ?? inputs.ToArray();
        if (array.Length == 0) return;
        uint sent = NativeMethods.SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        if (sent != array.Length)
            throw new UiCtlException($"SendInput only sent {sent} of {array.Length} events (error {Marshal.GetLastWin32Error()})");
    }
}
