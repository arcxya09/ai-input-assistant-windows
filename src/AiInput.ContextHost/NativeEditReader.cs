using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AiInput.Core;
using AiInput.Windows;

namespace AiInput.ContextHost;

internal sealed record NativeEditState(nint Handle, int Position, string Fingerprint, string Before, string After);

internal static class NativeEditReader
{
    internal static nint FocusWindow(uint thread)
    {
        var info = new Native.GUITHREADINFO { Size = (uint)Marshal.SizeOf<Native.GUITHREADINFO>() };
        return Native.GetGUIThreadInfo(thread, ref info) ? info.Focus : 0;
    }
    internal static bool IsComposing(nint focus)
    {
        if (focus == 0) return false;
        // IMM is best-effort across processes. A missing context never implies a
        // protected field, nor does it justify rejecting an entire CJK keyboard.
        nint context = Native.ImmGetContext(focus);
        if (context == 0) return false;
        try { return Native.ImmGetCompositionString(context, 8, 0, 0) > 0; }
        finally { Native.ImmReleaseContext(focus, context); }
    }
    internal static NativeEditState? Read(nint foreground, uint thread, uint process)
    {
        nint hwnd = FocusWindow(thread);
        if (hwnd == 0) return null;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != process) return null;
        var name = new StringBuilder(256);
        Native.GetClassName(hwnd, name, name.Capacity);
        string cls = name.ToString();
        bool edit = cls.Equals("Edit", StringComparison.OrdinalIgnoreCase) || cls.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase);
        bool rich = cls.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase) || cls.Equals("RICHEDIT50W", StringComparison.OrdinalIgnoreCase) ||
            cls.StartsWith("WindowsForms10.RICHEDIT", StringComparison.OrdinalIgnoreCase);
        if (!edit && !rich) return null;
        long style = (long)Native.GetWindowLongPtr(hwnd, -16);
        if ((style & 0x20) != 0) throw new InvalidOperationException("ProtectedOrUnknown");
        if ((style & 0x800) != 0 || (style & 0x08000000) != 0) throw new InvalidOperationException("ReadOnlyOrUnknown");
        if (IsComposing(hwnd)) throw new InvalidOperationException("Composing");
        if (Native.SendMessageTimeout(hwnd, 0xE6, 0, 0, 2, 200, out nuint password) == 0) throw new InvalidOperationException("ContextUnavailable");
        if (password != 0) throw new InvalidOperationException("ProtectedOrUnknown");
        if (Native.SendMessageTimeout(hwnd, 0xE, 0, 0, 2, 200, out nuint length) == 0 || length > 1024 * 1024) throw new InvalidOperationException("ContextUnavailable");
        int start = 0, end = 0;
        if (Native.SendSelectionMessageTimeout(hwnd, 0xB0, ref start, ref end, 2, 200, out _) == 0) throw new InvalidOperationException("ContextUnavailable");
        if (start != end) throw new InvalidOperationException("SelectionNotEmpty");
        var buffer = new StringBuilder((int)length + 1);
        if (Native.SendTextMessageTimeout(hwnd, 0xD, buffer.Capacity, buffer, 2, 200, out _) == 0) throw new InvalidOperationException("ContextUnavailable");
        string text = buffer.ToString();
        // RichEdit may expose CRLF in WM_GETTEXT while selection uses single CR.
        // Keep RichEdit on its UIA text provider where these offsets differ.
        if (rich && text.Contains("\r\n")) return null;
        int verifyStart = 0, verifyEnd = 0;
        if (Native.SendSelectionMessageTimeout(hwnd, 0xB0, ref verifyStart, ref verifyEnd, 2, 200, out _) == 0 ||
            start != verifyStart || end != verifyEnd || start < 0 || start > text.Length || Native.GetForegroundWindow() != foreground || FocusWindow(thread) != hwnd)
            throw new InvalidOperationException("TargetChanged");
        return new(hwnd, start, Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(text))), TextPolicy.Tail(text[..start], 300), TextPolicy.Head(text[start..], 300));
    }
}
