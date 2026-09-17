using System.Globalization;

namespace AiInput.Core;
public static class TextPolicy
{
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
        string text = result.Text.Trim();
        if (text.Length == 0 || text.Any(char.IsControl) || text.Contains("```") ||
            text.StartsWith('#') || text.StartsWith("建议：") || text.StartsWith("续写：")) return null;
        if (before.EndsWith(text, StringComparison.Ordinal) ||
            after.StartsWith(text, StringComparison.Ordinal)) return null;
        var offsets = StringInfo.ParseCombiningCharacters(text);
        if (offsets.Length > 60)
        {
            var head = Head(text, 60);
            int end = head.LastIndexOfAny(['。', '！', '？', '；', '，', ';']);
            if (end < 5) return null;
            text = head[..(end+1)];
        }
        return text;
    }
    public static bool InsertionMatches(ContextSnapshot old, ContextSnapshot current, string inserted) =>
        current.Ok && old.Window == current.Window && old.Process == current.Process &&
        current.Before == Tail(old.Before + inserted, 300) && current.After == old.After;
}
