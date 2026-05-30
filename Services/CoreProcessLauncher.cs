using System.Diagnostics;
using System.IO;

namespace MicMuteTool.Services;

public static class CoreProcessLauncher
{
    private const string TaskName = "MicMuteToolCore";

    public static void EnsureCoreStarted()
    {
        if (CoreClient.TryPing(TimeSpan.FromMilliseconds(300)))
        {
            return;
        }

        var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MicMuteTool.exe");
        RegisterScheduledTask(exePath);
        TryRunScheduledTask();

        if (!CoreClient.TryPing(TimeSpan.FromSeconds(2)))
        {
            StartCoreWithUacPrompt(exePath);
        }

        CoreClient.TryPing(TimeSpan.FromSeconds(5));
    }

    public static void RegisterScheduledTaskForCurrentExe()
    {
        var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MicMuteTool.exe");
        RegisterScheduledTask(exePath);
    }

    private static void RegisterScheduledTask(string exePath)
    {
        var arguments = $"/Create /TN \"{TaskName}\" /SC ONDEMAND /TR \"\\\"{exePath}\\\" --core\" /RL HIGHEST /F";
        RunHidden("schtasks.exe", arguments, wait: true);
    }

    private static void TryRunScheduledTask()
    {
        RunHidden("schtasks.exe", $"/Run /TN \"{TaskName}\"", wait: true);
    }

    private static void StartCoreWithUacPrompt(string exePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--core",
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            });
        }
        catch
        {
            // The UI can still run; audio actions will report IPC errors if Core is unavailable.
        }
    }

    private static void RunHidden(string fileName, string arguments, bool wait)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });

            if (wait)
            {
                process?.WaitForExit(3000);
            }
        }
        catch
        {
            // Best effort; fall back to direct elevated launch.
        }
    }
}
