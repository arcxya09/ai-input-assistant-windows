using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiInput.Core;

namespace AiInput.DeepSeek;

/// <summary>Owns screenshot bytes. No disk file, immutable base64 string, redirect, or retry.</summary>
public sealed class OneShotContent : HttpContent
{
    byte[]? image;
    readonly byte[] prefix;
    readonly byte[] suffix;
    bool sent;
    public OneShotContent(ContextSnapshot context, byte[]? ownedImage)
    {
        image = ownedImage;
        if (ownedImage?.Length > 8*1024*1024) { Clear(); throw new InvalidDataException("ImageTooLarge"); }
        string user = JsonSerializer.Serialize(new { before = context.Before, after = context.After });
        string start = "{\"model\":\"deepseek-flash\",\"thinking\":{\"type\":\"disabled\"},\"stream\":true,\"max_tokens\":4096,\"messages\":[{\"role\":\"system\",\"content\":" +
            JsonSerializer.Serialize(CompletionClient.Prompt) + "},{\"role\":\"user\",\"content\":";
        if (ownedImage == null)
        {
            prefix = Encoding.UTF8.GetBytes(start + JsonSerializer.Serialize(user) + "}]}");
            suffix = [];
        }
        else
        {
            prefix = Encoding.UTF8.GetBytes(start + "[{\"type\":\"text\",\"text\":" + JsonSerializer.Serialize(user) +
                "},{\"type\":\"image_url\",\"image_url\":{\"detail\":\"high\",\"url\":\"data:image/png;base64,");
            suffix = Encoding.UTF8.GetBytes("\"}}]}]}");
        }
        Headers.ContentType = new MediaTypeHeaderValue("application/json");
    }
    protected override bool TryComputeLength(out long length)
    {
        length = prefix.Length + suffix.Length + (image == null ? 0 : 4L * ((image.Length+2)/3));
        return true;
    }
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
    {
        if (sent) throw new InvalidOperationException("OneShotRequestAlreadySent");
        sent = true;
        try
        {
            await stream.WriteAsync(prefix, ct);
            if (image != null)
            {
                using var transform = new ToBase64Transform();
                await using (var crypto = new CryptoStream(stream, transform, CryptoStreamMode.Write, leaveOpen:true))
                {
                    await crypto.WriteAsync(image, ct);
                    await crypto.FlushFinalBlockAsync(ct);
                }
            }
            await stream.WriteAsync(suffix, ct);
        }
        finally { Clear(); Array.Clear(prefix); Array.Clear(suffix); }
    }
    void Clear()
    {
        if (image != null) { CryptographicOperations.ZeroMemory(image); image = null; }
    }
    protected override void Dispose(bool disposing)
    {
        Clear(); Array.Clear(prefix); Array.Clear(suffix); base.Dispose(disposing);
    }
}
