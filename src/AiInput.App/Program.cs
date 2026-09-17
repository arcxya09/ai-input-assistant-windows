using Microsoft.UI.Dispatching;
using AiInput.Windows;
namespace AiInput.App;

internal static class Program
{
    internal static string? SmokePath;
    [STAThread]
    static void Main(string[] args)
    {
        using var instance=new Mutex(true,"Local\\AiInputAssistant.v1",out bool first);
        if(!first)return;
        if(args.Length==2&&args[0]=="--smoke-ui")SmokePath=args[1];
        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Microsoft.UI.Xaml.Application.Start(initialization=>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _=new App();
            });
        }
        catch(Exception error)
        {
            LocalStore.Log("StartupFailed",error);
            if(SmokePath!=null)File.WriteAllText(SmokePath,"FAILED "+error.GetType().Name+" "+error.HResult);
            Environment.ExitCode=1;
        }
    }
}
