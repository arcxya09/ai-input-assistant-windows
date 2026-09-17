using System.Net;
using System.Text;
using System.Text.Json;
using AiInput.Core;
using AiInput.DeepSeek;

internal static class CompletionTests
{
    internal static async Task<int> Run()
    {
        int count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
        void Reject(string payload,string code,string name)
        {
            try{CompletionOutput.Parse(new(payload,"stop"));throw new Exception("Failure text escaped parser");}
            catch(ProviderException e){Check(e.Code==code,name);}
        }
        string Json(string status,string text)=>JsonSerializer.Serialize(new{status,text});
        Check(CompletionOutput.Parse(new(Json("ok"," world "),"stop")).Text==" world ","structured success extracts only continuation and preserves boundary spaces");
        Reject(Json("insufficient_context","抱歉，需要更多上下文。"),"InsufficientContext","insufficient context never exposes model explanation");
        Reject(Json("cannot_continue","模型的失败说明，不应展示或复制。"),"CannotContinue","failed continuation never becomes suggestion text");
        Reject("抱歉，我无法根据这些内容续写。","InvalidCompletionFormat","plain model reply rejected instead of displayed");
        Reject("```json\n"+Json("ok","正文。")+"\n```","InvalidCompletionFormat","wrapped model output rejected");
        Reject("{\"status\":\"ok\",\"text\":\"未完成", "InvalidCompletionFormat","broken JSON rejected");
        Reject(Json("unknown","正文。"),"InvalidCompletionFormat","unknown model status rejected");
        Check(CompletionOutput.Parse(new("{\"status\":\"ok\",\"text\":\"正文。\",\"reason\":\"不得展示\"}","stop")).Text=="正文。","extra metadata ignored without exposing it");
        Reject("{\"status\":\"cannot_continue\",\"status\":\"ok\",\"text\":\"错误正文\"}","InvalidCompletionFormat","duplicate status rejected");
        Reject("{\"status\":\"ok\",\"text\":\"正文\",\"text\":\"错误正文\"}","InvalidCompletionFormat","duplicate text rejected");
        Reject(Json("ok",""),"NoContinuation","empty successful result becomes status");
        Reject(Json("ok","抱歉，我无法续写这段内容。"),"CannotContinue","apology mislabeled as success rejected");
        Reject(Json("ok","As an AI, I cannot continue this text."),"CannotContinue","English refusal mislabeled as success rejected");
        Check(!TextPolicy.IsMetaResponse("这个问题暂时无法解决，我们将继续研究。")&&!TextPolicy.IsMetaResponse("抱歉让你久等了，会议现在开始。"),"ordinary prose about difficulty or apologies remains valid");
        string paragraph=string.Concat(Enumerable.Repeat("这是一段完整的新增内容。",90));
        Check(CompletionOutput.Parse(new(Json("ok",paragraph),"stop")).Text==paragraph,"structured full paragraph remains intact");
        using(var client=new CompletionClient(new ModelHandler(Json("ok","新增正文。"))))
            Check((await client.GenerateAsync("test-placeholder",new(){Ok=true,Before="前文"},null,default)).Text=="新增正文。","HTTP and SSE pipeline exposes only validated text");
        using(var client=new CompletionClient(new ModelHandler(Json("cannot_continue","不要显示这段模型回复。"))))
        {
            var gate=new GenerationGate();
            try{var result=await client.GenerateAsync("test-placeholder",new(){Ok=true,SelectedText="选区"},null,default);gate.Offer(gate.Revision,result.Text);throw new Exception("Failure offered");}
            catch(ProviderException e){Check(e.Code=="CannotContinue"&&gate.Suggestion==null,"model failure never reaches suggestion acceptance pipeline");}
        }
        foreach(string calls in new[]{"null","[]","[{\"index\":0,\"function\":{\"name\":\"unexpected\"}}]","{}"})
        {
            string payload=Json("ok","正常完整的续写。");
            string frame(string delta,string finish="null")=>"data: {\"error\":null,\"choices\":[{\"index\":0,\"delta\":"+delta+",\"finish_reason\":"+finish+"}]}\n\n";
            string sse=frame("{\"role\":\"assistant\",\"content\":null,\"tool_calls\":"+calls+"}")+
                string.Concat(payload.Chunk(5).Select(part=>frame(JsonSerializer.Serialize(new{content=new string(part),tool_calls=(object?)null}))))+
                frame("null","\"stop\"")+"data: {\"choices\":[],\"usage\":{}}\n\ndata: [DONE]\n\n";
            using var stream=new MemoryStream(Encoding.UTF8.GetBytes(sse));
            try
            {
                var result=CompletionOutput.Parse(await CompletionClient.ReadSseAsync(stream,default));
                Check((calls=="null"||calls=="[]")&&result.Text=="正常完整的续写。","fragmented SSE accepts absent tool calls: "+calls);
            }
            catch(ProviderException e){Check(calls!="null"&&calls!="[]"&&e.Code=="UnexpectedToolCall","actual or malformed tool call remains rejected");}
        }
        return count;
    }
    sealed class ModelHandler(string payload):HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if(body.RootElement.GetProperty("response_format").GetProperty("type").GetString()!="json_object")throw new Exception("Missing JSON output mode");
            var sse="data: "+JsonSerializer.Serialize(new{choices=new[]{new{index=0,delta=new{content=payload},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";
            return new(HttpStatusCode.OK){Content=new StringContent(sse,Encoding.UTF8,"text/event-stream")};
        }
    }
}
