using System.Diagnostics;
using System.Runtime.InteropServices;
using AiInput.Windows;
using Interop.UIAutomationClient;

namespace AiInput.Windows.Tests;
internal static class BrowserTests
{
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(nint hwnd);
    internal static async Task Run(ContextBroker broker)
    {
        string edge=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft","Edge","Application","msedge.exe");
        if(!File.Exists(edge))throw new Exception("Edge is required for browser integration tests");
        string root=Path.Combine(Path.GetTempPath(),"AiInput-browser-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string html=Path.Combine(root,"fixture.html");
        File.WriteAllText(html,"""
            <!doctype html><html lang="zh"><meta charset="utf-8"><title>AI Input compatibility fixture</title>
            <style>body{padding:40px;font:24px sans-serif}input,textarea,div{display:block;margin:20px;padding:12px;border:1px solid #888}</style>
            <input aria-label="Plain text" value="前文后文" onfocus="this.setSelectionRange(2,2)">
            <textarea aria-label="Multiline text" onfocus="this.setSelectionRange(2,2)">前文后文</textarea>
            <div contenteditable="true" role="textbox" aria-label="Editable document" onfocus="let r=document.createRange();r.setStart(this.firstChild,2);r.collapse(true);let s=getSelection();s.removeAllRanges();s.addRange(r)">前文后文</div>
            <input type="password" aria-label="Password field" value="secret-value">
            </html>
            """);
        var start=new ProcessStartInfo(edge){UseShellExecute=false};
        foreach(string arg in new[]{"--no-first-run","--no-default-browser-check","--force-renderer-accessibility","--user-data-dir="+Path.Combine(root,"profile"),"--app="+new Uri(html).AbsoluteUri})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;
        try
        {
            for(int i=0;i<100;i++){process.Refresh();if(process.MainWindowHandle!=0)break;await Task.Delay(200);}
            if(process.MainWindowHandle==0)throw new Exception("Edge window not found");
            SetForegroundWindow(process.MainWindowHandle);
            IUIAutomation automation=new CUIAutomation();
            var window=automation.ElementFromHandle(process.MainWindowHandle);
            foreach(string name in new[]{"Plain text","Multiline text","Editable document","Password field"})
            {
                IUIAutomationElement? field=null;
                for(int i=0;i<40&&field==null;i++)
                {
                    field=window.FindFirst(TreeScope.TreeScope_Descendants,automation.CreatePropertyCondition(30005,name));
                    if(field==null)await Task.Delay(250);
                }
                if(field==null)throw new Exception("Browser field missing: "+name);
                SetForegroundWindow(process.MainWindowHandle);field.SetFocus();await Task.Delay(500);
                var capture=await broker.CallAsync(new("capture"),default);
                if(name=="Password field")
                {
                    if(capture.Ok||capture.Snapshot!=null)throw new Exception("Browser password exposed");
                    Console.WriteLine("PASS browser password target rejected");continue;
                }
                if(!capture.Ok||capture.Snapshot is not {Before:"前文",After:"后文"} context)
                    throw new Exception("Browser "+name+" context: "+capture.Code);
                var insertion=await broker.CallAsync(new("insert",context.Token,"新增"),default);
                if(!insertion.Ok)throw new Exception("Browser "+name+" insertion: "+insertion.Code);
                Console.WriteLine("PASS browser "+name+" context and verified insertion");
            }
        }
        finally{if(!process.HasExited)process.Kill(true);}
    }
}
