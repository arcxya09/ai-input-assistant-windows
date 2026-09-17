namespace AiInput.DeepSeek;

public static class ThinkingOptions
{
    public const string Auto="auto", Max="max", None="none";
    public static string Normalize(string? value)=>value is Max or None?value:Auto;
    // Continuation is a bounded writing task: extra input alone must not trigger
    // deeper reasoning. Images use high effort to interpret visual context.
    public static string Effort(string? value,bool hasImage=false)=>Normalize(value) switch
    {Max=>"max",None=>"none",_=>hasImage?"high":"low"};
    public static string RequestFields(string? value,bool hasImage=false)=>Effort(value,hasImage) switch
    {
        Max=>"\"thinking\":{\"type\":\"enabled\"},\"reasoning_effort\":\"max\",",
        None=>"\"thinking\":{\"type\":\"disabled\"},\"reasoning_effort\":\"none\",\"max_tokens\":4096,",
        var effort=>"\"thinking\":{\"type\":\"enabled\"},\"reasoning_effort\":\""+effort+"\","
    };
    public static TimeSpan Timeout(string? value)=>TimeSpan.FromSeconds(Normalize(value) switch{Max=>900,None=>90,_=>300});
}
