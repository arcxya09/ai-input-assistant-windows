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
            Check(none||max?root.GetProperty("reasoning_effort").GetString()==(none?"none":"max"):!root.TryGetProperty("reasoning_effort",out _),"provider-compatible reasoning effort: "+depth);
            Check(none?root.GetProperty("max_tokens").GetInt32()==4096:!root.TryGetProperty("max_tokens",out _),"thinking uses provider token budget: "+depth);
        }
        using(var content=new OneShotContent(new(){Before="默认"},null))
        using(var body=JsonDocument.Parse(await content.ReadAsStringAsync()))
            Check(body.RootElement.GetProperty("thinking").GetProperty("type").GetString()=="enabled"&&!body.RootElement.TryGetProperty("reasoning_effort",out _),"new requests default to automatic thinking");
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
        return count;
    }
}
