using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AiInput.Core;

internal static class UpdateTests
{
    internal static async Task<int> Run()
    {
        int count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);count++;}
        async Task Reject(Func<Task> action,string name){bool rejected=false;try{await action();}catch{rejected=true;}Check(rejected,name);}
        byte[] bytes=[77,90,11,22,33,44];
        string hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string Metadata(string tag="v1.2.0",bool preview=false,string? url=null,string? digest=null,long size=6)=>JsonSerializer.Serialize(new
        {
            tag_name=tag,draft=false,prerelease=preview,
            assets=new[]{new{name="AiInputAssistant-"+tag.TrimStart('v')+"-Setup-x64.exe",state="uploaded",size,
                browser_download_url=url??$"https://github.com/{ProductInfo.Repository}/releases/download/{tag}/AiInputAssistant-{tag.TrimStart('v')}-Setup-x64.exe",digest=digest??"sha256:"+hash}}
        });
        var current=new Version(1,1,0);
        Check(UpdateClient.ParseVersion("v1.10.0")>UpdateClient.ParseVersion("v1.9.0"),"update numeric version ordering");
        Check(UpdateClient.ParseVersion("v1.2.0-beta")==null&&UpdateClient.ParseVersion("../1.2.0")==null,"update rejects preview and path-like tags");
        Check(UpdateClient.ParseRelease(Metadata("v1.1.0"),current)==null&&UpdateClient.ParseRelease(Metadata("v1.0.0"),current)==null,"update never downgrades or reinstalls equal version");
        Check(UpdateClient.ParseRelease(Metadata(preview:true),current)==null,"update ignores prereleases");
        await Reject(()=>Task.FromResult(UpdateClient.ParseRelease(Metadata(url:"https://example.com/setup.exe"),current)),"update rejects installer outside owned release");
        await Reject(()=>Task.FromResult(UpdateClient.ParseRelease(Metadata(digest:"sha256:bad"),current)),"update requires full checksum");
        await Reject(()=>Task.FromResult(UpdateClient.ParseRelease(Metadata(size:UpdateClient.MaxInstallerBytes+1),current)),"update enforces size bound");
        using(var http=new HttpClient(new Handler((request,ct)=>
        {
            Check(request.RequestUri!.AbsolutePath.EndsWith("/releases/latest")&&request.Headers.UserAgent.Count>0&&request.Headers.Authorization==null,"update metadata request isolated from API key");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Metadata())});
        })))
            Check((await new UpdateClient(http).CheckAsync(current,default))?.Version==new Version(1,2,0),"update finds latest release");
        using(var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)))))
            await Reject(()=>new UpdateClient(http).CheckAsync(current,default),"update rate limiting is recoverable error");
        string directory=Path.Combine(Path.GetTempPath(),"AiInput-update-test-"+Guid.NewGuid().ToString("N"));
        var release=UpdateClient.ParseRelease(Metadata(),current)!;
        try
        {
            int requests=0;
            using(var http=new HttpClient(new Handler((_,_)=>{requests++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});})))
            {
                var client=new UpdateClient(http);
                string file=await client.DownloadAsync(release,directory,null,default);
                Check(await UpdateClient.VerifyFileAsync(file,bytes.Length,hash,default),"update downloads verified package");
                await client.DownloadAsync(release,directory,null,default);
                Check(requests==1,"update reuses verified cached installer");
                File.WriteAllBytes(file,[1,2,3,4,5,6]);
                await client.DownloadAsync(release,directory,null,default);
                Check(requests==2&&await UpdateClient.VerifyFileAsync(file,6,hash,default),"update replaces corrupted cache");
                File.Delete(file);
            }
            using(var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent([1,2,3,4,5,6])}))))
                await Reject(()=>new UpdateClient(http).DownloadAsync(release,directory,null,default),"update rejects tampered download");
            Check(Directory.GetFiles(directory).Length==0,"update removes partial file after checksum failure");
            using(var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent([1,2])}))))
                await Reject(()=>new UpdateClient(http).DownloadAsync(release,directory,null,default),"update rejects truncated download");
            using(var cts=new CancellationTokenSource(150))
            using(var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new WaitingStream())}))))
                await Reject(()=>new UpdateClient(http).DownloadAsync(release,directory,null,cts.Token),"update cancels streaming download");
            Check(Directory.GetFiles(directory).Length==0,"update cancellation leaves no executable or partial file");
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
        return count;
    }
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken);}
    sealed class WaitingStream:Stream
    {
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default){await Task.Delay(Timeout.Infinite,cancellationToken);return 0;}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override void Flush(){}public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
