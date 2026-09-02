using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicMuteTool.Services;

public enum VdsServiceState
{
    NotInstalled,
    Stopped,
    StartPending,
    StopPending,
    Running,
    Unknown
}

public sealed record VdsSystemSnapshot(
    VdsServiceState ServiceState,
    bool UsbipInstalled,
    bool HidHideInstalled,
    string? VdsctlPath)
{
    public bool VdsctlAvailable => !string.IsNullOrWhiteSpace(VdsctlPath);
}

public sealed class VdsControllerRow
{
    public string Name { get; init; } = "DualSense";
    public string Address { get; init; } = "";
    public bool IsUsb { get; init; }
    public bool Online { get; init; }
    public bool Registered { get; init; }
    public bool Connected { get; init; }
    public string Endpoint { get; init; } = "";
    public string Profile { get; init; } = "";
    public string Serial { get; set; } = "";
    public string BuildTime { get; set; } = "";
    public string Firmware { get; set; } = "";
    public string Board { get; set; } = "";
    public string ColorName { get; set; } = "";
    public string MacAddress { get; set; } = "";

    public string ConnectionDisplay => IsUsb ? "USB 连接" : $"蓝牙 {(Online ? "在线" : "离线")}";
    public string RegistrationDisplay => Registered ? "已注册" : "未注册";
    public string VirtualUsbDisplay => Connected ? "已建立" : "未建立";
    public string ProfileDisplay => Profile switch
    {
        "ds5" => "DualSense",
        "dse" => "DualSense Edge",
        _ => "自动"
    };
}

public sealed record VdsAudioBufferState(int Chunks, int Milliseconds);

public sealed class VdsIntegrationService
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    public const int DefaultAudioBufferChunks = 3;
    public const int MinAudioBufferChunks = 1;
    public const int MaxAudioBufferChunks = 48;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public VdsSystemSnapshot GetSystemSnapshot()
    {
        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var installedRoot = Path.Combine(programFiles, "vDS");
        var vdsctlPath = FindVdsctlPath(installedRoot);
        var usbipInstalled = File.Exists(Path.Combine(programFiles, "USBip", "usbip.exe"));
        var hidHideInstalled = File.Exists(Path.Combine(
            programFiles,
            "Nefarius Software Solutions",
            "HidHide",
            "x64",
            "HidHideCLI.exe"));

        return new VdsSystemSnapshot(
            QueryServiceState("vdsd"),
            usbipInstalled,
            hidHideInstalled,
            vdsctlPath);
    }

    public async Task<IReadOnlyList<VdsControllerRow>> GetControllersAsync(
        string vdsctlPath,
        CancellationToken cancellationToken = default)
    {
        var targets = ParseJsonLines<VdsControllerTarget>(
            (await RunAsync(vdsctlPath, ["list-targets"], cancellationToken)).StandardOutput);
        var statuses = ParseJsonLines<VdsControllerStatus>(
            (await RunAsync(vdsctlPath, ["list"], cancellationToken)).StandardOutput);
        var statusByAddress = statuses
            .GroupBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var rows = new List<VdsControllerRow>();

        foreach (var target in targets)
        {
            statusByAddress.TryGetValue(target.Address, out var status);
            rows.Add(new VdsControllerRow
            {
                Name = string.IsNullOrWhiteSpace(target.Name) ? "DualSense" : target.Name,
                Address = target.Address,
                IsUsb = target.Usb,
                Online = target.Online,
                Registered = target.Registered || status is not null,
                Connected = status?.Connected ?? false,
                Endpoint = status?.Path ?? "",
                Profile = status?.Profile ?? ""
            });
            statusByAddress.Remove(target.Address);
        }

        foreach (var status in statusByAddress.Values)
        {
            rows.Add(new VdsControllerRow
            {
                Address = status.Address,
                Registered = true,
                Connected = status.Connected,
                Endpoint = status.Path,
                Profile = status.Profile
            });
        }

        rows.RemoveAll(row => !row.Online && !row.Connected);

        try
        {
            var infoReply = await GetDeviceInfoAsync(vdsctlPath, cancellationToken);
            if (infoReply?.Ok == true)
            {
                var infoByAddress = infoReply.Controllers
                    .GroupBy(item => item.Address, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                {
                    if (!infoByAddress.TryGetValue(row.Address, out var info))
                    {
                        continue;
                    }

                    row.Serial = string.IsNullOrWhiteSpace(info.Info.Serial) || info.Info.Serial.Contains(' ')
                        ? "—"
                        : info.Info.Serial;
                    row.BuildTime = info.Info.BuildTime;
                    row.Firmware = info.Info.Firmware;
                    row.Board = string.IsNullOrWhiteSpace(info.Info.HardwareModel)
                        ? info.Info.HardwareVersion
                        : info.Info.HardwareModel;
                    row.ColorName = info.Info.ColorName;
                    row.MacAddress = info.Info.MacAddress;
                }
            }
        }
        catch
        {
            // Device information is best effort; the controller list remains usable.
        }

        return rows
            .OrderByDescending(item => item.Connected)
            .ThenByDescending(item => item.Online)
            .ThenBy(item => item.Address)
            .ToArray();
    }

    public async Task AttachAsync(
        string vdsctlPath,
        string address,
        string profile,
        string port,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "attach", address };
        if (!string.Equals(port, "auto", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["--ports", port]);
        }
        if (!string.Equals(profile, "auto", StringComparison.OrdinalIgnoreCase))
        {
            arguments.AddRange(["--profile", profile]);
        }

        var output = (await RunAsync(vdsctlPath, arguments, cancellationToken)).StandardOutput;
        RequireControlSuccess(output);
    }

    public async Task DetachAsync(
        string vdsctlPath,
        string address,
        CancellationToken cancellationToken = default)
    {
        var output = (await RunAsync(vdsctlPath, ["detach", address], cancellationToken)).StandardOutput;
        RequireControlSuccess(output);
    }

    public async Task<VdsAudioBufferState> GetAudioBufferAsync(
        string vdsctlPath,
        CancellationToken cancellationToken = default)
    {
        var output = (await RunAsync(vdsctlPath, ["audio-buffer"], cancellationToken)).StandardOutput;
        return RequireAudioBufferReply(output);
    }

    public async Task<VdsAudioBufferState> SetAudioBufferAsync(
        string vdsctlPath,
        int chunks,
        CancellationToken cancellationToken = default)
    {
        chunks = Math.Clamp(chunks, MinAudioBufferChunks, MaxAudioBufferChunks);
        var output = (await RunAsync(
            vdsctlPath,
            ["audio-buffer", chunks.ToString(CultureInfo.InvariantCulture)],
            cancellationToken)).StandardOutput;
        return RequireAudioBufferReply(output);
    }

    public void OpenBluetoothSettings()
    {
        Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
    }

    public async Task StartServiceAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync("sc.exe", ["start", "vdsd"], cancellationToken);
    }

    private async Task<VdsDeviceInfoReply?> GetDeviceInfoAsync(
        string vdsctlPath,
        CancellationToken cancellationToken)
    {
        var output = (await RunAsync(vdsctlPath, ["info"], cancellationToken)).StandardOutput;
        return ParseJsonLines<VdsDeviceInfoReply>(output).SingleOrDefault();
    }

    private VdsAudioBufferState RequireAudioBufferReply(string output)
    {
        var reply = ParseJsonLines<VdsAudioBufferReply>(output).SingleOrDefault();
        if (reply is null || !reply.Ok)
        {
            throw new InvalidOperationException(reply?.Error ?? "vdsctl 没有返回有效的缓冲设置");
        }

        return new VdsAudioBufferState(reply.Chunks, reply.Milliseconds);
    }

    private void RequireControlSuccess(string output)
    {
        var reply = ParseJsonLines<VdsControlReply>(output).SingleOrDefault();
        if (reply is null || !reply.Ok)
        {
            throw new InvalidOperationException(reply?.Error ?? "vdsctl 没有返回有效结果");
        }
    }

    private IReadOnlyList<T> ParseJsonLines<T>(string text)
    {
        var values = new List<T>();
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var value = JsonSerializer.Deserialize<T>(line, _jsonOptions);
            if (value is not null)
            {
                values.Add(value);
            }
        }
        return values;
    }

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            (Path.IsPathRooted(fileName) && !File.Exists(fileName)))
        {
            throw new FileNotFoundException("找不到所需程序", fileName);
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动 {fileName}");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
            if (timeoutSource.IsCancellationRequested)
            {
                throw new TimeoutException($"{Path.GetFileName(fileName)} 操作超时");
            }
            throw;
        }

        var result = new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)} 失败（{result.ExitCode}）：{detail.Trim()}");
        }
        return result;
    }

    private static string? FindVdsctlPath(string installedRoot)
    {
        var candidates = new List<string>
        {
            Path.Combine(installedRoot, "vdsctl.exe"),
            Path.Combine(AppContext.BaseDirectory, "vdsctl.exe")
        };

        var explicitRoot = Environment.GetEnvironmentVariable("VDS_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            candidates.Add(Path.Combine(explicitRoot, "vdsctl.exe"));
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            candidates.Add(Path.Combine(directory.FullName, "out", "build", "windows", "Release", "vdsctl.exe"));
            candidates.Add(Path.Combine(directory.FullName, "windows-vds", "out", "build", "windows", "Release", "vdsctl.exe"));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static VdsServiceState QueryServiceState(string serviceName)
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero)
        {
            return VdsServiceState.Unknown;
        }

        try
        {
            var service = OpenService(manager, serviceName, ServiceQueryStatus);
            if (service == IntPtr.Zero)
            {
                return Marshal.GetLastWin32Error() == 1060
                    ? VdsServiceState.NotInstalled
                    : VdsServiceState.Unknown;
            }

            try
            {
                if (!QueryServiceStatus(service, out var status))
                {
                    return VdsServiceState.Unknown;
                }

                return status.CurrentState switch
                {
                    1 => VdsServiceState.Stopped,
                    2 => VdsServiceState.StartPending,
                    3 => VdsServiceState.StopPending,
                    4 => VdsServiceState.Running,
                    _ => VdsServiceState.Unknown
                };
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr manager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);

    [DllImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class VdsControllerTarget
    {
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("online")] public bool Online { get; set; }
        [JsonPropertyName("registered")] public bool Registered { get; set; }
        [JsonPropertyName("usb")] public bool Usb { get; set; }
    }

    private sealed class VdsControllerStatus
    {
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("connected")] public bool Connected { get; set; }
        [JsonPropertyName("path")] public string Path { get; set; } = "";
        [JsonPropertyName("profile")] public string Profile { get; set; } = "";
    }

    private sealed class VdsControlReply
    {
        [JsonPropertyName("OK")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string Error { get; set; } = "";
    }

    private sealed class VdsAudioBufferReply
    {
        [JsonPropertyName("OK")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string Error { get; set; } = "";
        [JsonPropertyName("chunks")] public int Chunks { get; set; }
        [JsonPropertyName("milliseconds")] public int Milliseconds { get; set; }
    }

    private sealed class VdsDeviceInfoReply
    {
        [JsonPropertyName("OK")] public bool Ok { get; set; }
        [JsonPropertyName("controllers")] public VdsDeviceInfoEntry[] Controllers { get; set; } = [];
    }

    private sealed class VdsDeviceInfoEntry
    {
        [JsonPropertyName("address")] public string Address { get; set; } = "";
        [JsonPropertyName("info")] public VdsDeviceInfoDetails Info { get; set; } = new();
    }

    private sealed class VdsDeviceInfoDetails
    {
        [JsonPropertyName("serial")] public string Serial { get; set; } = "";
        [JsonPropertyName("firmware")] public string Firmware { get; set; } = "";
        [JsonPropertyName("hardware_version")] public string HardwareVersion { get; set; } = "";
        [JsonPropertyName("hardware_model")] public string HardwareModel { get; set; } = "";
        [JsonPropertyName("build_time")] public string BuildTime { get; set; } = "";
        [JsonPropertyName("color_name")] public string ColorName { get; set; } = "";
        [JsonPropertyName("mac_address")] public string MacAddress { get; set; } = "";
    }
}