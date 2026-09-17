using AiInput.Core;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AiInput.Windows;

public static class SuggestionActions
{
    public static async Task<RpcReply> AcceptAsync(ContextBroker broker,ContextSnapshot target,string text,CancellationToken ct)
    {
        if(!target.IsSelection)return await broker.CallAsync(new("insert",target.Token,text),ct);
        var valid=await broker.CallAsync(new("probe",target.Token),ct);
        if(!valid.Ok)return new(false,"InsertRejected");
        ct.ThrowIfCancellationRequested();
        return await CopyAsync(text,(nint)target.Window,ct)?new(true,"Copied"):new(false,"ClipboardBusy");
    }
    static Task<bool> CopyAsync(string text,nint target,CancellationToken ct)
    {
        var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try
            {
                for(int i=0;i<5;i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if(Native.GetForegroundWindow()!=target||!Native.KeysReleased()){done.TrySetResult(false);return;}
                    try{Clipboard.SetDataObject(text,true,0,0);done.TrySetResult(true);return;}
                    catch(ExternalException){Thread.Sleep(60);}
                }
                done.TrySetResult(false);
            }
            catch(OperationCanceledException){done.TrySetCanceled(ct);}
            catch(Exception e){done.TrySetException(e);}
        }){IsBackground=true,Name="AI Input clipboard"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        return done.Task;
    }
}
