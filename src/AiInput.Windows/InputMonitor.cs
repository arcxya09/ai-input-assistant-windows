using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace AiInput.Windows;

public sealed class InputMonitor : Form
{
    readonly Native.HookProc keyboardProc,mouseProc;
    nint keyboard,mouse;
    public event Action<int>? Hotkey;
    public event Action? Activity;
    public event Action? SessionPause;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<bool>? Watching {get;set;}
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<nint,bool>? IsOwnWindow {get;set;}
    public bool HooksAvailable => keyboard!=0&&mouse!=0;
    public InputMonitor()
    {
        ShowInTaskbar=false;
        keyboardProc=Keyboard;mouseProc=Mouse;
        _=Handle;
        keyboard=Native.SetWindowsHookEx(13,keyboardProc,Native.GetModuleHandle(null),0);
        mouse=Native.SetWindowsHookEx(14,mouseProc,Native.GetModuleHandle(null),0);
        Native.WTSRegisterSessionNotification(Handle,0);
    }
    public string Register(Settings s)
    {
        for(int i=1;i<=3;i++)Native.UnregisterHotKey(Handle,i);
        var errors=new List<string>();
        uint[] keys=[s.ToggleKey,s.ScreenshotKey,s.AcceptKey];
        for(int i=0;i<3;i++)if(!Native.RegisterHotKey(Handle,i+1,0x4003,keys[i]))errors.Add(new[]{"启用／暂停","截图","采纳"}[i]);
        return string.Join("、",errors);
    }
    nint Keyboard(int code,nint wp,nint lp)
    {
        if(code>=0 && Watching?.Invoke()==true && (wp==0x100||wp==0x104))
        {
            var data=Marshal.PtrToStructure<Native.KEYHOOK>(lp);
            bool modifier=data.Key is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
            bool ourShortcut=(Native.GetAsyncKeyState(0x11)&0x8000)!=0&&(Native.GetAsyncKeyState(0x12)&0x8000)!=0;
            if(data.Extra!=Native.InjectionTag&&!modifier&&!ourShortcut)Activity?.Invoke();
        }
        return Native.CallNextHookEx(keyboard,code,wp,lp);
    }
    nint Mouse(int code,nint wp,nint lp)
    {
        if(code>=0&&Watching?.Invoke()==true&&(wp==0x201||wp==0x202||wp==0x204||wp==0x20A))
        {
            var data=Marshal.PtrToStructure<Native.MOUSEHOOK>(lp);
            if(IsOwnWindow?.Invoke(Native.WindowFromPoint(data.Point))!=true)Activity?.Invoke();
        }
        return Native.CallNextHookEx(mouse,code,wp,lp);
    }
    protected override void WndProc(ref Message m)
    {
        if(m.Msg==0x0312)Hotkey?.Invoke((int)m.WParam);
        if(m.Msg==0x02B1 || m.Msg==0x0218)SessionPause?.Invoke();
        base.WndProc(ref m);
    }
    protected override void Dispose(bool disposing)
    {
        for(int i=1;i<=3;i++)Native.UnregisterHotKey(Handle,i);
        if(keyboard!=0)Native.UnhookWindowsHookEx(keyboard);
        if(mouse!=0)Native.UnhookWindowsHookEx(mouse);
        Native.WTSUnRegisterSessionNotification(Handle);
        base.Dispose(disposing);
    }
}
