using System.Diagnostics;
using System.Runtime.InteropServices;
using AiInput.Core;
using AiInput.Windows;
using System.Windows.Forms;
namespace AiInput.Windows.Tests;
static class Program
{
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")]static extern nint LoadKeyboardLayout(string id,uint flags);
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Length>0&&args[0]=="--target")
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            LoadKeyboardLayout("00000409",1);
            var form=new Form{Text="AI Input synthetic integration target",Width=500,Height=200,TopMost=true};
            var edit=new TextBox{Text="前文后文",Multiline=true,Dock=DockStyle.Fill};
            if(args.Length>1&&args[1]=="password"){edit.Multiline=false;edit.UseSystemPasswordChar=true;}
            form.Controls.Add(edit);
            form.Shown+=(_,_)=>{edit.Focus();edit.SelectionStart=2;edit.SelectionLength=0;};
            Application.Run(form);return 0;
        }
        try{return Run(args[0]).GetAwaiter().GetResult();}
        catch(Exception e){Console.WriteLine("FAIL "+e.GetType().Name+" "+e.Message);return 1;}
    }
    static async Task<int> Run(string appRoot)
    {
        using var broker=new ContextBroker(appRoot);
        async Task<Process> Target(string kind)
        {
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false};
            info.ArgumentList.Add("--target");info.ArgumentList.Add(kind);
            var target=Process.Start(info)!;
            for(int i=0;i<50;i++){target.Refresh();if(target.MainWindowHandle!=0)break;await Task.Delay(100);}
            if(target.MainWindowHandle==0)throw new Exception("Synthetic target did not initialize");
            SetForegroundWindow(target.MainWindowHandle);
            await Task.Delay(300);
            return target;
        }
        using(var target=await Target("text"))
        {
            try
            {
                var capture=await broker.CallAsync(new("capture"),default);
                if(!capture.Ok||capture.Snapshot is not {Before:"前文",After:"后文"} context)
                    throw new Exception("ContextCapture "+capture.Code);
                Console.WriteLine("PASS native Edit caret and before/after context");
                var insert=await broker.CallAsync(new("insert",context.Token,"新增"),default);
                if(!insert.Ok)throw new Exception("Insert "+insert.Code);
                Console.WriteLine("PASS Unicode insertion verified through independent UIA reread");
                var again=await broker.CallAsync(new("insert",context.Token,"新增"),default);
                if(again.Ok)throw new Exception("Duplicate token accepted");
                Console.WriteLine("PASS duplicate insertion token rejected");
                var read=await broker.CallAsync(new("capture"),default);
                if(read.Snapshot is not {Before:"前文新增",After:"后文"})throw new Exception("Insertion corrupted surrounding text");
                Console.WriteLine("PASS surrounding text preserved");
            }
            finally{target.Kill(true);}
        }
        using(var password=await Target("password"))
        {
            try
            {
                var result=await broker.CallAsync(new("capture"),default);
                if(result.Ok||result.Snapshot!=null)throw new Exception("Password context read");
                Console.WriteLine("PASS password target rejected without text snapshot");
            }
            finally{password.Kill(true);}
        }
        return 0;
    }
}
