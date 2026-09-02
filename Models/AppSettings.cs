namespace MicMuteTool.Models;

public class AppSettings
{
    public HotkeySetting ToggleHotkey { get; set; } = HotkeySetting.Default();

    public SoundSetting MicOnSound { get; set; } = SoundSetting.DefaultOn();

    public SoundSetting MicMutedSound { get; set; } = SoundSetting.DefaultOff();

    public OsdSettings Osd { get; set; } = new();

    public MicrophoneSettings Microphones { get; set; } = new();

    public bool EnableOsd { get; set; } = true;

    public bool EnableSound { get; set; } = true;

    public bool RunOnStartup { get; set; }
        = false;

    public AppSettings Clone() => new()
    {
        ToggleHotkey = ToggleHotkey.Clone(),
        MicOnSound = MicOnSound.Clone(),
        MicMutedSound = MicMutedSound.Clone(),
        Osd = Osd.Clone(),
        Microphones = (Microphones ?? new MicrophoneSettings()).Clone(),
        EnableOsd = EnableOsd,
        EnableSound = EnableSound,
        RunOnStartup = RunOnStartup
    };
}

