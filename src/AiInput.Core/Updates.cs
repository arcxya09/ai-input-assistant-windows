using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AiInput.Core;

public static class ProductInfo
{
    public static Version Version { get; } = new(typeof(ProductInfo).Assembly.GetName().Version!.ToString(3));
    public static string VersionText => Version.ToString(3);
    public const string Repository = "arcxya09/ai-input-assistant-windows";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
}

public sealed record UpdateRelease(Version Version, string Tag, string FileName, Uri DownloadUrl, long Size, string Sha256);
public sealed record UpdateProgress(long Received, long Total)
{
    public double Percent => Total > 0 ? Math.Min(100, Received * 100.0 / Total) : 0;
}
public sealed class UpdateException(string code) : Exception(code);

public sealed class UpdateClient(HttpClient http)
{
    public const long MaxInstallerBytes = 1024L * 1024 * 1024;
    public static Version? ParseVersion(string? tag)
    {
        if (tag == null || !Regex.IsMatch(tag, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)) return null;
        return System.Version.TryParse(tag.TrimStart('v'), out var version) ? version : null;
    }

    public static UpdateRelease? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        Version? version = ParseVersion(tag);
        if (version == null || version <= new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return null;
        string fileName = $"AiInputAssistant-{version.ToString(3)}-Setup-x64.exe";
        var assets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == fileName).ToArray();
        if (assets.Length != 1) throw new UpdateException("MissingInstaller");
        var asset = assets[0];
        string url = asset.GetProperty("browser_download_url").GetString() ?? "";
        string expected = $"https://github.com/{ProductInfo.Repository}/releases/download/{tag}/{fileName}";
        if (url != expected || asset.GetProperty("state").GetString() != "uploaded") throw new UpdateException("InvalidInstaller");
        long size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxInstallerBytes) throw new UpdateException("InvalidInstaller");
        string digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, @"^sha256:[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)) throw new UpdateException("MissingChecksum");
        return new(version, tag, fileName, new Uri(url), size, digest[7..].ToLowerInvariant());
    }

    public async Task<UpdateRelease?> CheckAsync(Version current, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{ProductInfo.Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("AiInputAssistant/" + ProductInfo.VersionText);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if ((int)response.StatusCode is 403 or 429) throw new UpdateException("RateLimited");
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var data = new MemoryStream();
        byte[] buffer = new byte[16384];
        int length;
        while ((length = await input.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (data.Length + length > 2 * 1024 * 1024) throw new UpdateException("InvalidMetadata");
            data.Write(buffer, 0, length);
        }
        return ParseRelease(System.Text.Encoding.UTF8.GetString(data.ToArray()), current);
    }

    public static async Task<bool> VerifyFileAsync(string path, long size, string sha256, CancellationToken ct)
    {
        if (!File.Exists(path)) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        if (file.Length != size) return false;
        return Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> DownloadAsync(UpdateRelease release, string directory, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        // Revalidate even when a caller constructs a release directly.
        if (ParseVersion(release.Tag) != release.Version || release.FileName != $"AiInputAssistant-{release.Version.ToString(3)}-Setup-x64.exe" ||
            release.DownloadUrl.AbsoluteUri != $"https://github.com/{ProductInfo.Repository}/releases/download/{release.Tag}/{release.FileName}" ||
            release.Size <= 0 || release.Size > MaxInstallerBytes || !Regex.IsMatch(release.Sha256, @"^[a-fA-F0-9]{64}$"))
            throw new UpdateException("InvalidInstaller");
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, release.FileName);
        if (await VerifyFileAsync(destination, release.Size, release.Sha256, ct)) return destination;
        string partial = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("AiInputAssistant/" + ProductInfo.VersionText);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            Uri final = response.RequestMessage?.RequestUri ?? release.DownloadUrl;
            if (final.Scheme != "https" || final.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new UpdateException("InvalidInstaller");
            if (response.Content.Headers.ContentLength is long length && length != release.Size) throw new UpdateException("SizeMismatch");
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                byte[] buffer = new byte[65536];
                long total = 0;
                long reported = Environment.TickCount64;
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += count;
                    if (total > release.Size) throw new UpdateException("SizeMismatch");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    if (Environment.TickCount64 - reported >= 200)
                    {
                        progress?.Report(new(total, release.Size));
                        reported = Environment.TickCount64;
                    }
                }
                if (total != release.Size) throw new UpdateException("SizeMismatch");
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new UpdateException("ChecksumMismatch");
                await output.FlushAsync(timeout.Token);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(partial, destination, true);
            progress?.Report(new(release.Size, release.Size));
            return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}

public sealed record UpdateInstallPlan(string Installer, string Version, long Size, string Sha256,
    string AppDirectory, int ParentId, long ParentStartTicks, bool Restart = true);

[JsonSerializable(typeof(UpdateInstallPlan))]
public partial class UpdateJson : JsonSerializerContext;
