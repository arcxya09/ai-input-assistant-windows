using System.Text.Json;
using AiInput.Core;

namespace AiInput.DeepSeek;
public static class CompletionOutput
{
    public static Completion Parse(Completion raw)
    {
        if(raw.FinishReason!="stop")throw new ProviderException("IncompleteResponse");
        try
        {
            using var doc=JsonDocument.Parse(raw.Text,new JsonDocumentOptions{MaxDepth=4});
            var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object||root.EnumerateObject().Count(p=>p.Name=="status")!=1||
                root.EnumerateObject().Count(p=>p.Name=="text")!=1||
                !root.TryGetProperty("status",out var status)||status.ValueKind!=JsonValueKind.String||
                !root.TryGetProperty("text",out var content)||content.ValueKind!=JsonValueKind.String)
                throw new ProviderException("InvalidCompletionFormat");
            // Failure text is untrusted diagnostic prose, never a suggestion.
            switch(status.GetString())
            {
                case "insufficient_context":throw new ProviderException("InsufficientContext");
                case "cannot_continue":throw new ProviderException("CannotContinue");
                case "ok":break;
                default:throw new ProviderException("InvalidCompletionFormat");
            }
            string text=content.GetString()!;
            if(string.IsNullOrWhiteSpace(text))throw new ProviderException("NoContinuation");
            if(text.Length>TextPolicy.MaxInsertionChars)throw new ProviderException("IncompleteResponse");
            if(TextPolicy.IsMetaResponse(text))throw new ProviderException("CannotContinue");
            return new(text,"stop");
        }
        catch(JsonException){throw new ProviderException("InvalidCompletionFormat");}
    }
}
