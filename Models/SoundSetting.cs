using MicMuteTool.Utilities;

namespace MicMuteTool.Models;

public class SoundSetting
{
    public string? FilePath { get; set; }
    public double Volume { get; set; } = 1.0;

    public SoundSetting Clone() => new()
    {
        FilePath = FilePath,
        Volume = Volume
    };

    public static SoundSetting DefaultOn() => new()
    {
        FilePath = SoundDefaults.DefaultOnSoundPath,
        Volume = 1.0
    };

    public static SoundSetting DefaultOff() => new()
    {
        FilePath = SoundDefaults.DefaultOffSoundPath,
        Volume = 1.0
    };
}

