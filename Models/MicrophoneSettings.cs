<<<<<<< HEAD
using System.Collections.Generic;

namespace MicMuteTool.Models;

public class MicrophoneSettings
{
    public bool MuteAll { get; set; } = true;
    public List<string> SelectedDeviceIds { get; set; } = new();

    public MicrophoneSettings Clone() => new()
    {
        MuteAll = MuteAll,
        SelectedDeviceIds = new List<string>(SelectedDeviceIds)
    };
}
=======
using System.Collections.Generic;

namespace MicMuteTool.Models;

public class MicrophoneSettings
{
    public bool MuteAll { get; set; } = true;
    public List<string> SelectedDeviceIds { get; set; } = new();

    public MicrophoneSettings Clone() => new()
    {
        MuteAll = MuteAll,
        SelectedDeviceIds = new List<string>(SelectedDeviceIds)
    };
}
>>>>>>> dc97ba6 (feat: MD3 UI refactor and robust hotkey compatibility)
