using System.Text;
using System.Text.Json;
using AiInput.Core;
using AiInput.DeepSeek;
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
Check(TextPolicy.Accept(new(new string('甲',90),"stop"),"","")==null,"no hard cut");
Check(TextPolicy.Accept(new("第一句。"+new string('甲',70),"stop"),"","")==null,"too short cut rejected");
var original=new ContextSnapshot{Ok=true,Window=1,Process=2,Before=new string('前',300),After="后文"};
var inserted=original with{Before=new string('前',298)+"新字"};
Check(TextPolicy.InsertionMatches(original,inserted,"新字"),"bounded insertion verified");
Check(!TextPolicy.InsertionMatches(original,inserted with{Window=5},"新字"),"wrong window rejected");
string sse=": keepalive\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"中文\"},\"finish_reason\":null}]}\n\ndata: {\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n";
using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(sse)))
    Check((await CompletionClient.ReadSseAsync(stream,default)).Text=="中文","SSE keepalive and completion");
await Reject(async()=>{using var stream=new MemoryStream(Encoding.UTF8.GetBytes(sse.Replace("data: [DONE]\n\n","")));await CompletionClient.ReadSseAsync(stream,default);},"interrupted SSE rejected");
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
    Check(json.GetProperty("thinking").GetProperty("type").GetString()=="disabled","thinking disabled");
    var uri=json.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString();
    Check(uri=="data:image/png;base64,AQIDBAU=","single inline image serialization");
    Check(image.All(b=>b==0),"image cleared after serialization");
    await Reject(async()=>{using var second=new MemoryStream();await content.CopyToAsync(second);},"image request cannot replay");
}
byte[] cancelled=[7,8,9];new OneShotContent(original,cancelled).Dispose();
Check(cancelled.All(b=>b==0),"image cleared on cancellation before upload");
count+=await UpdateTests.Run();
Console.WriteLine("TOTAL "+count+" PASSED");
