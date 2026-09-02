namespace MicMuteTool.Models;

public sealed class MicrophoneDeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsAvailable { get; init; }
}

