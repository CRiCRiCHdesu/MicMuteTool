<<<<<<< HEAD
using System.IO;

namespace MicMuteTool.Utilities;

public static class SoundDefaults
{
    private const string OnFileName = "ON.wav";
    private const string OffFileName = "OFF.wav";

    public static string DefaultOnSoundPath => GetAssetPath(OnFileName);
    public static string DefaultOffSoundPath => GetAssetPath(OffFileName);

    private static string GetAssetPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
    }
}
=======
using System.IO;

namespace MicMuteTool.Utilities;

public static class SoundDefaults
{
    private const string OnFileName = "ON.wav";
    private const string OffFileName = "OFF.wav";

    public static string DefaultOnSoundPath => GetAssetPath(OnFileName);
    public static string DefaultOffSoundPath => GetAssetPath(OffFileName);

    private static string GetAssetPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
    }
}
>>>>>>> dc97ba6 (feat: MD3 UI refactor and robust hotkey compatibility)
