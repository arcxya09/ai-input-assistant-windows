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
        var result=Read(true);
        if(!result.Ok)return new(false,result.Code);
        snapshot=result;
        return new(true,"Ready",result);
    }
    (IUIAutomationElement Element,IUIAutomationTextRange Caret,nint Window,int Process) Locate()
    {
        if(!Native.InteractiveDesktop())throw new InvalidOperationException("DesktopUnavailable");
        nint hwnd=Native.GetForegroundWindow();
        uint thread=Native.GetWindowThreadProcessId(hwnd,out uint pid);
        if(hwnd==0||pid==parent||pid==Environment.ProcessId)throw new InvalidOperationException("NoTarget");
        var element=automation.GetFocusedElement();
        if(element==null||element.CurrentProcessId!=(int)pid||element.CurrentHasKeyboardFocus==0||element.CurrentIsEnabled==0)
            throw new InvalidOperationException("NoFocus");
        object password=element.GetCurrentPropertyValueEx(30019,1);
        if(password is not bool isPassword||isPassword)throw new InvalidOperationException("ProtectedOrUnknown");
        var pattern=element.GetCurrentPattern(10014) as IUIAutomationTextPattern;
        if(pattern==null)throw new InvalidOperationException("TextPatternUnavailable");
        var selection=pattern.GetSelection();
        if(selection.Length!=1)throw new InvalidOperationException("SelectionUnsupported");
        var selected=selection.GetElement(0);
        if(selected.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,selected,TextPatternRangeEndpoint.TextPatternRangeEndpoint_End)!=0)
            throw new InvalidOperationException("SelectionNotEmpty");
        IUIAutomationTextRange caret;
        if(element.GetCurrentPattern(10024) is IUIAutomationTextPattern2 p2)
        {
            caret=p2.GetCaretRange(out int active);
            if(active==0)throw new InvalidOperationException("InactiveCaret");
        }
        else caret=selected;
        if(caret.GetAttributeValue(40015) is not bool readOnly||readOnly)
            throw new InvalidOperationException("ReadOnlyOrUnknown");
        if(element.GetCurrentPattern(10032) is IUIAutomationTextEditPattern edit)
        {
            if(edit.GetActiveComposition()!=null)throw new InvalidOperationException("Composing");
        }
        else
        {
            // No reliable cross-process IMM/TSF fallback: CJK IME targets without
            // composition support are explicitly unavailable rather than guessed.
            int lang=(int)((long)Native.GetKeyboardLayout(thread)&0x3FF);
            if(lang is 0x04 or 0x11 or 0x12)throw new InvalidOperationException("CompositionUnsupported");
        }
        return(element,caret,hwnd,(int)pid);
    }
    ContextSnapshot Read(bool attach)
    {
        try
        {
            var current=Locate();
            long start=Process.GetProcessById(current.Process).StartTime.ToUniversalTime().Ticks;
            if(attach)
            {
                target=current.Element;anchor=current.Caret.Clone();
                automation.AddAutomationEventHandler(20015,target,TreeScope.TreeScope_Element,null,handler);
                automation.AddAutomationEventHandler(20014,target,TreeScope.TreeScope_Element,null,handler);
            }
            long version=Interlocked.Read(ref handler.Version);
            var before=current.Caret.Clone();
            before.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,TextUnit.TextUnit_Character,-300);
            var after=current.Caret.Clone();
            after.MoveEndpointByUnit(TextPatternRangeEndpoint.TextPatternRangeEndpoint_End,TextUnit.TextUnit_Character,300);
            string left=TextPolicy.Tail(before.GetText(4096),300);
            string right=TextPolicy.Head(after.GetText(4096),300);
            var end=Locate();
            if(version!=Interlocked.Read(ref handler.Version)||automation.CompareElements(current.Element,end.Element)==0||
                current.Caret.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,end.Caret,TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)!=0)
                throw new InvalidOperationException("TargetChanged");
            if(attach){processStart=start;eventVersion=version;}
            return new(){Ok=true,Code="Ready",Token=Guid.NewGuid().ToString("N"),Window=(long)current.Window,
                Process=current.Process,Revision=version,Before=left,After=right};
        }
        catch(InvalidOperationException ex)
        {
            // Only our fixed internal code; never provider error messages.
            string code=ex.Message;
            return new(){Code=code is "Composing" or "CompositionUnsupported" or "ProtectedOrUnknown" or
                "ReadOnlyOrUnknown" or "SelectionNotEmpty" or "TextPatternUnavailable" or "NoTarget" ? code:"ContextUnavailable"};
        }
        catch{return new(){Code="ContextUnavailable"};}
    }
    bool Validate(string token)
    {
        try
        {
            if(snapshot==null||snapshot.Token!=token||target==null||anchor==null||
                eventVersion!=Interlocked.Read(ref handler.Version))return false;
            var current=Locate();
            return current.Window==(nint)snapshot.Window&&current.Process==snapshot.Process&&
                Process.GetProcessById(current.Process).StartTime.ToUniversalTime().Ticks==processStart&&
                automation.CompareElements(target,current.Element)!=0&&
                anchor.CompareEndpoints(TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start,current.Caret,TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start)==0;
        }
        catch{return false;}
    }
    RpcReply Insert(RpcRequest request)
    {
        if(request.Text.Length==0||request.Text.Length>256||request.Text.Any(char.IsControl)||!Validate(request.Token)||snapshot==null)
            return new(false,"InsertRejected");
        // A fresh text read detects providers that missed TextChanged.
        var fresh=Read(false);
        var old=snapshot;
        if(!fresh.Ok||fresh.Before!=old.Before||fresh.After!=old.After||!Validate(request.Token))
            return new(false,"InsertRejected");
        if(!Native.Inject(request.Text,(nint)old.Window)){Clear();return new(false,"InsertUncertain");}
        var timer=Stopwatch.StartNew();
        while(timer.ElapsedMilliseconds<1000)
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
        target=null;anchor=null;snapshot=null;processStart=0;
    }
    public void Dispose(){Clear();}
    [ComVisible(true)]
    public sealed class Changed : IUIAutomationEventHandler
    {
        public long Version;
        public void HandleAutomationEvent(IUIAutomationElement sender,int eventId)=>Interlocked.Increment(ref Version);
    }
}
