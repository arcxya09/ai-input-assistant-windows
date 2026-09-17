using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using AiInput.Core;
using AiInput.Windows;
using Interop.UIAutomationClient;

namespace AiInput.Windows.Tests;
internal static class ObsidianTests
{
    internal static async Task Run(ContextBroker broker)
    {
        string exe=Environment.GetEnvironmentVariable("OBSIDIAN_TEST_EXE")??throw new Exception("Official Obsidian fixture required");
        foreach(bool live in new[]{true,false})await RunMode(broker,exe,live);
    }
    static async Task RunMode(ContextBroker broker,string exe,bool live)
    {
        string mode=live?"Live Preview":"Source mode";
        string root=Path.Combine(Path.GetTempPath(),"AiInput-Obsidian-"+Guid.NewGuid().ToString("N"));
        string vault=Path.Combine(root,"vault"),data=Path.Combine(root,"profile");
        Directory.CreateDirectory(Path.Combine(vault,".obsidian"));Directory.CreateDirectory(data);
        string note=Path.Combine(vault,"Fixture.md");
        File.WriteAllText(note,"前文后文");
        File.WriteAllText(Path.Combine(data,"obsidian.json"),JsonSerializer.Serialize(new{vaults=new Dictionary<string,object>{{"a1b2c3d4e5f67890",new{path=vault,ts=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),open=true}}}}));
        File.WriteAllText(Path.Combine(vault,".obsidian","app.json"),JsonSerializer.Serialize(new{livePreview=live,defaultViewMode="source"}));
        File.WriteAllText(Path.Combine(vault,".obsidian","workspace.json"),JsonSerializer.Serialize(new
        {
            main=new{id="root",type="split",children=new[]{new{id="editor",type="leaf",state=new{type="markdown",state=new{file="Fixture.md",mode="source",source=!live}}}},direction="vertical"},active="editor",lastOpenFiles=new[]{"Fixture.md"}
        }));
        var start=new ProcessStartInfo(exe){UseShellExecute=false};
        start.ArgumentList.Add("--user-data-dir="+data);
        start.ArgumentList.Add("--disable-gpu");
        start.ArgumentList.Add("obsidian://open?path="+Uri.EscapeDataString(note));
        using var process=Process.Start(start)!;
        try
        {
            for(int i=0;i<100;i++){process.Refresh();if(process.MainWindowHandle!=0)break;await Task.Delay(200);}
            if(process.MainWindowHandle==0)throw new Exception("Obsidian window not initialized");
            Native.SetForegroundWindow(process.MainWindowHandle);await Task.Delay(2500);
            // Open an existing test note through Obsidian's normal quick switcher.
            Chord(0x11,0x4F);await Task.Delay(400);
            Native.Inject("Fixture",process.MainWindowHandle);Key(0x0D);await Task.Delay(800);Key(0x1B);
            ContextSnapshot? context=null;string last="";
            for(int i=0;i<12;i++)
            {
                Native.SetForegroundWindow(process.MainWindowHandle);
                Chord(0x11,0x24);Key(0x27);Key(0x27);await Task.Delay(200);
                var capture=await broker.CallAsync(new("capture"),default);last=capture.Code;
                if(capture.Ok&&capture.Snapshot is {} value&&value.Before=="前文"&&value.After.TrimEnd('\r','\n')=="后文"){context=value;break;}
                await Task.Delay(300);
            }
            if(context==null)
            {
                IUIAutomation automation=new CUIAutomation();var focus=automation.GetFocusedElement();
                throw new Exception("Obsidian "+mode+" caret "+last+" focusType="+focus?.CurrentControlType+" class="+focus?.CurrentClassName+" name="+focus?.CurrentName);
            }
            var insertion=await broker.CallAsync(new("insert",context.Token,"新增"),default);
            if(!insertion.Ok)throw new Exception("Obsidian "+mode+" insert: "+insertion.Code);
            for(int i=0;i<30&&File.ReadAllText(note)!="前文新增后文";i++)await Task.Delay(100);
            if(File.ReadAllText(note)!="前文新增后文")throw new Exception("Obsidian saved note differs after insertion");
            Console.WriteLine("PASS real Obsidian 1.13.7 "+mode+" caret, insertion and saved Markdown");
            Chord(0x11,0x24);Key(0x27);Key(0x27);
            Chord(0x10,0x27);Chord(0x10,0x27);await Task.Delay(300);
            var selection=await broker.CallAsync(new("capture"),default);
            if(!selection.Ok||selection.Snapshot==null)throw new Exception("Obsidian "+mode+" selection: "+selection.Code);
            await SelectionTests.Verify(broker,selection.Snapshot,"新增");
            if(File.ReadAllText(note)!="前文新增后文")throw new Exception("Obsidian source changed while copying");
            Console.WriteLine("PASS real Obsidian "+mode+" selection-only context, clipboard output and unchanged Markdown");
            // Exercise a realistic note spanning paragraphs in both selection directions.
            string extra="\n\n提高效率需要先明确目标，再将复杂的任务拆分为可以逐步完成的小步骤。\n\n在安排计划时，我们还需要考虑实际情况，并为后续调整留出空间。";
            Chord(0x11,0x23);
            foreach(string paragraph in extra.Split('\n').Skip(1)){Key(0x0D);if(paragraph.Length>0)Native.Inject(paragraph,process.MainWindowHandle);}
            await Task.Delay(500);
            string full="前文新增后文"+extra;
            for(int i=0;i<30&&File.ReadAllText(note).Replace("\r\n","\n")!=full;i++)await Task.Delay(100);
            if(File.ReadAllText(note).Replace("\r\n","\n")!=full)throw new Exception("Multi-paragraph fixture was not saved");
            foreach(bool backward in new[]{false,true})
            {
                Chord(0x11,backward?(ushort)0x23:(ushort)0x24);
                Send((0x11,false),(0x10,false),(backward?(ushort)0x24:(ushort)0x23,false),
                    (backward?(ushort)0x24:(ushort)0x23,true),(0x10,true),(0x11,true));
                await Task.Delay(300);
                var multi=await broker.CallAsync(new("capture"),default);
                if(!multi.Ok||multi.Snapshot==null)throw new Exception("Obsidian multi-paragraph "+mode+" "+backward+": "+multi.Code);
                if(multi.Snapshot.SelectedText.Replace("\r\n","\n").TrimEnd('\n')!=full)
                    throw new Exception("Obsidian multi-paragraph selection incomplete: chars="+multi.Snapshot.SelectedText.Length);
                await SelectionTests.Verify(broker,multi.Snapshot,multi.Snapshot.SelectedText);
                if(File.ReadAllText(note).Replace("\r\n","\n")!=full)throw new Exception("Multi-paragraph copy changed Markdown");
                Console.WriteLine("PASS real Obsidian "+mode+" multi-paragraph "+(backward?"backward":"forward")+" selection and clipboard");
            }
        }
        finally{if(!process.HasExited)process.Kill(true);await broker.CallAsync(new("clear"),default);}
    }
    static void Key(ushort key)=>Send((key,false),(key,true));
    static void Chord(ushort modifier,ushort key)=>Send((modifier,false),(key,false),(key,true),(modifier,true));
    static void Send(params (ushort Key,bool Up)[] keys)
    {
        var input=keys.Select(k=>new Native.INPUT{Type=1,U=new Native.INPUTUNION{Key=new Native.KEYBD{Vk=k.Key,Flags=k.Up?2u:0u,Extra=Native.InjectionTag}}}).ToArray();
        if(Native.SendInput((uint)input.Length,input,Marshal.SizeOf<Native.INPUT>())!=input.Length)throw new Exception("Obsidian test keystroke failed");
    }
}
