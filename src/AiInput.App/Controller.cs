using AiInput.Core;
using AiInput.DeepSeek;
using AiInput.Windows;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Forms=System.Windows.Forms;
using Drawing=System.Drawing;

namespace AiInput.App;

public sealed class Controller : IDisposable
{
    public Settings Settings {get;}=LocalStore.Load();
    public UpdateCoordinator Updates {get;}
    readonly GenerationGate gate=new();
    readonly CompletionClient client=new();
    readonly ContextBroker broker=new();
    readonly InputMonitor monitor;
    readonly SuggestionWindow overlay;
    readonly Forms.NotifyIcon tray;
    readonly DispatcherTimer timer;
    readonly DispatcherQueue dispatcher=DispatcherQueue.GetForCurrentThread();
    CancellationTokenSource cancel=new();
    ContextSnapshot? context;
    SettingsWindow? window;
    DateTime due=DateTime.MaxValue,cooldown=DateTime.MinValue,lastProbe=DateTime.MinValue;
    bool tickBusy,generating,inserting,disposed;
    nint foreground;
    public string Status {get;private set;}="已暂停";
    public string HotkeyError {get;private set;}="";
    public event Action? Changed;
    public bool Enabled=>gate.Enabled;
    public bool IsQuitting=>disposed;
    public Controller()
    {
        overlay=new(Settings);
        overlay.OpenSettingsRequested+=OpenSettings;
        overlay.ToggleRequested+=Toggle;
        overlay.ExitRequested+=Quit;
        overlay.BoundsSaved+=()=>{try{LocalStore.Save(Settings);}catch(Exception e){LocalStore.Log("SaveSettingsFailed",e);}};
        monitor=new();
        monitor.Watching=()=>gate.Enabled||context!=null||generating;
        monitor.IsOwnWindow=h=>h==overlay.Handle;
        monitor.Activity+=()=>dispatcher.TryEnqueue(Activity);
        monitor.SessionPause+=()=>dispatcher.TryEnqueue(()=>Pause("已暂停：会话或电源状态改变"));
        monitor.Hotkey+=id=>dispatcher.TryEnqueue(()=>HandleHotkey(id));
        tray=new Forms.NotifyIcon{Text="AI 输入助手 · 已暂停",Icon=new Drawing.Icon(Path.Combine(AppContext.BaseDirectory,"Assets","App.ico")),Visible=true};
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("设置",null,(_,_)=>dispatcher.TryEnqueue(OpenSettings));
        menu.Items.Add("检查更新",null,(_,_)=>dispatcher.TryEnqueue(()=>{OpenUpdateSettings();_=Updates?.CheckAsync();}));
        menu.Items.Add("启用／暂停",null,(_,_)=>dispatcher.TryEnqueue(Toggle));
        menu.Items.Add("退出",null,(_,_)=>dispatcher.TryEnqueue(Quit));
        tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(_,_)=>dispatcher.TryEnqueue(OpenSettings);
        tray.BalloonTipClicked+=(_,_)=>dispatcher.TryEnqueue(OpenUpdateSettings);
        Updates=new(Settings,message=>tray.ShowBalloonTip(7000,"AI 输入助手 · 更新",message,Forms.ToolTipIcon.Info),Quit);
        HotkeyError=monitor.Register(Settings);
        if(!monitor.HooksAvailable)HotkeyError+=" 输入监听不可用";
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
        timer.Tick+=async(_,_)=>await TickAsync();
        timer.Start();SetStatus("已暂停 · 按快捷键启用");LocalStore.Log("StartedPaused");
    }
    public void OpenSettings()
    {
        if(window==null)
        {
            window=new SettingsWindow(this);
            window.Closed+=(_,_)=>window=null;
        }
        window.AppWindow.IsShownInSwitchers=true;
        window.AppWindow.Show();
        if(window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)presenter.Restore();
        window.Activate();
        Native.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
    }
    void OpenUpdateSettings(){OpenSettings();window?.ShowUpdates();}
    void SetStatus(string status)
    {
        if(disposed)return;
        Status=status;
        tray.Text="AI 输入助手 · "+(gate.Enabled?"已启用":"已暂停");
        overlay.SetStatus(status,gate.Enabled,generating);
        Changed?.Invoke();
    }
    void Invalidate()
    {
        gate.Invalidate();context=null;
        cancel.Cancel();cancel.Dispose();cancel=new();
        overlay.Conceal();
    }
    public void Pause(string reason="已暂停")
    {
        if(disposed)return;
        gate.SetEnabled(false);Invalidate();due=DateTime.MaxValue;
        SetStatus(reason);
        _=ClearHostAsync();
    }
    async Task ClearHostAsync()
    {
        try{await broker.CallAsync(new("clear"),CancellationToken.None);}catch{}
    }
    public void Toggle()
    {
        if(gate.Enabled){Pause();return;}
        if(HotkeyError.Length>0){SetStatus("快捷键或输入监听不可用，请在设置中处理");return;}
        if(LocalStore.ReadKey().Length==0){SetStatus("请先保存 DeepSeek API Key");OpenSettings();return;}
        gate.SetEnabled(true);Invalidate();due=DateTime.UtcNow.AddMilliseconds(1500);
        foreground=Native.GetForegroundWindow();SetStatus("已启用：等待输入停顿");
    }
    void Activity()
    {
        if(disposed)return;
        // Even while inserting, a real input invalidates the request generation.
        Invalidate();
        due=gate.Enabled?DateTime.UtcNow.AddMilliseconds(1500):DateTime.MaxValue;
        SetStatus(gate.Enabled?"已启用 · 等待输入停顿":"已暂停");
    }
    async void HandleHotkey(int id)
    {
        try
        {
            if(id==1)Toggle();
            if(id==2&&!inserting&&gate.Enabled)
            {
                Invalidate();due=DateTime.MaxValue;
                long shotRevision=gate.Revision;
                for(int i=0;i<100&&generating&&gate.IsCurrent(shotRevision);i++)await Task.Delay(20);
                if(gate.IsCurrent(shotRevision)&&!generating)await GenerateAsync(true);
            }
            if(id==3)await AcceptAsync();
        }
        catch(Exception e){LocalStore.Log("HotkeyFailed",e);SetStatus("操作未完成，请查看诊断日志");}
    }
    async Task TickAsync()
    {
        if(tickBusy||disposed||inserting)return;
        tickBusy=true;
        try
        {
            if(!gate.Enabled&&context==null&&!generating)return;
            nint now=Native.GetForegroundWindow();
            if(now!=foreground)
            {
                foreground=now;Activity();
            }
            if(context!=null&&DateTime.UtcNow-lastProbe>TimeSpan.FromMilliseconds(250))
            {
                lastProbe=DateTime.UtcNow;
                long rev=gate.Revision;var captured=context;
                var valid=await broker.CallAsync(new("probe",captured.Token),cancel.Token);
                if(gate.IsCurrent(rev)&&!valid.Ok)Activity();
            }
            if(gate.Enabled&&!generating&&context==null&&DateTime.UtcNow>=due&&DateTime.UtcNow>=cooldown&&Native.KeysReleased()&&(Native.GetAsyncKeyState(1)&0x8000)==0)
            {
                due=DateTime.MaxValue;
                await GenerateAsync(false);
            }
        }
        catch(OperationCanceledException){}
        catch(Exception e){LocalStore.Log("ContextFailed",e);Pause("输入检测暂不可用，按快捷键重新启用");}
        finally{tickBusy=false;}
    }
    async Task GenerateAsync(bool screenshot)
    {
        if(generating){SetStatus("正在取消上次请求，请稍后重新触发");return;}
        if(DateTime.UtcNow<cooldown){SetStatus("服务冷却中，请稍后重试");return;}
        string key=LocalStore.ReadKey();
        if(key.Length==0){SetStatus("请先保存 DeepSeek API Key");return;}
        generating=true;
        long revision=gate.Revision;
        var ct=cancel.Token;
        byte[]? image=null;
        try
        {
            foreground=Native.GetForegroundWindow();
            SetStatus("正在识别输入框…");
            var capture=await broker.CallAsync(new("capture"),ct);
            if(!gate.IsCurrent(revision))return;
            if(!capture.Ok||capture.Snapshot==null){LocalStore.Log("CaptureFailed_"+capture.Code);SetStatus(Explain(capture.Code));return;}
            context=capture.Snapshot;
            var target=context;
            LocalStore.Log("ContextCaptured",mode:target.IsSelection?"selection":"caret",beforeChars:target.Before.Length,afterChars:target.After.Length,selectionChars:target.SelectedText.Length);
            if(!screenshot&&!target.IsSelection&&string.IsNullOrWhiteSpace(target.Before)&&string.IsNullOrWhiteSpace(target.After))
            {Invalidate();SetStatus("已识别空输入框，请输入或选中文字后再试");return;}
            if(screenshot&&!target.IsSelection)
            {
                overlay.Conceal();
                await Task.Delay(80,ct);
                if(!gate.IsCurrent(revision))return;
                image=ScreenCapture.Capture((nint)target.Window);
            }
            SetStatus(target.IsSelection?$"已选中 {target.SelectedText.Length} 字 · 正在续写…":screenshot?"已识别输入框 · 正在根据截图续写…":$"已识别 {target.Before.Length+target.After.Length} 字 · 正在续写…");
            var result=await client.GenerateAsync(key,target,image,ct,Settings.ThinkingDepth);
            image=null; // Request content owns and clears the image.
            if(!gate.IsCurrent(revision))return;
            var valid=await broker.CallAsync(new("probe",target.Token),ct);
            if(!gate.IsCurrent(revision)||!valid.Ok){Invalidate();return;}
            string? text=TextPolicy.Accept(result,target.IsSelection?target.SelectedText:target.Before,target.IsSelection?"":target.After);
            if(text==null){Invalidate();SetStatus("本次没有合适的续写，继续输入后再试");return;}
            if(gate.Offer(revision,text)){overlay.Present(text,target.IsSelection);SetStatus(target.IsSelection?"选区续写就绪，按采纳快捷键复制":"建议已就绪，按采纳快捷键插入");}
        }
        catch(OperationCanceledException)
        {
            if(gate.IsCurrent(revision)&&!disposed){Invalidate();SetStatus("请求超时，请继续输入或重新截图");}
        }
        catch(ProviderException e)
        {
            if(gate.IsCurrent(revision)&&!disposed)
            {
                Invalidate();cooldown=DateTime.UtcNow.AddSeconds(e.CooldownSeconds);
                SetStatus(Explain(e.Code));
                if(e.Code is "ApiKeyInvalid" or "BalanceInsufficient")Pause(Explain(e.Code));
            }
            LocalStore.Log(e.Code.StartsWith("Http")?"ProviderHttpError":e.Code);
        }
        catch(Exception e)
        {
            if(gate.IsCurrent(revision)&&!disposed){Invalidate();SetStatus("生成失败，请检查网络或导出诊断日志");}
            LocalStore.Log("GenerationFailed",e);
        }
        finally{if(image!=null)Array.Clear(image);generating=false;}
    }
    async Task AcceptAsync()
    {
        if(inserting||context==null)return;
        string? text=gate.Consume();if(text==null)return;
        var target=context;long revision=gate.Revision;
        inserting=true;overlay.Conceal();
        try
        {
            var ct=cancel.Token;
            for(int i=0;i<20&&!Native.KeysReleased();i++)await Task.Delay(50,ct);
            if(!Native.KeysReleased()||!gate.IsCurrent(revision)){Invalidate();return;}
            var reply=await SuggestionActions.AcceptAsync(broker,target,text,ct);
            if(!gate.IsCurrent(revision))return;
            if(reply.Code=="ClipboardBusy")
            {
                gate.Offer(revision,text);overlay.Present(text,true);SetStatus("剪贴板暂被占用，按采纳快捷键重试复制");return;
            }
            Invalidate();
            if(reply.Ok)
            {
                SetStatus(target.IsSelection?"已复制续写，可粘贴到需要的位置":"已插入");
                due=!target.IsSelection&&gate.Enabled?DateTime.UtcNow.AddMilliseconds(150):DateTime.MaxValue;
            }
            else
            {
                due=DateTime.MaxValue;SetStatus(Explain(reply.Code));
                if(reply.Code=="InsertUncertain")Pause(Explain(reply.Code));
            }
        }
        catch(OperationCanceledException){Pause("插入已取消，未自动重试");}
        catch(Exception e){LocalStore.Log(target.IsSelection?"ClipboardFailed":"InsertUncertain",e);Pause(target.IsSelection?"复制失败，请重新生成后再试":"插入结果未确认，已暂停以避免重复");}
        finally{inserting=false;}
    }
    public void SaveSettings()
    {
        LocalStore.Save(Settings);HotkeyError=monitor.Register(Settings);
        overlay.RefreshStyle();Pause(HotkeyError.Length==0?"设置已保存，当前暂停":"快捷键冲突："+HotkeyError);
    }
    public async Task SmokeAsync()
    {
        var reply=await broker.CallAsync(new("ping"),CancellationToken.None);
        if(!reply.Ok||window==null||!monitor.HooksAvailable||overlay.IsDisplayed)throw new InvalidOperationException("SmokeFailed");
        // Exercise the actual title-bar close path. Window.Close() explicitly
        // destroys a WinUI window and bypasses cancellable AppWindow.Closing.
        Native.PostMessage(WinRT.Interop.WindowNative.GetWindowHandle(window),0x112,0xF060,0);
        for(int i=0;i<20&&window!=null&&window.AppWindow.IsVisible;i++)await Task.Delay(100);
        if(window==null||window.AppWindow.IsVisible||window.AppWindow.IsShownInSwitchers||overlay.IsDisplayed||!tray.Visible||disposed)
            throw new InvalidOperationException("BackgroundHideFailed");
        OpenSettings();
        await Task.Delay(300);
        if(!window.AppWindow.IsVisible||!window.AppWindow.IsShownInSwitchers)
            throw new InvalidOperationException("BackgroundRestoreFailed");
        nint settingsHandle=WinRT.Interop.WindowNative.GetWindowHandle(window);
        Native.SetForegroundWindow(settingsHandle);
        for(int i=0;i<10&&Native.GetForegroundWindow()!=settingsHandle;i++)await Task.Delay(50);
        if(Native.GetForegroundWindow()!=settingsHandle)throw new InvalidOperationException("SmokeFocusSetupFailed");
        overlay.VerifyDisplay(Program.SmokePath==null?null:Program.SmokePath+".png");
        if(overlay.IsDisplayed)throw new InvalidOperationException("SmokeLeftPausedOverlayVisible");
        var legacy=System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"Schema\":1,\"FontSize\":19}")!;
        if(legacy.ThinkingDepth!="auto"||legacy.FontSize!=19)throw new InvalidOperationException("ThinkingMigrationFailed");
        foreach(string depth in new[]{"auto","max","none"})
        {
            var restored=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(new Settings{ThinkingDepth=depth}));
            if(restored?.ThinkingDepth!=depth)throw new InvalidOperationException("ThinkingPersistenceFailed");
        }

    }
    static string Explain(string code)=>code switch
    {
        "Composing"=>"正在选择输入法候选词，暂不生成",
        "CompositionUnsupported"=>"当前控件无法可靠检测中文组合输入",
        "ProtectedOrUnknown"=>"当前为密码框或无法确认输入类型",
        "ReadOnlyOrUnknown"=>"当前控件不可编辑或无法确认编辑状态",
        "SelectionTooLarge"=>"选中文字过长，请缩小选区后再试",
        "SelectionUnsupported"=>"暂不支持多个选区，请只保留一个连续选区",
        "TextPatternUnavailable"=>"当前应用暂不提供可读取的光标上下文",
        "NoTarget"=>"请将光标放在其他应用的输入框中",
        "ApiKeyInvalid"=>"API Key 无效，请在设置中重新填写",
        "BalanceInsufficient"=>"DeepSeek 账户余额不足",
        "RateLimited"=>"请求受到限流，稍后可重试",
        "StreamError"=>"续写服务返回错误，请稍后重试",
        "UnexpectedToolCall"=>"续写返回异常，本次未展示，请重试",
        "InsufficientContext"=>"已读取文字，但模型未找到续写方向，请调整内容后重试",
        "ContextUnavailable" or "NoFocus" or "TargetChanged"=>"未能稳定读取光标或选区，请停留片刻后重试",
        "CannotContinue"=>"本次无法续写，请调整内容后重试",
        "NoContinuation"=>"本次没有生成续写，请调整内容后重试",
        "InvalidCompletionFormat"=>"续写返回异常，本次未展示，请重试",
        "IncompleteResponse"=>"续写未完整结束，本次未插入，请重新触发",
        "InsertRejected"=>"输入位置已经变化，本条建议已取消",
        "InsertUncertain"=>"插入结果未确认，已暂停，未自动重试",
        _=>"当前操作未完成："+(code.StartsWith("Http")?"服务暂不可用":"输入目标暂不可用")
    };
    public void Quit(){Dispose();Microsoft.UI.Xaml.Application.Current.Exit();}
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        timer.Stop();cancel.Cancel();Updates.Dispose();
        tray.Visible=false;var icon=tray.Icon;tray.Dispose();icon?.Dispose();monitor.Dispose();overlay.Dispose();broker.Dispose();client.Dispose();
        cancel.Dispose();
    }
}
