using System.Text;
using System.Text.Json;
using AiInput.Core;
using AiInput.DeepSeek;

internal static class ThinkingTests
{
    internal static async Task<int> Run()
    {
        int count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
        foreach(string depth in new[]{ThinkingOptions.Auto,ThinkingOptions.Max,ThinkingOptions.None,"invalid"})
        {
            using var content=new OneShotContent(new(){Ok=true,SelectedText="选区"},null,depth);
            using var body=JsonDocument.Parse(await content.ReadAsStringAsync());
            var root=body.RootElement;
            bool none=depth==ThinkingOptions.None,max=depth==ThinkingOptions.Max;
            Check(root.GetProperty("thinking").GetProperty("type").GetString()==(none?"disabled":"enabled"),"thinking switch: "+depth);
            Check(root.GetProperty("reasoning_effort").GetString()==(none?"none":max?"max":"low"),"provider-compatible reasoning effort: "+depth);
            Check(none?root.GetProperty("max_tokens").GetInt32()==4096:!root.TryGetProperty("max_tokens",out _),"thinking uses provider token budget: "+depth);
        }
        using(var content=new OneShotContent(new(){Before="默认"},null))
        using(var body=JsonDocument.Parse(await content.ReadAsStringAsync()))
            Check(body.RootElement.GetProperty("thinking").GetProperty("type").GetString()=="enabled"&&body.RootElement.GetProperty("reasoning_effort").GetString()=="low","new requests default to lightweight thinking");
        using(var content=new OneShotContent(new(){Before="截图"},[1,2,3]))
        using(var body=JsonDocument.Parse(await content.ReadAsStringAsync()))
            Check(body.RootElement.GetProperty("reasoning_effort").GetString()=="high","automatic image interpretation uses high effort");
        using(var content=new OneShotContent(new(){SelectedText=new string('选',8192)},[1,2,3]))
        using(var body=JsonDocument.Parse(await content.ReadAsStringAsync()))
        {
            Check(body.RootElement.GetProperty("reasoning_effort").GetString()=="low","long selection stays lightweight and excludes image effort");
            using var input=JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            Check(input.RootElement.GetProperty("selection").GetString()!.Length==8192,"long selection is not shortened to improve latency");
        }
        string reasoning="data: {\"choices\":[{\"index\":0,\"delta\":{\"reasoning_content\":\"hidden reasoning must never enter suggestions\",\"content\":null,\"tool_calls\":null}}]}\n\n";
        string answer="data: "+JsonSerializer.Serialize(new{choices=new[]{new{index=0,delta=new{content=JsonSerializer.Serialize(new{status="ok",text="最终完整续写。"})},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(reasoning,16000))+answer)))
        {
            Check(stream.Length>2*1024*1024,"reasoning fixture exceeds old streaming limit");
            Check(CompletionOutput.Parse(await CompletionClient.ReadSseAsync(stream,default)).Text=="最终完整续写。","long reasoning stream discarded while final JSON remains intact");
        }
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(reasoning+"data: [DONE]\n\n")))
        {
            try{await CompletionClient.ReadSseAsync(stream,default);throw new Exception("Incomplete thinking accepted");}
            catch(ProviderException e){Check(e.Code=="IncompleteResponse","interrupted thinking never becomes a suggestion");}
        }
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(reasoning+answer)))
        {
            try{await CompletionClient.ReadSseAsync(stream,new CancellationToken(true));throw new Exception("Cancelled thinking accepted");}
            catch(OperationCanceledException){Check(true,"thinking remains immediately cancellable");}
        }
        string paragraph=string.Concat(Enumerable.Repeat("新增内容继续展开当前主题，并用完整的句子将意思表达清楚。",80));
        string payload=JsonSerializer.Serialize(new{status="ok",text=paragraph});
        string final="data: "+JsonSerializer.Serialize(new{choices=new[]{new{index=0,delta=new{content=payload},finish_reason="stop"}},
            usage=new{prompt_tokens=5000,completion_tokens=2300,completion_tokens_details=new{reasoning_tokens=300}}})+"\n\ndata: [DONE]\n\n";
        var phases=new List<CompletionPhase>();
        var trace=new CompletionTrace(new InlineProgress(p=>phases.Add(p.Phase)));
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(reasoning+final)))
        {
            var result=CompletionOutput.Parse(await CompletionClient.ReadSseAsync(stream,default,trace));
            Check(result.Text==paragraph,"long final paragraph survives thinking stream without cropping");
            Check(TextPolicy.Accept(result,new string('原',600),"")==paragraph,"long context cannot shorten accepted paragraph");
            var stats=trace.Snapshot();
            Check(phases.SequenceEqual(new[]{CompletionPhase.Thinking,CompletionPhase.Writing}),"thinking and final-writing phases are distinct");
            Check(stats.PromptTokens==5000&&stats.CompletionTokens==2300&&stats.ReasoningTokens==300&&stats.FirstContentMs!=null&&stats.FinishReason=="stop",
                "last content frame usage and completion reason recorded");
            Check(stats.ReasoningChars>0&&stats.OutputChars==payload.Length&&!JsonSerializer.Serialize(stats).Contains(paragraph),"diagnostics count output without storing text");
        }
        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(final.Replace("\"stop\"","\"length\""))))
        {
            try{await CompletionClient.ReadSseAsync(stream,default);throw new Exception("Token-limited output accepted");}
            catch(ProviderException e){Check(e.Code=="OutputLimitReached","token limit distinguished from a short successful completion");}
        }
        return count;
    }
    sealed class InlineProgress(Action<CompletionProgress> action):IProgress<CompletionProgress>
    {public void Report(CompletionProgress value)=>action(value);}
}
