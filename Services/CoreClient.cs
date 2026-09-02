using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicMuteTool.Ipc;
using MicMuteTool.Models;

namespace MicMuteTool.Services;

public sealed class CoreClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static bool TryPing(TimeSpan timeout)
    {
        try
        {
            var response = Send(new CoreRequest { Command = "ping" }, timeout);
            return response.Success;
        }
        catch
        {
            return false;
        }
    }

    public bool GetMuteState(MicrophoneSettings? settings = null)
    {
        var response = Send(new CoreRequest { Command = "getMuteState", Microphones = settings });
        return response.MuteState;
    }

    public bool ToggleMute(MicrophoneSettings? settings = null)
    {
        var response = Send(new CoreRequest { Command = "toggleMute", Microphones = settings });
        return response.MuteState;
    }

    public void SetMute(bool mute, MicrophoneSettings? settings = null)
    {
        Send(new CoreRequest { Command = "setMute", Mute = mute, Microphones = settings });
    }

    public IReadOnlyList<MicrophoneDeviceInfo> GetMicrophoneDevices()
    {
        var response = Send(new CoreRequest { Command = "getDevices" });
        return response.Devices ?? new List<MicrophoneDeviceInfo>();
    }

    public void UpdateHotkey(HotkeySetting hotkey, MicrophoneSettings? settings = null)
    {
        Send(new CoreRequest { Command = "updateHotkey", Hotkey = hotkey, Microphones = settings });
    }

    public void ShutdownCore()
    {
        try
        {
            Send(new CoreRequest { Command = "shutdown" }, TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Core may already be gone.
        }
    }

    private static CoreResponse Send(CoreRequest request) => Send(request, TimeSpan.FromSeconds(3));

    private static CoreResponse Send(CoreRequest request, TimeSpan timeout)
    {
        using var client = new NamedPipeClientStream(".", CoreIpc.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        client.Connect(timeout);

        using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, leaveOpen: true);
        writer.WriteLine(JsonSerializer.Serialize(request, JsonOptions));

        var line = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            throw new InvalidOperationException("Core 进程没有返回响应。");
        }

        var response = JsonSerializer.Deserialize<CoreResponse>(line, JsonOptions)
            ?? throw new InvalidOperationException("Core 进程响应格式无效。");

        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "Core 进程执行失败。");
        }

        return response;
    }
}
