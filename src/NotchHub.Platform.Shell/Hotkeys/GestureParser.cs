namespace NotchHub.Platform.Shell.Hotkeys;

/// <summary>Parses gestures like "Ctrl+Alt+Space", "Win+Shift+N", "Ctrl+F12", "Ctrl+Alt+`", "Ctrl++".</summary>
internal static class GestureParser
{
    private static readonly Dictionary<string, uint> Keys = BuildKeys();

    public static bool TryParse(string? gesture, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        var text = gesture.Trim();
        var parts = new List<string>();
        // "Ctrl++" → ["Ctrl", "+"]; split on '+' that separate tokens.
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '+' || i == start) continue;
            parts.Add(text[start..i].Trim());
            start = i + 1;
        }
        if (start < text.Length) parts.Add(text[start..].Trim());
        else return false; // trailing separator without key
        if (parts.Count == 0) return false;

        for (var i = 0; i < parts.Count; i++)
        {
            var token = parts[i];
            var isLast = i == parts.Count - 1;
            var mod = ParseModifier(token);
            if (mod != 0 && !isLast)
            {
                if ((modifiers & mod) != 0) return false;
                modifiers |= mod;
                continue;
            }
            if (!isLast) return false;
            if (!Keys.TryGetValue(token, out virtualKey)) return false;
        }

        if (virtualKey == 0) return false;
        // A bare key is only allowed for F13-F24 / media-less keys that nobody types; everything else needs a modifier.
        var isFunction = virtualKey is >= 0x70 and <= 0x87;
        return modifiers != 0 || isFunction;
    }

    private static uint ParseModifier(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" or "ctl" => Internal.Interop.Win32.MOD_CONTROL,
        "alt" or "menu" => Internal.Interop.Win32.MOD_ALT,
        "shift" => Internal.Interop.Win32.MOD_SHIFT,
        "win" or "windows" or "meta" or "super" or "lwin" => Internal.Interop.Win32.MOD_WIN,
        _ => 0,
    };

    private static Dictionary<string, uint> BuildKeys()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++) map[c.ToString()] = c;
        for (var d = 0; d <= 9; d++)
        {
            map[d.ToString()] = (uint)('0' + d);
            map["D" + d] = (uint)('0' + d);
            map["Num" + d] = (uint)(0x60 + d);
            map["NumPad" + d] = (uint)(0x60 + d);
        }
        for (var f = 1; f <= 24; f++) map["F" + f] = (uint)(0x6F + f);

        void Add(uint vk, params string[] names)
        {
            foreach (var n in names) map[n] = vk;
        }

        Add(0x20, "Space", "Spacebar");
        Add(0x0D, "Enter", "Return");
        Add(0x09, "Tab");
        Add(0x1B, "Esc", "Escape");
        Add(0x08, "Backspace", "Back");
        Add(0x2E, "Delete", "Del");
        Add(0x2D, "Insert", "Ins");
        Add(0x24, "Home");
        Add(0x23, "End");
        Add(0x21, "PageUp", "PgUp", "Prior");
        Add(0x22, "PageDown", "PgDn", "Next");
        Add(0x26, "Up");
        Add(0x28, "Down");
        Add(0x25, "Left");
        Add(0x27, "Right");
        Add(0x2C, "PrintScreen", "PrtSc", "Snapshot");
        Add(0x13, "Pause", "Break");
        Add(0x91, "ScrollLock", "Scroll");
        Add(0x6A, "Multiply", "NumMultiply");
        Add(0x6B, "Add", "NumAdd", "NumPlus");
        Add(0x6D, "Subtract", "NumSubtract", "NumMinus");
        Add(0x6E, "Decimal", "NumDecimal");
        Add(0x6F, "Divide", "NumDivide");
        // OEM keys (US layout names + the characters themselves).
        Add(0xC0, "`", "~", "Backtick", "Tilde", "Grave", "Oem3", "OemTilde");
        Add(0xBD, "-", "_", "Minus", "OemMinus");
        Add(0xBB, "=", "+", "Plus", "Equals", "OemPlus");
        Add(0xDB, "[", "{", "OpenBracket", "LeftBracket", "Oem4", "OemOpenBrackets");
        Add(0xDD, "]", "}", "CloseBracket", "RightBracket", "Oem6", "OemCloseBrackets");
        Add(0xDC, "\\", "|", "Backslash", "Oem5", "OemPipe");
        Add(0xBA, ";", ":", "Semicolon", "Oem1", "OemSemicolon");
        Add(0xDE, "'", "\"", "Quote", "Apostrophe", "Oem7", "OemQuotes");
        Add(0xBC, ",", "<", "Comma", "OemComma");
        Add(0xBE, ".", ">", "Period", "Dot", "OemPeriod");
        Add(0xBF, "/", "?", "Slash", "Oem2", "OemQuestion");
        Add(0xE2, "Oem102", "OemBackslash");
        return map;
    }
}
