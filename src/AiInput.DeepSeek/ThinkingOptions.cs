namespace AiInput.DeepSeek;

public static class ThinkingOptions
{
    public const string Auto="auto", Max="max", None="none";
    public static string Normalize(string? value)=>value is Max or None?value:Auto;
    // Auto uses the provider's default reasoning effort (currently high),
    // rather than sending an unsupported reasoning_effort=auto value.
    public static string RequestFields(string? value)=>Normalize(value) switch
    {
        Max=>"\"thinking\":{\"type\":\"enabled\"},\"reasoning_effort\":\"max\",",
        None=>"\"thinking\":{\"type\":\"disabled\"},\"reasoning_effort\":\"none\",\"max_tokens\":4096,",
        _=>"\"thinking\":{\"type\":\"enabled\"},"
    };
    public static TimeSpan Timeout(string? value)=>TimeSpan.FromSeconds(Normalize(value) switch{Max=>900,None=>90,_=>300});
}
