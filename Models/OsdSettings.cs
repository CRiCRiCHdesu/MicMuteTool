<<<<<<< HEAD
namespace MicMuteTool.Models;

public class OsdSettings
{
    public string MicOnText { get; set; } = "麦克风已开启";
    public string MicMutedText { get; set; } = "麦克风已静音";
    public string MicOnColor { get; set; } = "#FF2ECC71";
    public string MicMutedColor { get; set; } = "#FFE74C3C";
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 120;
    public double FontSize { get; set; } = 26;
    public double BackgroundOpacity { get; set; } = 0.85;
    public double ContentOpacity { get; set; } = 1.0;
    public bool ShowStatusDot { get; set; } = true;
    public bool EnableFadeIn { get; set; } = true;
    public bool EnableFadeOut { get; set; } = true;
    public double FadeInDurationMs { get; set; } = 180;
    public double FadeOutDurationMs { get; set; } = 260;
    public double DisplayDurationMs { get; set; } = 1800;
    public double PositionX { get; set; } = double.NaN;
    public double PositionY { get; set; } = double.NaN;
    public bool IsPositionLocked { get; set; } = true;

    public OsdSettings Clone() => new()
    {
        MicOnText = MicOnText,
        MicMutedText = MicMutedText,
        MicOnColor = MicOnColor,
        MicMutedColor = MicMutedColor,
        Width = Width,
        Height = Height,
        FontSize = FontSize,
        BackgroundOpacity = BackgroundOpacity,
        ContentOpacity = ContentOpacity,
        ShowStatusDot = ShowStatusDot,
        EnableFadeIn = EnableFadeIn,
        EnableFadeOut = EnableFadeOut,
        FadeInDurationMs = FadeInDurationMs,
        FadeOutDurationMs = FadeOutDurationMs,
        DisplayDurationMs = DisplayDurationMs,
        PositionX = PositionX,
        PositionY = PositionY,
        IsPositionLocked = IsPositionLocked
    };
}
=======
namespace MicMuteTool.Models;

public class OsdSettings
{
    public string MicOnText { get; set; } = "麦克风已开启";
    public string MicMutedText { get; set; } = "麦克风已静音";
    public string MicOnColor { get; set; } = "#FF2ECC71";
    public string MicMutedColor { get; set; } = "#FFE74C3C";
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 120;
    public double FontSize { get; set; } = 26;
    public double BackgroundOpacity { get; set; } = 0.85;
    public double ContentOpacity { get; set; } = 1.0;
    public bool ShowStatusDot { get; set; } = true;
    public bool EnableFadeIn { get; set; } = true;
    public bool EnableFadeOut { get; set; } = true;
    public double FadeInDurationMs { get; set; } = 180;
    public double FadeOutDurationMs { get; set; } = 260;
    public double DisplayDurationMs { get; set; } = 1800;
    public double PositionX { get; set; } = double.NaN;
    public double PositionY { get; set; } = double.NaN;
    public bool IsPositionLocked { get; set; } = true;

    public OsdSettings Clone() => new()
    {
        MicOnText = MicOnText,
        MicMutedText = MicMutedText,
        MicOnColor = MicOnColor,
        MicMutedColor = MicMutedColor,
        Width = Width,
        Height = Height,
        FontSize = FontSize,
        BackgroundOpacity = BackgroundOpacity,
        ContentOpacity = ContentOpacity,
        ShowStatusDot = ShowStatusDot,
        EnableFadeIn = EnableFadeIn,
        EnableFadeOut = EnableFadeOut,
        FadeInDurationMs = FadeInDurationMs,
        FadeOutDurationMs = FadeOutDurationMs,
        DisplayDurationMs = DisplayDurationMs,
        PositionX = PositionX,
        PositionY = PositionY,
        IsPositionLocked = IsPositionLocked
    };
}
>>>>>>> dc97ba6 (feat: MD3 UI refactor and robust hotkey compatibility)
