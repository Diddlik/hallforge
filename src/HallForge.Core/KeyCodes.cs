namespace HallForge.Core;

/// <summary>Maps HID keyboard usages (page 0x07) to W3C <c>KeyboardEvent.code</c> names, the key ids used throughout HallForge.</summary>
public static class KeyCodes
{
    private static readonly Dictionary<int, string> ByUsage = Build();

    public static string? FromHidUsage(int usage) => ByUsage.GetValueOrDefault(usage);

    private static Dictionary<int, string> Build()
    {
        var map = new Dictionary<int, string>
        {
            [0x28] = "Enter", [0x29] = "Escape", [0x2A] = "Backspace", [0x2B] = "Tab", [0x2C] = "Space",
            [0x2D] = "Minus", [0x2E] = "Equal", [0x2F] = "BracketLeft", [0x30] = "BracketRight", [0x31] = "Backslash",
            [0x32] = "IntlHash", [0x33] = "Semicolon", [0x34] = "Quote", [0x35] = "Backquote", [0x36] = "Comma",
            [0x37] = "Period", [0x38] = "Slash", [0x39] = "CapsLock",
            [0x46] = "PrintScreen", [0x47] = "ScrollLock", [0x48] = "Pause", [0x49] = "Insert", [0x4A] = "Home",
            [0x4B] = "PageUp", [0x4C] = "Delete", [0x4D] = "End", [0x4E] = "PageDown",
            [0x4F] = "ArrowRight", [0x50] = "ArrowLeft", [0x51] = "ArrowDown", [0x52] = "ArrowUp",
            [0x64] = "IntlBackslash",
            [0xE0] = "ControlLeft", [0xE1] = "ShiftLeft", [0xE2] = "AltLeft", [0xE3] = "MetaLeft",
            [0xE4] = "ControlRight", [0xE5] = "ShiftRight", [0xE6] = "AltRight", [0xE7] = "MetaRight",
        };
        for (var i = 0; i < 26; i++)
            map[0x04 + i] = $"Key{(char)('A' + i)}";
        for (var i = 0; i < 10; i++)
            map[0x1E + i] = $"Digit{(i + 1) % 10}";
        for (var i = 0; i < 12; i++)
            map[0x3A + i] = $"F{i + 1}";
        return map;
    }
}
