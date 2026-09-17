using System.Runtime.InteropServices;
namespace AiInput.Windows;

public static class Native
{
    public const uint InjectionTag = 0x41494931;
    public delegate nint HookProc(int code, nint wParam, nint lParam);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(nint hwnd,System.Text.StringBuilder name,int capacity);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeoutW",SetLastError=true)] public static extern nint SendMessageTimeout(nint hwnd,uint message,nint wParam,nint lParam,uint flags,uint timeout,out nuint result);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeoutW",SetLastError=true)] public static extern nint SendTextMessageTimeout(nint hwnd,uint message,nint wParam,System.Text.StringBuilder text,uint flags,uint timeout,out nuint result);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageTimeoutW",SetLastError=true)] public static extern nint SendSelectionMessageTimeout(nint hwnd,uint message,ref int start,ref int end,uint flags,uint timeout,out nuint result);
    [DllImport("imm32.dll")] public static extern nint ImmGetContext(nint hwnd);
    [DllImport("imm32.dll")] public static extern bool ImmReleaseContext(nint hwnd,nint context);
    [DllImport("imm32.dll",EntryPoint="ImmGetCompositionStringW")] public static extern int ImmGetCompositionString(nint context,uint index,nint buffer,uint length);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct GUITHREADINFO
    { public uint Size,Flags; public nint Active,Focus,Capture,MenuOwner,MoveSize,Caret; public RECT CaretRect; }
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] public static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern nint GetModuleHandle(string? module);
    [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(nint hwnd);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hwnd,uint message,nint wParam,nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] public static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetUserObjectInformation(nint obj, int index, System.Text.StringBuilder buffer, uint length, out uint needed);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(nint hwnd,nint dst, ref POINT location,ref SIZE size,nint src,ref POINT source,uint key,ref BLEND blend,uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; public POINT(int x,int y) { X=x;Y=y; } }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int Width,Height; public SIZE(int w,int h){Width=w;Height=h;} }
    [StructLayout(LayoutKind.Sequential,Pack=1)] public struct BLEND { public byte Op,Flags,Alpha,Format; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYHOOK { public uint Key,Scan,Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEHOOK { public POINT Point; public uint Data,Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint Type; public INPUTUNION U; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public KEYBD Key; [FieldOffset(0)] public MOUSE Mouse; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBD { public ushort Vk,Scan; public uint Flags,Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSE { public int Dx,Dy; public uint Data,Flags,Time; public nuint Extra; }
    public static bool KeysReleased() => new[]{0x11,0x12,0x10,0x5B,0x5C,0x0D}.All(k=>(GetAsyncKeyState(k)&0x8000)==0);
    public static bool InteractiveDesktop()
    {
        nint desk=OpenInputDesktop(0,false,1);
        if(desk==0)return false;
        try { var name=new System.Text.StringBuilder(128);return GetUserObjectInformation(desk,2,name,256,out _) && name.ToString()=="Default"; }
        finally { CloseDesktop(desk); }
    }
    public static bool Inject(string text,nint hwnd)
    {
        if(GetForegroundWindow()!=hwnd || !KeysReleased() || !InteractiveDesktop())return false;
        var inputs=new INPUT[text.Length*2];
        for(int i=0;i<text.Length;i++)
        {
            inputs[i*2]=new INPUT{Type=1,U=new INPUTUNION{Key=new KEYBD{Scan=text[i],Flags=4,Extra=InjectionTag}}};
            inputs[i*2+1]=new INPUT{Type=1,U=new INPUTUNION{Key=new KEYBD{Scan=text[i],Flags=6,Extra=InjectionTag}}};
        }
        return SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<INPUT>())==inputs.Length;
    }
}
