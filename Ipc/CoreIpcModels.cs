using MicMuteTool.Models;

namespace MicMuteTool.Ipc;

public static class CoreIpc
{
    public const string PipeName = "MicMuteTool.Core.v2";
    public const string UiEventPipeName = "MicMuteTool.UiEvents.v1";
}

public sealed class CoreRequest
{
    public string Command { get; set; } = string.Empty;
    public MicrophoneSettings? Microphones { get; set; }
    public HotkeySetting? Hotkey { get; set; }
    public bool Mute { get; set; }
}

public sealed class CoreResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool MuteState { get; set; }
    public List<MicrophoneDeviceInfo>? Devices { get; set; }
}

public sealed class CoreEvent
{
    public string Type { get; set; } = string.Empty;
    public bool MuteState { get; set; }
}
