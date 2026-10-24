namespace Cothon.Core.HotKeys;

/// <summary>
/// A global hotkey chord, e.g. "Ctrl+Alt+Space". Parsed from Settings,
/// mapped to Win32 Registration, and printable back for the UI.
/// </summary>
public readonly record struct HotkeyChord(bool Ctrl, bool Alt, bool Shift, bool Win, uint VirtualKey)
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public static HotkeyChord Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        bool ctrl = false, alt = false, shift = false, win = false;
        uint key = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim().ToLowerInvariant();
            switch (token)
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "win":
                case "windows":
                    win = true;
                    break;
                default:
                    if (key != 0)
                        throw new FormatException($"More than one key in '{text}'.");
                    key = DecodeKey(token, text);
                    break;
            }
        }

        if (key == 0)
            throw new FormatException($"No key in '{text}'.");
        // A bare key can only be an F-key; every other chord needs Ctrl/Alt/Win
        // so normal typing never triggers the mic.
        if (!ctrl && !alt && !win && key is < 0x70u or > 0x87u)
            throw new FormatException(
                $"'{text}' needs Ctrl, Alt, or Win as a modifier.");

        return new HotkeyChord(ctrl, alt, shift, win, key);
    }

    private static uint DecodeKey(string token, string original)
    {
        if (token.Length == 1)
        {
            var c = token[0];
            if (c is >= 'a' and <= 'z')
                return (uint)(c - 'a' + 0x41);
            if (c is >= '0' and <= '9')
                return (uint)(c - '0' + 0x30);
            if (token == " ")
                return 0x20u;
        }

        if (token == "space")
            return 0x20u;

        if (token[0] == 'f' && token.Length is 2 or 3 &&
            ushort.TryParse(token.AsSpan(1), out var f) && f is >= 1 and <= 24)
            return f + 0x6Fu; // F1 = 0x70

        throw new FormatException($"Unknown key '{token}' in '{original}'.");
    }

    private string KeyName()
    {
        return VirtualKey switch
        {
            0x20 => "Space",
            >= 0x41 and <= 0x5A => char.ToString((char)('A' + VirtualKey - 0x41)),
            >= 0x30 and <= 0x39 => char.ToString((char)('0' + VirtualKey - 0x30)),
            >= 0x70 and <= 0x87 => VirtualKey <= 0x88 ? $"F{VirtualKey - 0x6F}" : VirtualKey.ToString("X2"),
            _ => VirtualKey.ToString("X2"),
        };
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName());
        return string.Join("+", parts);
    }

    /// <summary>MOD_* flags for RegisterHotKey.</summary>
    public uint ModifierFlags()
    {
        uint m = 0;
        if (Alt) m |= ModAlt;
        if (Ctrl) m |= ModControl;
        if (Shift) m |= ModShift;
        if (Win) m |= ModWin;
        return m;
    }
}
