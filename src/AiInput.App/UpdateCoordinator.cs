using System.Diagnostics;
using System.Text.Json;
using AiInput.Core;
using AiInput.Windows;
using Microsoft.UI.Xaml;

namespace AiInput.App;

public sealed class UpdateCoordinator : IDisposable
{
    readonly Settings settings;
    readonly Action<string> notify;
    readonly Action quit;
    readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    readonly UpdateClient client;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(20) };
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? operation;
    string? installer;
    bool disposed;
    public UpdateRelease? Available { get; private set; }
    public bool Busy { get; private set; }
    public bool Downloading { get; private set; }
    public bool Ready => installer != null;
    public double Progress { get; private set; }
    public string Message { get; private set; } = "启动后自动检查，之后每 6 小时检查一次。";
    public event Action? Changed;
    public bool Portable => !File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public UpdateCoordinator(Settings settings, Action<string> notify, Action quit)
    {
        this.settings = settings; this.notify = notify; this.quit = quit;
        client = new(http);
        ReadResult();
        timer.Tick += async (_, _) =>
        {
            timer.Interval = TimeSpan.FromHours(6);
            if (settings.AutoUpdate) await CheckAsync(true);
        };
        if (Program.SmokePath == null) timer.Start();
    }
    void ReadResult()
    {
        string path = Path.Combine(LocalStore.Root, "update-result.txt");
        try
        {
            if (!File.Exists(path)) return;
            string result = File.ReadAllText(path); File.Delete(path);
            Message = result switch
            {
                "UpdateInstalled" => "更新完成，当前版本 " + ProductInfo.VersionText,
                "UpdateNeedsReboot" => "更新需要重启 Windows 才能完成，请保存工作后重启电脑。",
                "UpdateCancelled" => "安装已取消，可以重新检查并安装。",
                _ => "上次更新未完成，请重试，或从 GitHub Release 手动下载安装。"
            };
            LocalStore.Log(result is "UpdateInstalled" or "UpdateNeedsReboot" or "UpdateCancelled" ? result : "UpdateFailed");
        }
        catch (Exception e) { LocalStore.Log("UpdateResultReadFailed", e); }
    }
    void Refresh() { if (!disposed) Changed?.Invoke(); }
    void StartOperation(string message)
    {
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        Busy = true; Message = message; Refresh();
    }
    void EndOperation()
    {
        Busy = false; Downloading = false;
        operation?.Dispose(); operation = null; Refresh();
    }
    public void SetAutomatic(bool enabled)
    {
        bool previous = settings.AutoUpdate;
        settings.AutoUpdate = enabled;
        try { LocalStore.Save(settings); }
        catch (Exception e) { settings.AutoUpdate = previous; Message = "更新设置保存失败。"; LocalStore.Log("UpdateSettingsFailed", e); }
        Refresh();
    }
    public async Task CheckAsync(bool automatic = false)
    {
        if (Busy || disposed) return;
        StartOperation("正在检查 GitHub 最新版本…");
        try
        {
            var found = await client.CheckAsync(ProductInfo.Version, operation!.Token);
            Available = found; installer = null; Progress = 0;
            if (found == null) Message = "当前已是最新正式版：" + ProductInfo.VersionText;
            else
            {
                Message = "发现新版本 " + found.Version.ToString(3); Refresh();
                if (settings.AutoUpdate)
                {
                    await DownloadCoreAsync(operation!.Token);
                    if (!disposed) notify("新版 " + found.Version.ToString(3) + " 已下载，打开设置后点击“重启并安装”。");
                }
            }
            LocalStore.Log("UpdateCheckCompleted");
        }
        catch (Exception e) { HandleError(e); }
        finally { EndOperation(); }
    }
    async Task DownloadCoreAsync(CancellationToken ct)
    {
        var release = Available ?? throw new InvalidOperationException();
        Downloading = true; Progress = 0; Message = "正在下载 " + release.Version.ToString(3) + "…"; Refresh();
        string root = Path.Combine(LocalStore.Root, "updates");
        string directory = Path.Combine(root, release.Version.ToString(3) + "-" + release.Sha256[..12]);
        Cleanup(root, directory);
        var progress = new Progress<UpdateProgress>(p =>
        {
            if (!disposed && Downloading)
            {
                Progress = p.Percent;
                Message = $"正在下载 {release.Version.ToString(3)}：{p.Received / 1048576.0:F1} / {p.Total / 1048576.0:F1} MB";
                Refresh();
            }
        });
        installer = await client.DownloadAsync(release, directory, progress, ct);
        Downloading = false; Progress = 100;
        Message = "新版 " + release.Version.ToString(3) + " 已下载并校验，可重启安装。"; Refresh();
        LocalStore.Log("UpdateDownloadedVerified");
    }
    static void Cleanup(string root, string keep)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (string directory in Directory.GetDirectories(root))
                if (directory != keep && Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-7))
                    try { Directory.Delete(directory, true); } catch { }
        }
        catch { }
    }
    public async Task InstallAsync()
    {
        if (Busy || disposed || Available == null) return;
        StartOperation("正在准备更新…");
        Process? helper = null;
        bool handedOff = false;
        try
        {
            var ct = operation!.Token;
            if (installer == null) await DownloadCoreAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (!await UpdateClient.VerifyFileAsync(installer!, Available.Size, Available.Sha256, ct))
            { installer = null; throw new UpdateException("ChecksumMismatch"); }
            string directory = Path.GetDirectoryName(installer!)!;
            string updater = Path.Combine(directory, "AiInput.UpdateHost.exe");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Updater", "AiInput.UpdateHost.exe"), updater, true);
            using var self = Process.GetCurrentProcess();
            var plan = new UpdateInstallPlan(installer!, Available.Version.ToString(3), Available.Size, Available.Sha256,
                Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), self.Id, self.StartTime.ToUniversalTime().Ticks);
            string planPath = Path.Combine(directory, "install-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan, UpdateJson.Default.UpdateInstallPlan));
            var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
            start.ArgumentList.Add(planPath);
            helper = Process.Start(start) ?? throw new IOException("UpdateHostStartFailed");
            Message = "安装包已校验，正在准备退出并安装…"; Refresh();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (!File.Exists(planPath + ".ready"))
            {
                if (helper.HasExited) throw new IOException("UpdateHostFailed");
                await Task.Delay(100, timeout.Token);
            }
            ct.ThrowIfCancellationRequested();
            LocalStore.Save(settings);
            LocalStore.Log("UpdateInstallStarted");
            handedOff = true;
            quit();
        }
        catch (Exception e) { HandleError(e); }
        finally
        {
            if (!handedOff && helper != null) try { if (!helper.HasExited) helper.Kill(); } catch { }
            helper?.Dispose(); EndOperation();
        }
    }
    void HandleError(Exception e)
    {
        if (disposed) return;
        Message = e switch
        {
            OperationCanceledException => operation?.IsCancellationRequested == true ? "更新操作已取消。" : "更新请求超时，请稍后重试。",
            UpdateException { Message: "RateLimited" } => "GitHub 请求暂时受限，请稍后重试。",
            UpdateException { Message: "ChecksumMismatch" or "SizeMismatch" } => "安装包校验失败，未运行安装，请重新下载。",
            UpdateException { Message: "MissingInstaller" or "MissingChecksum" } => "新版安装包或校验信息尚未完整，请稍后重试。",
            _ => "更新未完成，请检查网络，或从 GitHub Release 手动下载安装。"
        };
        LocalStore.Log(e is OperationCanceledException ? "UpdateCancelledOrTimedOut" : "UpdateOperationFailed", e);
    }
    public void Cancel() => operation?.Cancel();
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        timer.Stop(); lifetime.Cancel(); http.Dispose(); lifetime.Dispose();
    }
}
