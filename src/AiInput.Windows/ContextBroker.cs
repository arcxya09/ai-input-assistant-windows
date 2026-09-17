using System.Diagnostics;
using System.IO.Pipes;
using AiInput.Core;
namespace AiInput.Windows;

public sealed class ContextBroker(string? appRoot = null) : IDisposable
{
    NamedPipeServerStream? pipe;
    Process? process;
    readonly SemaphoreSlim mutex=new(1,1);
    readonly Queue<DateTime> failures=new();
    public async Task<RpcReply> CallAsync(RpcRequest request,CancellationToken ct)
    {
        await mutex.WaitAsync(ct);
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(pipe==null?8:2));
            if(pipe==null)
            {
                string name="AiInput-"+Guid.NewGuid().ToString("N");
                pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                string exe=Path.Combine(appRoot ?? AppContext.BaseDirectory,"ContextHost","AiInput.ContextHost.exe");
                var start=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};
                start.ArgumentList.Add(name);start.ArgumentList.Add(Environment.ProcessId.ToString());
                process=Process.Start(start)??throw new IOException("HostStartFailed");
                await pipe.WaitForConnectionAsync(timeout.Token);
            }
            await Frames.WriteAsync(pipe,request,timeout.Token);
            return await Frames.ReadAsync<RpcReply>(pipe,timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Reset();
            throw;
        }
        catch
        {
            Reset();
            failures.Enqueue(DateTime.UtcNow);
            while(failures.TryPeek(out var time)&&time<DateTime.UtcNow.AddMinutes(-1))failures.Dequeue();
            if(failures.Count>=3)throw new InvalidOperationException("ContextHostRepeatedFailure");
            throw;
        }
        finally{mutex.Release();}
    }
    void Reset()
    {
        pipe?.Dispose();pipe=null;
        try{if(process is {HasExited:false})process.Kill(true);}catch{}
        process?.Dispose();process=null;
    }
    public void Dispose()=>Reset();
}
