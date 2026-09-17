using System.Text;
using System.Text.Json;
using AiInput.Core;
using AiInput.DeepSeek;
if(args is ["--github-update-smoke"])
{
    using var http=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};
    var updates=new UpdateClient(http);
    var release=await updates.CheckAsync(new Version(0,0,0),default)??throw new Exception("No public GitHub release");
    string directory=Path.Combine(Path.GetTempPath(),"AiInput-live-update-"+Guid.NewGuid().ToString("N"));
    try
    {
        string installer=await updates.DownloadAsync(release,directory,null,default);
        if(!await UpdateClient.VerifyFileAsync(installer,release.Size,release.Sha256,default))throw new Exception("Live download verification failed");
        Console.WriteLine("PASS real GitHub metadata, release asset download and SHA-256 verification: "+release.Tag);
    }
    finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    return;
}
int count=0;
void Check(bool condition,string name){if(!condition)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
async Task Reject(Func<Task> action,string name)
{
    bool failed=false;try{await action();}catch{failed=true;}Check(failed,name);
}
var gate=new GenerationGate();
Check(!gate.Enabled,"starts paused");
gate.SetEnabled(true);long old=gate.Revision;gate.Invalidate();
Check(!gate.Offer(old,"旧建议"),"late response rejected");
Check(gate.Offer(gate.Revision,"一句新内容"),"current response accepted");
Check(gate.Consume()=="一句新内容"&&gate.Consume()==null,"accept at most once");
long lockVersion=gate.Revision;gate.SetEnabled(false);
Check(!gate.Offer(lockVersion,"late")&&!gate.Enabled,"lock epoch invalidated");
Check(TextPolicy.Head("👩‍🔬甲乙",1)=="👩‍🔬","grapheme head");
Check(TextPolicy.Tail("甲e\u0301乙",2)=="e\u0301乙","grapheme tail");
Check(TextPolicy.Accept(new("词语","stop"),"前","后")=="词语","short phrase preserved");
Check(TextPolicy.Accept(new("正文","length"),"","")==null,"truncated completion rejected");
Check(TextPolicy.Accept(new("已有文字","stop"),"已有文字","")==null,"prefix repetition rejected");
Check(TextPolicy.Accept(new("下一段","stop"),"","下一段")==null,"suffix repetition rejected");
string paragraph=string.Concat(Enumerable.Range(1,32).Select(i=>$"这是第{i}句，用来验证超过六十字和二百五十六字后仍然完整保留。"));
Check(paragraph.Length>256&&TextPolicy.Accept(new(paragraph,"stop"),"前文","")==paragraph,"complete paragraph preserved without cropping");
Check(TextPolicy.Accept(new(" world ","stop"),"Hello","again.")==" world ","word boundary spaces preserved");
Check(TextPolicy.Accept(new("national","stop"),"inter","ization")=="national","mid-word continuation preserved");
Check(TextPolicy.Accept(new("今天完成测试","stop"),"计划是：","，明天发布。")=="今天完成测试","mid-sentence continuation joins existing suffix");
Check(TextPolicy.Accept(new("已有完整前文内容继续补充。","stop"),"已有完整前文内容","")==null,"partial prefix echo rejected");
Check(TextPolicy.Accept(new("新增内容后面已有完整内容","stop"),"","后面已有完整内容。")==null,"partial suffix echo rejected");
Check(TextPolicy.Accept(new("第一段。\n第二段。","stop"),"","")==null,"embedded Enter rejected for chat inputs");
Check(TextPolicy.Accept(new(new string('甲',TextPolicy.MaxInsertionChars+1),"stop"),"","")==null,"oversize response rejected rather than cropped");
var original=new ContextSnapshot{Ok=true,Window=1,Process=2,Before=new string('前',300),After="后文"};
var inserted=original with{Before=new string('前',298)+"新字"};
Check(TextPolicy.InsertionMatches(original,inserted,"新字"),"bounded insertion verified");
Check(!TextPolicy.InsertionMatches(original,inserted with{Window=5},"新字"),"wrong window rejected");
string sse=": keepalive\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"中文\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(sse)))
    Check((await CompletionClient.ReadSseAsync(stream,default)).Text=="中文","SSE keepalive and completion");
await Reject(async()=>{using var stream=new MemoryStream(Encoding.UTF8.GetBytes(sse.Replace("data: [DONE]\n\n","")));await CompletionClient.ReadSseAsync(stream,default);},"interrupted SSE rejected");
await Reject(async()=>{using var stream=new MemoryStream(Encoding.UTF8.GetBytes(sse.Replace("\"stop\"","\"length\"")));await CompletionClient.ReadSseAsync(stream,default);},"token-limited SSE never accepted");
var streamed=new StringBuilder();
foreach(char c in paragraph)streamed.Append("data: ").Append(JsonSerializer.Serialize(new{choices=new[]{new{index=0,delta=new{content=c.ToString()}}}})).Append("\n\n");
streamed.Append("data: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(streamed.ToString())))
    Check((await CompletionClient.ReadSseAsync(stream,default)).Text==paragraph,"full paragraph streamed without cropping");
using(var memory=new MemoryStream())
{
    string maximum=new string('甲',TextPolicy.MaxInsertionChars-2)+"。 ";
    await Frames.WriteAsync(memory,new RpcRequest("insert","token",maximum),default);memory.Position=0;
    Check((await Frames.ReadAsync<RpcRequest>(memory,default)).Text==maximum,"maximum paragraph fits IPC intact");
}
using(var memory=new MemoryStream())
{
    await Frames.WriteAsync(memory,new RpcRequest("capture"),default);memory.Position=0;
    Check((await Frames.ReadAsync<RpcRequest>(memory,default)).Command=="capture","bounded IPC roundtrip");
}
await Reject(async()=>{using var stream=new MemoryStream(BitConverter.GetBytes(70000));await Frames.ReadAsync<RpcReply>(stream,default);},"oversized IPC rejected");
byte[] image=[1,2,3,4,5];
using(var content=new OneShotContent(original,image))
{
    using var memory=new MemoryStream();await content.CopyToAsync(memory);
    using var doc=JsonDocument.Parse(memory.ToArray());
    var json=doc.RootElement;
    Check(json.GetProperty("model").GetString()=="deepseek-flash","fixed provider model");
    Check(json.GetProperty("max_tokens").GetInt32()==4096,"paragraph generation budget");
    Check(json.GetProperty("thinking").GetProperty("type").GetString()=="disabled","thinking disabled");
    var uri=json.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString();
    Check(uri=="data:image/png;base64,AQIDBAU=","single inline image serialization");
    Check(image.All(b=>b==0),"image cleared after serialization");
    await Reject(async()=>{using var second=new MemoryStream();await content.CopyToAsync(second);},"image request cannot replay");
}
byte[] cancelled=[7,8,9];new OneShotContent(original,cancelled).Dispose();
Check(cancelled.All(b=>b==0),"image cleared on cancellation before upload");
var selectedContext=original with{SelectedText="仅使用这段选中文字。",Before="DO_NOT_SEND_BEFORE",After="DO_NOT_SEND_AFTER"};
byte[] excludedImage=[9,8,7];
using(var selectedContent=new OneShotContent(selectedContext,excludedImage))
{
    using var memory=new MemoryStream();await selectedContent.CopyToAsync(memory);
    using var json=JsonDocument.Parse(memory.ToArray());
    var messages=json.RootElement.GetProperty("messages");
    Check(messages[0].GetProperty("content").GetString()==CompletionClient.SelectionPrompt,"selection uses dedicated continuation prompt");
    using var input=JsonDocument.Parse(messages[1].GetProperty("content").GetString()!);
    Check(input.RootElement.EnumerateObject().Count()==1&&input.RootElement.GetProperty("selection").GetString()==selectedContext.SelectedText,"only selected text sent without before/after context");
    Check(excludedImage.All(b=>b==0)&&!Encoding.UTF8.GetString(memory.ToArray()).Contains("image_url"),"selection mode excludes and clears screenshot");
}
Check(!TextPolicy.InsertionMatches(selectedContext,original,"text"),"selection can never use insertion verification");
using(var memory=new MemoryStream())
{
    var large=original with{Before="",After="",SelectedText=new string('选',TextPolicy.MaxSelectionChars)};
    await Frames.WriteAsync(memory,new RpcReply(true,"Ready",large),default);memory.Position=0;
    Check((await Frames.ReadAsync<RpcReply>(memory,default)).Snapshot?.SelectedText==large.SelectedText,"full selection fits IPC without truncation");
}
count+=await UpdateTests.Run();
Console.WriteLine("TOTAL "+count+" PASSED");
