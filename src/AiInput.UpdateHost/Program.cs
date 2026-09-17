using System.Diagnostics;
using System.Text.Json;
using AiInput.Core;

UpdateInstallPlan? plan = null;
bool parentExited = false;
string result = "UpdateFailed";
int exitCode = 1;
try
{
    if (args.Length != 1) throw new InvalidDataException("InvalidPlan");
    string planPath = Path.GetFullPath(args[0]);
    plan = JsonSerializer.Deserialize(File.ReadAllText(planPath), UpdateJson.Default.UpdateInstallPlan) ?? throw new InvalidDataException("InvalidPlan");
    var version = UpdateClient.ParseVersion(plan.Version) ?? throw new InvalidDataException("InvalidVersion");
    string installer = Path.GetFullPath(plan.Installer);
    if (Path.GetDirectoryName(installer) != Path.GetDirectoryName(planPath) ||
        Path.GetFileName(installer) != $"AiInputAssistant-{version.ToString(3)}-Setup-x64.exe" ||
        !Path.IsPathFullyQualified(plan.AppDirectory) || !File.Exists(Path.Combine(plan.AppDirectory, "AiInputAssistant.exe")))
        throw new InvalidDataException("InvalidPlan");
    // Hold a read-only handle through installation so the verified file cannot be replaced.
    using var lockedInstaller = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
    if (!await UpdateClient.VerifyFileAsync(installer, plan.Size, plan.Sha256, default)) throw new InvalidDataException("ChecksumMismatch");
    File.WriteAllText(planPath + ".ready", "READY");
    Process? parent = null;
    try { parent = Process.GetProcessById(plan.ParentId); } catch (ArgumentException) { }
    if (parent != null)
    {
        using (parent)
        {
            if (!parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await parent.WaitForExitAsync(timeout.Token);
            }
        }
    }
    parentExited = true;
    var start = new ProcessStartInfo(installer) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installer)! };
    foreach (string arg in new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/NOFORCECLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS", "/RESTARTEXITCODE=3010", "/DIR=" + plan.AppDirectory }) start.ArgumentList.Add(arg);
    using var setup = Process.Start(start) ?? throw new IOException("InstallerStartFailed");
    await setup.WaitForExitAsync();
    exitCode = setup.ExitCode;
    result = exitCode switch { 0 => "UpdateInstalled", 3010 => "UpdateNeedsReboot", 2 or 5 => "UpdateCancelled", _ => "UpdateFailed" };
    if (exitCode == 0)
    {
        var installed = FileVersionInfo.GetVersionInfo(Path.Combine(plan.AppDirectory, "AiInputAssistant.exe"));
        if (new Version(installed.FileMajorPart, installed.FileMinorPart, installed.FileBuildPart) != version)
        { result = "UpdateVersionMismatch"; exitCode = 1; }
    }
}
catch (OperationCanceledException) { result = "UpdateWaitTimedOut"; }
catch { result = "UpdateFailed"; }
finally
{
    try
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiInputAssistant");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "update-result.txt"), result);
    }
    catch { }
    // Never restart over files which Windows has deferred replacing until reboot.
    if (plan is { Restart: true } && parentExited && exitCode != 3010)
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(plan.AppDirectory, "AiInputAssistant.exe")) { UseShellExecute = true, WorkingDirectory = plan.AppDirectory }); }
        catch { }
    }
}
return exitCode;
