using System.Globalization;

namespace AiInput.Core;
public static class TextPolicy
{
    // Transport/resource safeguard, never a point at which text is cropped.
    public const int MaxInsertionChars = 8192;
    public const int MaxSelectionChars = 8192;
    public static string Head(string text, int count) => Slice(text, count, false);
    public static string Tail(string text, int count) => Slice(text, count, true);
    static string Slice(string text, int count, bool tail)
    {
        var offsets = StringInfo.ParseCombiningCharacters(text);
        if (offsets.Length <= count) return text;
        return tail ? text[offsets[offsets.Length-count]..] : text[..offsets[count]];
    }
    public static bool IsMetaResponse(string text)
    {
        string value=text.TrimStart();
        string[] prefixes=["{\"status\"","{\"error\"","无法续写","不能续写","未能续写","无法进行续写","无法继续写作","续写失败","生成失败","上下文不足","缺少上下文","请提供更多","请补充上下文","请提供需要续写","以下是续写","续写如下","作为一个AI","作为一个 AI","作为AI","作为 AI","作为语言模型","作为人工智能","I cannot continue","I can't continue","I cannot assist","I can't assist","As an AI","As a language model","Insufficient context","Please provide more","Please provide the text","Here is the continuation"];
        if(prefixes.Any(p=>value.StartsWith(p,StringComparison.OrdinalIgnoreCase)))return true;
        string[] openings=["抱歉","对不起","很抱歉","非常抱歉","很遗憾","遗憾的是","我无法","我不能","Sorry","I'm sorry","I am sorry","Unfortunately","I don’t have enough","I do not have enough"];
        string[] failureTerms=["续写","上下文","无法帮助","不能帮助","提供更多","context","cannot","can't","unable","continue","assist"];
        return openings.Any(p=>value.StartsWith(p,StringComparison.OrdinalIgnoreCase))&&
            failureTerms.Any(p=>value.Contains(p,StringComparison.OrdinalIgnoreCase));
    }
    public static string? Accept(Completion result, string before, string after)
    {
        if (result.FinishReason != "stop") return null;
        // Preserve spaces that join words at the caret. Only discard outer
        // paragraph separators; never inject Enter into a chat input.
        string text = result.Text.Trim('\r', '\n');
        if (IsMetaResponse(text) || string.IsNullOrWhiteSpace(text) || text.Length > MaxInsertionChars || text.Any(char.IsControl) || text.Contains("```") ||
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
        current.Ok && !old.IsSelection && !current.IsSelection && old.Window == current.Window && old.Process == current.Process &&
        current.Before == Tail(old.Before + inserted, 300) && current.After == old.After;
}
