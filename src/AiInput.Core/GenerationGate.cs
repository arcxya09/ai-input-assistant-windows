namespace AiInput.Core;

/// <summary>All operations are serialized by the UI dispatcher.</summary>
public sealed class GenerationGate
{
    public long Revision { get; private set; }
    public bool Enabled { get; private set; }
    public string? Suggestion { get; private set; }
    public long Invalidate() { Suggestion = null; return ++Revision; }
    public void SetEnabled(bool enabled) { Enabled = enabled; Invalidate(); }
    public bool IsCurrent(long revision) => revision == Revision;
    public bool Offer(long revision, string text)
    {
        if (!IsCurrent(revision) || string.IsNullOrWhiteSpace(text)) return false;
        Suggestion = text; return true;
    }
    public string? Consume()
    {
        var text = Suggestion; Suggestion = null;
        return text;
    }
}
