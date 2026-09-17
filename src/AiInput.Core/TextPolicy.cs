using System.Globalization;

namespace AiInput.Core;
public static class TextPolicy
{
    // Transport/resource safeguard, never a point at which text is cropped.
    public const int MaxInsertionChars = 8192;
    public static string Head(string text, int count) => Slice(text, count, false);
    public static string Tail(string text, int count) => Slice(text, count, true);
    static string Slice(string text, int count, bool tail)
    {
        var offsets = StringInfo.ParseCombiningCharacters(text);
        if (offsets.Length <= count) return text;
        return tail ? text[offsets[offsets.Length-count]..] : text[..offsets[count]];
    }
    public static string? Accept(Completion result, string before, string after)
    {
        if (result.FinishReason != "stop") return null;
        // Preserve spaces that join words at the caret. Only discard outer
        // paragraph separators; never inject Enter into a chat input.
        string text = result.Text.Trim('\r', '\n');
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxInsertionChars || text.Any(char.IsControl) || text.Contains("```") ||
            text.StartsWith('#') || text.StartsWith("建议：") || text.StartsWith("续写：")) return null;
        if (before.EndsWith(text, StringComparison.Ordinal) ||
            after.StartsWith(text, StringComparison.Ordinal)) return null;
        // Reject clear context echoes rather than rewriting or cutting the
        // model's words. Short overlaps may be intentional (e.g. reduplication).
        for (int n = 8; n <= Math.Min(before.Length, text.Length); n++)
            if (before.AsSpan(before.Length-n).SequenceEqual(text.AsSpan(0,n))) return null;
        for (int n = 8; n <= Math.Min(after.Length, text.Length); n++)
            if (after.AsSpan(0,n).SequenceEqual(text.AsSpan(text.Length-n))) return null;
        return text;
    }
    public static bool InsertionMatches(ContextSnapshot old, ContextSnapshot current, string inserted) =>
        current.Ok && old.Window == current.Window && old.Process == current.Process &&
        current.Before == Tail(old.Before + inserted, 300) && current.After == old.After;
}
