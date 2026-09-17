using Microsoft.UI.Xaml;
using AiInput.Windows;
namespace AiInput.App;

public partial class App : Application
{
    Controller? controller;
    public App()
    {
        InitializeComponent();
        UnhandledException+=(_,e)=>LocalStore.Log("Unhandled",e.Exception);
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        controller=new Controller();
        controller.OpenSettings();
        if(Program.SmokePath!=null)
        {
            var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
            timer.Tick+=async(_,_)=>{
                timer.Stop();
                try {await controller.SmokeAsync();File.WriteAllText(Program.SmokePath,"PASS WinUI background hide/restore, adaptive compact capsule, full paragraph preview/scroll, app icon, update controls, tray, hotkeys and ContextHost IPC initialized.");}
                catch(Exception ex){File.WriteAllText(Program.SmokePath,"FAILED "+ex.GetType().Name+" "+ex.Message);Environment.ExitCode=1;}
                controller.Quit();
            };
            timer.Start();
        }
    }
}
