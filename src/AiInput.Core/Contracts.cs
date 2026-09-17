namespace AiInput.Core;

public sealed record ContextSnapshot
{
    public bool Ok { get; init; }
    public string Code { get; init; } = "Unsupported";
    public string Token { get; init; } = "";
    public long Window { get; init; }
    public int Process { get; init; }
    public long Revision { get; init; }
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
}
public sealed record RpcRequest(string Command, string Token = "", string Text = "");
public sealed record RpcReply(bool Ok, string Code, ContextSnapshot? Snapshot = null);
public sealed record Completion(string Text, string FinishReason);
public sealed class ProviderException(string code, int cooldownSeconds = 0) : Exception(code)
{
    public string Code { get; } = code;
    public int CooldownSeconds { get; } = cooldownSeconds;
}
public static class Frames
{
    public const int MaxBytes = 65536;
    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken ct)
    {
        var data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(message);
        if (data.Length > MaxBytes) throw new InvalidDataException("FrameSize");
        await stream.WriteAsync(BitConverter.GetBytes(data.Length), ct);
        await stream.WriteAsync(data, ct);
        await stream.FlushAsync(ct);
        Array.Clear(data);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        byte[] prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, ct);
        int size = BitConverter.ToInt32(prefix);
        if (size <= 0 || size > MaxBytes) throw new InvalidDataException("FrameSize");
        byte[] data = new byte[size];
        try
        {
            await stream.ReadExactlyAsync(data, ct);
            return System.Text.Json.JsonSerializer.Deserialize<T>(data)
                ?? throw new InvalidDataException("FrameJson");
        }
        finally { Array.Clear(data); }
    }
}
