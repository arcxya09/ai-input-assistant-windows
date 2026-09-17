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
            LoadKeyboardLayout(args.Length>1&&args[1]=="cjk"?"00000804":"00000409",1);
            var form=new Form{Text="AI Input synthetic integration target",Width=500,Height=200,TopMost=true};
            string kind=args.Length>1?args[1]:"text";
            TextBoxBase edit=kind=="rich"?new RichTextBox():new TextBox{Multiline=true};
            edit.MaxLength=1100000;
            edit.Text=kind=="empty"?"":kind=="long"?new string('前',70000)+"后文":"前文后文";edit.Dock=DockStyle.Fill;
            if(kind=="password"&&edit is TextBox password){password.Multiline=false;password.UseSystemPasswordChar=true;}
            if(kind is "readonly" or "readonlyselection")edit.ReadOnly=true;
            form.Controls.Add(edit);
            form.Shown+=(_,_)=>{edit.Focus();edit.SelectionStart=kind=="empty"?0:kind=="long"?70000:2;edit.SelectionLength=kind is "selection" or "readonlyselection"?2:0;};
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
                Console.WriteLine("PASS Unicode insertion verified through independent context reread");
                var again=await broker.CallAsync(new("insert",context.Token,"新增"),default);
                if(again.Ok)throw new Exception("Duplicate token accepted");
                Console.WriteLine("PASS duplicate insertion token rejected");
                var read=await broker.CallAsync(new("capture"),default);
                if(read.Snapshot is not {Before:"前文新增",After:"后文"})throw new Exception("Insertion corrupted surrounding text");
                Console.WriteLine("PASS surrounding text preserved");
                string paragraph=string.Concat(Enumerable.Range(1,20).Select(i=>$"第{i}句完整内容用于检查插入没有截断，前后文保持原样。"));
                var longInsert=await broker.CallAsync(new("insert",read.Snapshot.Token,paragraph),default);
                if(!longInsert.Ok)throw new Exception("Paragraph insertion "+longInsert.Code);
                uint thread=Native.GetWindowThreadProcessId(target.MainWindowHandle,out _);
                var info=new Native.GUITHREADINFO{Size=(uint)Marshal.SizeOf<Native.GUITHREADINFO>()};
                if(!Native.GetGUIThreadInfo(thread,ref info))throw new Exception("Missing native focus");
                var actual=new System.Text.StringBuilder(4096);
                if(Native.SendTextMessageTimeout(info.Focus,0x000D,4096,actual,2,1000,out _)==0||actual.ToString()!="前文新增"+paragraph+"后文")
                    throw new Exception("Full paragraph or caret boundaries changed");
                Console.WriteLine("PASS full paragraph >256 characters inserted at caret and independently read in full");
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
        foreach(string kind in new[]{"readonly"})
        {
            using var target=await Target(kind);
            try
            {
                var result=await broker.CallAsync(new("capture"),default);
                if(result.Ok||result.Snapshot!=null)throw new Exception(kind+" target accepted");
                Console.WriteLine("PASS "+kind+" target rejected");
            }
            finally{target.Kill(true);}
        }
        foreach(string kind in new[]{"selection","readonlyselection"})
        {
            using var target=await Target(kind);
            try
            {
                var capture=await broker.CallAsync(new("capture"),default);
                if(!capture.Ok||capture.Snapshot==null)throw new Exception(kind+" capture: "+capture.Code);
                await SelectionTests.Verify(broker,capture.Snapshot,"后文");
                uint thread=Native.GetWindowThreadProcessId(target.MainWindowHandle,out _);
                var info=new Native.GUITHREADINFO{Size=(uint)Marshal.SizeOf<Native.GUITHREADINFO>()};Native.GetGUIThreadInfo(thread,ref info);
                Native.SendMessageTimeout(info.Focus,0xB1,0,2,2,200,out _);
                var stale=await SuggestionActions.AcceptAsync(broker,capture.Snapshot,"STALE",default);
                if(stale.Ok||SelectionTests.ReadClipboard()!="完整续写结果，可以自行粘贴。")throw new Exception("Changed selection copied stale result");
                Console.WriteLine("PASS "+kind+" selection-only snapshot, copy, source preservation and stale selection rejection");
            }
            finally{target.Kill(true);}
        }
        int failures=0;
        foreach(string kind in new[]{"empty","long","rich","cjk"})
        {
            using var target=await Target(kind);
            try
            {
                var result=await broker.CallAsync(new("capture"),default);
                if(!result.Ok||result.Snapshot==null)throw new Exception(kind+" context "+result.Code);
                string before=kind=="empty"?"":kind=="long"?new string('前',300):"前文";
                string after=kind=="empty"?"":"后文";
                if(result.Snapshot.Before!=before||result.Snapshot.After.TrimEnd('\r','\n')!=after)throw new Exception(kind+" wrong synthetic context: "+System.Text.Json.JsonSerializer.Serialize(result.Snapshot));
                var insertion=await broker.CallAsync(new("insert",result.Snapshot.Token,"新增"),default);
                if(!insertion.Ok)throw new Exception(kind+" insertion "+insertion.Code);
                Console.WriteLine("PASS "+kind+" input context and verified insertion");
                if(kind=="cjk")
                {
                    uint thread=Native.GetWindowThreadProcessId(target.MainWindowHandle,out _);
                    if(((long)Native.GetKeyboardLayout(thread)&0x3FF)!=4)Console.WriteLine("SKIP real Chinese layout unavailable on this runner; candidate composition not tested");
                }
            }
            catch(Exception e){Console.WriteLine("FAIL "+e.Message);failures++;}
            finally{target.Kill(true);}
        }
        try{await BrowserTests.Run(broker);}catch(Exception e){Console.WriteLine("FAIL "+e.Message);failures++;}
        try{await ObsidianTests.Run(broker);}catch(Exception e){Console.WriteLine("FAIL "+e.Message);failures++;}
        if(failures>0)
        {
            string log=Path.Combine(LocalStore.Root,"logs","events.log");
            if(File.Exists(log))foreach(string line in File.ReadLines(log).TakeLast(25))Console.WriteLine("DIAGNOSTIC "+line);
            throw new Exception(failures+" compatibility test group(s) failed");
        }
        return 0;
    }
}
