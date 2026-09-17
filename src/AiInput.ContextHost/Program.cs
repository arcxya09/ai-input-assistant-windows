using System.Diagnostics;
using System.IO.Pipes;
using AiInput.Core;
namespace AiInput.ContextHost;
internal static class Program
{
    [MTAThread]
    static int Main(string[] args)
    {
        if(args.Length!=2||!int.TryParse(args[1],out int parentId))return 2;
        try
        {
            var parent=Process.GetProcessById(parentId);
            _=Task.Run(async()=>{try{await parent.WaitForExitAsync();}finally{Environment.Exit(0);}});
            using var pipe=new NamedPipeClientStream(".",args[0],PipeDirection.InOut,PipeOptions.Asynchronous);
            pipe.Connect(8000);
            using var reader=new ContextReader(parentId);
            while(true)
            {
                var request=Frames.ReadAsync<RpcRequest>(pipe,CancellationToken.None).GetAwaiter().GetResult();
                RpcReply reply;
                try{reply=reader.Handle(request);}catch{reader.Clear();reply=new(false,"ContextUnavailable");}
                Frames.WriteAsync(pipe,reply,CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        catch{return 1;}
    }
}
