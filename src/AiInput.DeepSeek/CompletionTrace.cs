using System.Diagnostics;
using System.Text.Json;
namespace AiInput.DeepSeek;

public enum CompletionPhase { Waiting,Thinking,Writing }
public sealed record CompletionProgress(CompletionPhase Phase);
public sealed record CompletionStatistics(long ElapsedMs,long? FirstContentMs,long ReasoningChars,long OutputChars,
    string FinishReason,int? PromptTokens,int? CompletionTokens,int? ReasoningTokens,int ResultChars);

// Counts only; no prompt, completion or hidden reasoning is retained here.
public sealed class CompletionTrace(IProgress<CompletionProgress>? progress=null)
{
    readonly Stopwatch clock=Stopwatch.StartNew();
    CompletionPhase phase=CompletionPhase.Waiting;
    long? firstContent;
    long reasoningChars,outputChars;
    int? promptTokens,completionTokens,reasoningTokens;
    string finish="missing";
    public int ResultChars {get;set;}
    internal void ObserveDelta(JsonElement delta)
    {
        if(delta.TryGetProperty("reasoning_content",out var reasoning)&&reasoning.ValueKind==JsonValueKind.String)
        {
            int count=reasoning.GetString()?.Length??0;reasoningChars+=count;
            if(count>0&&phase==CompletionPhase.Waiting)SetPhase(CompletionPhase.Thinking);
        }
        if(delta.TryGetProperty("content",out var content)&&content.ValueKind==JsonValueKind.String)
        {
            int count=content.GetString()?.Length??0;outputChars+=count;
            if(count>0){firstContent??=clock.ElapsedMilliseconds;SetPhase(CompletionPhase.Writing);}
        }
    }
    void SetPhase(CompletionPhase value){if(phase==value)return;phase=value;progress?.Report(new(phase));}
    internal void ObserveFrame(JsonElement root)
    {
        if(!root.TryGetProperty("usage",out var usage)||usage.ValueKind!=JsonValueKind.Object)return;
        promptTokens=Number(usage,"prompt_tokens")??promptTokens;
        completionTokens=Number(usage,"completion_tokens")??completionTokens;
        if(usage.TryGetProperty("completion_tokens_details",out var details)&&details.ValueKind==JsonValueKind.Object)
            reasoningTokens=Number(details,"reasoning_tokens")??reasoningTokens;
    }
    static int? Number(JsonElement value,string name)=>value.TryGetProperty(name,out var token)&&token.ValueKind==JsonValueKind.Number&&token.TryGetInt32(out int n)&&n>=0?n:null;
    internal void Finish(string reason)=>finish=reason switch{"stop"=>"stop","length"=>"length",_=>"other"};
    public CompletionStatistics Snapshot()=>new(clock.ElapsedMilliseconds,firstContent,reasoningChars,outputChars,finish,promptTokens,completionTokens,reasoningTokens,ResultChars);
}
