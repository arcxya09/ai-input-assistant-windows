using System.Runtime.InteropServices;
using Accessibility;
using AiInput.Core;
using AiInput.Windows;

namespace AiInput.ContextHost;

// Electron versions without native UIA TextPattern still expose IAccessible2.
// Calls stay in ContextHost, whose parent bounds time and can terminate it.
internal sealed record AccessibleTextState(IAccessible Target,int Start,int End,string Before,string After,string SelectedText)
{
    internal bool Matches(AccessibleTextState other)
    {
        nint a=Marshal.GetIUnknownForObject(Target),b=Marshal.GetIUnknownForObject(other.Target);
        try{return a==b&&Start==other.Start&&End==other.End&&Before==other.Before&&After==other.After&&SelectedText==other.SelectedText;}
        finally{Marshal.Release(a);Marshal.Release(b);}
    }
}
internal static class AccessibleTextReader
{
    static readonly Guid AccessibleId=new("618736E0-3C3D-11CF-810C-00AA00389B71");
    static readonly Guid TextId=new("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B");
    [DllImport("oleacc.dll")]static extern int AccessibleObjectFromWindow(nint hwnd,uint objectId,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IAccessible accessible);
    internal static IAccessible? Client(nint hwnd)
    {
        var id=AccessibleId;
        return AccessibleObjectFromWindow(hwnd,0xFFFFFFFC,ref id,out var accessible)>=0?accessible:null;
    }
    static IAccessible? Focus(nint hwnd)
    {
        var current=Client(hwnd);
        for(int i=0;i<20&&current!=null;i++)
        {
            object? focus=current.accFocus;
            if(focus is IAccessible child){current=child;continue;}
            if(focus is int id&&id>0&&current.get_accChild(id) is IAccessible simple){current=simple;continue;}
            return current;
        }
        return null;
    }
    internal static AccessibleTextState? Read(nint hwnd,uint thread)
    {
        if(!ChromiumAccessibility.IsChromium(hwnd))return null;
        var target=Focus(hwnd);if(target==null)return null;
        int state=Convert.ToInt32(target.get_accState(0));
        if((state&0x20000000)!=0)throw new InvalidOperationException("ProtectedOrUnknown");
        if((state&1)!=0||(state&4)==0)return null;
        if(target is not IServiceProvider provider)return null;
        var service=TextId;var iid=TextId;
        if(provider.QueryService(ref service,ref iid,out object textObject)<0||textObject is not IAccessibleText text)return null;
        if(text.NSelections(out int count)<0||count>1)return null;
        int start,end;
        if(count==1)
        {
            if(text.Selection(0,out start,out end)<0)return null;
            if(start>end)(start,end)=(end,start);
        }
        else {if(text.CaretOffset(out start)<0)return null;end=start;}
        if(text.NCharacters(out int length)<0||start<0||end>length||length>1024*1024)return null;
        if(NativeEditReader.IsComposing(NativeEditReader.FocusWindow(thread)))throw new InvalidOperationException("Composing");
        string before="",after="",selected="";
        if(start!=end)
        {
            if(end-start>TextPolicy.MaxSelectionChars)throw new InvalidOperationException("SelectionTooLarge");
            if(text.Text(start,end,out selected)<0||selected==null||selected.Length!=end-start)return null;
        }
        else
        {
            if((state&0x40)!=0||Convert.ToInt32(target.get_accRole(0))!=42)throw new InvalidOperationException("ReadOnlyOrUnknown");
            if(text.Text(Math.Max(0,start-2048),start,out before)<0||text.Text(end,Math.Min(length,end+2048),out after)<0)return null;
            before=TextPolicy.Tail(before??"",300);after=TextPolicy.Head(after??"",300);
        }
        if(Native.GetForegroundWindow()!=hwnd)return null;
        return new(target,start,end,before,after,selected);
    }
    [ComImport,Guid("6D5140C1-7436-11CE-8034-00AA006009FA"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider
    {
        [PreserveSig]int QueryService(ref Guid service,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object result);
    }
    // Vtable order from LinuxA11y/IAccessible2 api/AccessibleText.idl.
    [ComImport,Guid("24FD2FFB-3AAD-4A08-8335-A3AD89C0FB4B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAccessibleText
    {
        [PreserveSig]int AddSelection(int start,int end);
        [PreserveSig]int Attributes(int offset,out int start,out int end,[MarshalAs(UnmanagedType.BStr)]out string value);
        [PreserveSig]int CaretOffset(out int offset);
        [PreserveSig]int CharacterExtents(int offset,int coordinates,out int x,out int y,out int width,out int height);
        [PreserveSig]int NSelections(out int count);
        [PreserveSig]int OffsetAtPoint(int x,int y,int coordinates,out int offset);
        [PreserveSig]int Selection(int index,out int start,out int end);
        [PreserveSig]int Text(int start,int end,[MarshalAs(UnmanagedType.BStr)]out string text);
        [PreserveSig]int TextBeforeOffset(int offset,int boundary,out int start,out int end,[MarshalAs(UnmanagedType.BStr)]out string text);
        [PreserveSig]int TextAfterOffset(int offset,int boundary,out int start,out int end,[MarshalAs(UnmanagedType.BStr)]out string text);
        [PreserveSig]int TextAtOffset(int offset,int boundary,out int start,out int end,[MarshalAs(UnmanagedType.BStr)]out string text);
        [PreserveSig]int RemoveSelection(int index);
        [PreserveSig]int SetCaretOffset(int offset);
        [PreserveSig]int SetSelection(int index,int start,int end);
        [PreserveSig]int NCharacters(out int count);
        [PreserveSig]int ScrollSubstringTo(int start,int end,int type);
        [PreserveSig]int ScrollSubstringToPoint(int start,int end,int coordinates,int x,int y);
        [PreserveSig]int NewText(nint segment);
        [PreserveSig]int OldText(nint segment);
    }
}
