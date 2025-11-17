using System.Windows.Input;

namespace MicMuteTool.Models;

public class HotkeySetting
{
    public Key Key { get; set; } = Key.None;
    public ModifierKeys Modifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Shift;

    public static HotkeySetting Default() => new()
    {
        Key = Key.M,
        Modifiers = ModifierKeys.Control | ModifierKeys.Shift
    };

    public HotkeySetting Clone() => new()
    {
        Key = Key,
        Modifiers = Modifiers
    };
}
