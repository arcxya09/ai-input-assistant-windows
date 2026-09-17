using System.Windows.Forms;
using AiInput.Core;
using AiInput.Windows;
namespace AiInput.Windows.Tests;
internal static class SelectionTests
{
    internal static async Task Verify(ContextBroker broker,ContextSnapshot context,string expected)
    {
        if(!context.IsSelection||context.SelectedText!=expected||context.Before!=""||context.After!="")throw new Exception("Selection contains surrounding text or wrong range");
        var rejected=await broker.CallAsync(new("insert",context.Token,"MUST NOT REPLACE"),default);
        if(rejected.Ok)throw new Exception("Selection insertion must be rejected");
        var result=await SuggestionActions.AcceptAsync(broker,context,"完整续写结果，可以自行粘贴。",default);
        if(!result.Ok||result.Code!="Copied"||ReadClipboard()!="完整续写结果，可以自行粘贴。")throw new Exception("Selection copy failed: "+result.Code);
        var unchanged=await broker.CallAsync(new("probe",context.Token),default);
        if(!unchanged.Ok)throw new Exception("Copy changed selected source text or selection");
    }
    internal static string ReadClipboard()
    {
        string result="";Exception? error=null;
        var thread=new Thread(()=>{try{result=Clipboard.GetText();}catch(Exception e){error=e;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(error!=null)throw error;return result;
    }
}
