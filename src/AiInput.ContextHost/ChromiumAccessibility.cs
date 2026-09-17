using System.Runtime.InteropServices;
using System.Text;
using AiInput.Windows;

namespace AiInput.ContextHost;

internal static class ChromiumAccessibility
{
    static nint lastWindow;
    static long lastAttempt;
    internal static bool IsChromium(nint hwnd)
    {
        var name=new StringBuilder(128);Native.GetClassName(hwnd,name,name.Capacity);
        return name.ToString().StartsWith("Chrome",StringComparison.OrdinalIgnoreCase);
    }
    internal static void Wake(nint hwnd)
    {
        if(!IsChromium(hwnd))return;
        long now=Environment.TickCount64;
        if(hwnd==lastWindow&&now-lastAttempt<3000)return;
        lastWindow=hwnd;lastAttempt=now;
        // Chromium's documented assistive-technology handshake. No global
        // screen-reader setting, process injection or application restart.
        Native.SendMessageTimeout(hwnd,0x003D,0,1,2,200,out _);
        try{AccessibleTextReader.Client(hwnd);}catch(COMException){}
        Thread.Sleep(120);
    }
}
