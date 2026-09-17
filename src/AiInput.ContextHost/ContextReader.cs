using System.Diagnostics;
using System.Runtime.InteropServices;
using AiInput.Core;
using AiInput.Windows;
using Interop.UIAutomationClient;

namespace AiInput.ContextHost;

public sealed class ContextReader : IDisposable
{
    readonly IUIAutomation automation=new CUIAutomation();
    readonly int parent;
    readonly Changed handler=new();
    IUIAutomationElement? target;
    IUIAutomationTextRange? anchor;
    ContextSnapshot? snapshot;
    long processStart;
    long eventVersion;
    NativeEditState? nativeState;
    AccessibleTextState? accessibleState;
    public ContextReader(int parent){this.parent=parent;}
    public RpcReply Handle(RpcRequest request)
    {
        return request.Command switch
        {
            "capture"=>CaptureReply(),
            "probe"=>new(Validate(request.Token),"TargetChanged"),
            "insert"=>Insert(request),
            "clear"=>ClearReply(),
            "ping"=>new(true,"Ready"),
            _=>new(false,"UnknownCommand")
        };
    }
    RpcReply ClearReply(){Clear();return new(true,"Cleared");}
    RpcReply CaptureReply()
    {
        Clear();
        ChromiumAccessibility.Wake(Native.GetForegroundWindow());
        var result=Read(true);
        if(!result.Ok)return new(false,result.Code);
        snapshot=result;
        return new(true,"Ready",result);
    }
    (IUIAutomationElement Element,IUIAutomationTextRange Caret,IUIAutomationTextRange Bounds,nint Window,int Process) Locate()
    {
        if(!Native.InteractiveDesktop())throw new InvalidOperationException("DesktopUnavailable");
        nint hwnd=Native.GetForegroundWindow();
        uint thread=Native.GetWindowThreadProcessId(hwnd,out uint pid);
        if(hwnd==0||pid==parent||pid==Environment.ProcessId)throw new InvalidOperationException("NoTarget");
        var element=automation.GetFocusedElement();
        var focused=element;
        if(element==null||element.CurrentHasKeyboardFocus==0||element.CurrentIsEnabled==0)
            throw new InvalidOperationException("NoFocus");
        // Browser accessibility providers may live in a renderer process.
        // Confirm tree ownership instead of requiring the same process id.
        var root=automation.ElementFromHandle(hwnd);
        var ancestor=element;bool belongs=false;
        for(int i=0;i<40&&ancestor!=null;i++)
        {
            if(automation.CompareElements(root,ancestor)!=0){belongs=true;break;}
            ancestor=automation.RawViewWalker.GetParentElement(ancestor);
        }
        if(!belongs)throw new InvalidOperationException("NoFocus");
        object password=element.GetCurrentPropertyValueEx(30019,1);
        if(password is not bool isPassword||isPassword)throw new InvalidOperationException("ProtectedOrUnknown");
        var pattern=Pattern(element,10014) as IUIAutomationTextPattern;
        for(int i=0;i<20&&pattern==null;i++)
        {
            var candidate=automation.RawViewWalker.GetParentElement(element);
            if(candidate==null||automation.CompareElements(root,element)!=0)break;
            if(candidate.GetCurrentPropertyValueEx(30019,1) is bool p&&p)throw new InvalidOperationException("ProtectedOrUnknown");
            element=candidate;pattern=Pattern(element,10014) as IUIAutomationTextPattern;
        }
        if(pattern==null)throw new InvalidOperationException("TextPatternUnavailable");
        var bounds=automation.CompareElements(element,focused)!=0?pattern.DocumentRange:pattern.RangeFromChild(focused);
        var selection=pattern.GetSelection();
        if(selection.Length>1)throw new InvalidOperationException("SelectionUnsupported");
        var selected=selection.Length==1?selection.GetElement(0):null;
        bool hasSelection=selected!=null&&!Collapsed(selected);
        IUIAutomationTextRange caret=selected!;
        if(!hasSelection)
        {
            // Some Electron/CodeMirror providers expose a usable collapsed
            // selection even when TextPattern2 reports an inactive caret.
            if(Pattern(element,10024) is IUIAutomationTextPattern2 p2)
            {
                try{var range=p2.GetCaretRange(out int active);if(active!=0&&range!=null)caret=range;}
                catch(COMException){}
            }
            if(caret==null||!Collapsed(caret))throw new InvalidOperationException("InactiveCaret");
            object editable=caret.GetAttributeValue(40015);
            if(editable is bool readOnly)
            {
                if(readOnly)throw new InvalidOperationException("ReadOnlyOrUnknown");
            }
            else if((Pattern(focused,10002)??Pattern(element,10002)) is not IUIAutomationValuePattern value||value.CurrentIsReadOnly!=0)
                throw new InvalidOperationException("ReadOnlyOrUnknown");
        }
        if(Pattern(element,10032) is IUIAutomationTextEditPattern edit)
        {
            var composition=edit.GetActiveComposition();
            if(composition!=null&&composition.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,composition,TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)!=0)
                throw new InvalidOperationException("Composing");
        }
        if(NativeEditReader.IsComposing(NativeEditReader.FocusWindow(thread)))throw new InvalidOperationException("Composing");
        if(caret.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,bounds,TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)<0||
            caret.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,bounds,TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)>0)
            throw new InvalidOperationException("TargetChanged");
        return(focused,caret,bounds,hwnd,(int)pid);
    }
    static bool Collapsed(IUIAutomationTextRange range)=>range.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,range,TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)==0;
    static bool SameRange(IUIAutomationTextRange a,IUIAutomationTextRange b)=>
        a.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,b,TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)==0&&
        a.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,b,TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)==0;
    static object? Pattern(IUIAutomationElement element,int id)
    {try{return element.GetCurrentPattern(id);}catch(COMException){return null;}}
    (NativeEditState? State,nint Window,int Process) ReadNative()
    {
        if(!Native.InteractiveDesktop())throw new InvalidOperationException("DesktopUnavailable");
        nint hwnd=Native.GetForegroundWindow();
        uint thread=Native.GetWindowThreadProcessId(hwnd,out uint pid);
        if(hwnd==0||pid==parent||pid==Environment.ProcessId)throw new InvalidOperationException("NoTarget");
        return(NativeEditReader.Read(hwnd,thread,pid),hwnd,(int)pid);
    }
    ContextSnapshot Read(bool attach)
    {
        try
        {
            var native=ReadNative();
            if(native.State is {} state)
            {
                if(attach)
                {
                    nativeState=state;
                    using var process=Process.GetProcessById(native.Process);
                    processStart=process.StartTime.ToUniversalTime().Ticks;
                }
                return new(){Ok=true,Code="Ready",Token=Guid.NewGuid().ToString("N"),Window=(long)native.Window,
                    Process=native.Process,Before=state.Before,After=state.After,SelectedText=state.SelectedText};
            }
            if(accessibleState!=null)return ReadAccessible(attach);
            try{return ReadUia(attach);}
            catch(InvalidOperationException e) when(e.Message is not ("ProtectedOrUnknown" or "Composing" or "SelectionTooLarge" or "SelectionUnsupported"))
            {
                if(attach)LocalStore.Log("UiaLookupUnavailable");
                return ReadAccessible(attach);
            }
            catch(COMException)
            {
                if(attach)LocalStore.Log("UiaProviderUnavailable");
                return ReadAccessible(attach);
            }
        }
        catch(InvalidOperationException ex)
        {
            string code=ex.Message;
            if(attach)LocalStore.Log(code is "Composing" or "ProtectedOrUnknown" or "ReadOnlyOrUnknown" or "SelectionTooLarge" or "SelectionUnsupported" ? code : "ContextUnavailable");
            return new(){Code=code is "Composing" or "CompositionUnsupported" or "ProtectedOrUnknown" or
                "ReadOnlyOrUnknown" or "SelectionTooLarge" or "SelectionUnsupported" or "TextPatternUnavailable" or "NoTarget" ? code:"ContextUnavailable"};
        }
        catch(Exception error){if(attach)LocalStore.Log("ContextProviderFailed",error);return new(){Code="ContextUnavailable"};}
    }
    ContextSnapshot ReadAccessible(bool attach)
    {
        nint hwnd=Native.GetForegroundWindow();
        uint thread=Native.GetWindowThreadProcessId(hwnd,out uint pid);
        if(hwnd==0||pid==parent||pid==Environment.ProcessId)throw new InvalidOperationException("NoTarget");
        var state=AccessibleTextReader.Read(hwnd,thread)??throw new InvalidOperationException("TextPatternUnavailable");
        var verify=AccessibleTextReader.Read(hwnd,thread);
        if(verify==null||!state.Matches(verify))throw new InvalidOperationException("TargetChanged");
        if(attach)
        {
            accessibleState=state;
            using var owner=Process.GetProcessById((int)pid);processStart=owner.StartTime.ToUniversalTime().Ticks;
            LocalStore.Log("IAccessible2Ready");
        }
        return new(){Ok=true,Code="Ready",Token=Guid.NewGuid().ToString("N"),Window=(long)hwnd,Process=(int)pid,
            Before=state.Before,After=state.After,SelectedText=state.SelectedText};
    }
    ContextSnapshot ReadUia(bool attach)
    {
            var current=Locate();
            using var owner=Process.GetProcessById(current.Process);
            long start=owner.StartTime.ToUniversalTime().Ticks;
            if(attach)
            {
                target=current.Element;anchor=current.Caret.Clone();
                try{automation.AddAutomationEventHandler(20015,target,TreeScope.TreeScope_Element,null,handler);}catch(COMException){}
                try{automation.AddAutomationEventHandler(20014,target,TreeScope.TreeScope_Element,null,handler);}catch(COMException){}
            }
            long version=Interlocked.Read(ref handler.Version);
            string left="",right="",selected="";
            if(!Collapsed(current.Caret))
            {
                selected=current.Caret.GetText(TextPolicy.MaxSelectionChars+1);
                if(selected.Length>TextPolicy.MaxSelectionChars)throw new InvalidOperationException("SelectionTooLarge");
                if(selected.Length==0)throw new InvalidOperationException("SelectionUnsupported");
            }
            else
            {
            var before=current.Caret.Clone();
            before.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,TextUnit.TextUnit_Character,-300);
            if(before.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,current.Bounds,TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)<0)
                before.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,current.Bounds,TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start);
            var after=current.Caret.Clone();
            after.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,TextUnit.TextUnit_Character,300);
            if(after.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,current.Bounds,TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)>0)
                after.MoveEndpointByRange(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,current.Bounds,TextPatternRangeEndpoint.TextPatternRangeEndpoint_End);
            left=TextPolicy.Tail(before.GetText(4096),300);
            right=TextPolicy.Head(after.GetText(4096),300);
            }
            var end=Locate();
            if(version!=Interlocked.Read(ref handler.Version)||automation.CompareElements(current.Element,end.Element)==0||
                !SameRange(current.Caret,end.Caret))
                throw new InvalidOperationException("TargetChanged");
            if(attach){processStart=start;eventVersion=version;LocalStore.Log("UiaTextReady");}
            return new(){Ok=true,Code="Ready",Token=Guid.NewGuid().ToString("N"),Window=(long)current.Window,
                Process=current.Process,Revision=version,Before=left,After=right,SelectedText=selected};
    }
    bool Validate(string token)
    {
        try
        {
            if(snapshot==null||snapshot.Token!=token)return false;
            if(nativeState!=null)
            {
                var native=ReadNative();
                using var owner=Process.GetProcessById(snapshot.Process);
                return native.Window==(nint)snapshot.Window&&native.Process==snapshot.Process&&
                    owner.StartTime.ToUniversalTime().Ticks==processStart&&native.State==nativeState;
            }
            if(accessibleState!=null)
            {
                nint hwnd=Native.GetForegroundWindow();uint thread=Native.GetWindowThreadProcessId(hwnd,out uint pid);
                if(hwnd!=(nint)snapshot.Window||pid!=snapshot.Process)return false;
                using var owner=Process.GetProcessById(snapshot.Process);
                var accessibleFresh=AccessibleTextReader.Read(hwnd,thread);
                return owner.StartTime.ToUniversalTime().Ticks==processStart&&accessibleFresh!=null&&accessibleState.Matches(accessibleFresh);
            }
            if(target==null||anchor==null||
                eventVersion!=Interlocked.Read(ref handler.Version))return false;
            var current=Locate();
            using var process=Process.GetProcessById(current.Process);
            var fresh=Read(false);
            return current.Window==(nint)snapshot.Window&&current.Process==snapshot.Process&&
                process.StartTime.ToUniversalTime().Ticks==processStart&&fresh.Ok&&fresh.Before==snapshot.Before&&fresh.After==snapshot.After&&fresh.SelectedText==snapshot.SelectedText&&
                automation.CompareElements(target,current.Element)!=0&&
                SameRange(anchor,current.Caret);
        }
        catch{return false;}
    }
    RpcReply Insert(RpcRequest request)
    {
        if(snapshot?.IsSelection==true||request.Text.Length==0||request.Text.Length>TextPolicy.MaxInsertionChars||request.Text.Any(char.IsControl)||!Validate(request.Token)||snapshot==null)
            return new(false,"InsertRejected");
        // A fresh text read detects providers that missed TextChanged.
        var fresh=Read(false);
        var old=snapshot;
        if(!fresh.Ok||fresh.Before!=old.Before||fresh.After!=old.After||!Validate(request.Token))
            return new(false,"InsertRejected");
        if(!Native.Inject(request.Text,(nint)old.Window)){Clear();return new(false,"InsertUncertain");}
        var timer=Stopwatch.StartNew();
        while(timer.ElapsedMilliseconds<3000)
        {
            Thread.Sleep(40);
            var now=Read(false);
            if(TextPolicy.InsertionMatches(old,now,request.Text)){Clear();return new(true,"Inserted");}
            if(Native.GetForegroundWindow()!=(nint)old.Window)break;
        }
        Clear();return new(false,"InsertUncertain");
    }
    public void Clear()
    {
        try{automation.RemoveAllEventHandlers();}catch{}
        target=null;anchor=null;snapshot=null;nativeState=null;accessibleState=null;processStart=0;
    }
    public void Dispose(){Clear();}
    [ComVisible(true)]
    public sealed class Changed : IUIAutomationEventHandler
    {
        public long Version;
        public void HandleAutomationEvent(IUIAutomationElement sender,int eventId)=>Interlocked.Increment(ref Version);
    }
}
