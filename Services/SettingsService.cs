using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using MicMuteTool.Models;
using MicMuteTool.Utilities;

namespace MicMuteTool.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "MicMuteTool");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public AppSettings Load()
    {
        AppSettings settings;
        var isNewSettings = false;

        if (!File.Exists(_settingsPath))
        {
            settings = new AppSettings();
            isNewSettings = true;
        }
        else
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                settings = JsonSerializer.Deserialize<AppSettings>(json, _serializerOptions) ?? new AppSettings();
                if (settings == null)
                {
                    settings = new AppSettings();
                    isNewSettings = true;
                }
            }
            catch
            {
                settings = new AppSettings();
                isNewSettings = true;
            }
        }

        EnsureSoundSettings(settings);
        if (settings.Osd is null)
        {
            settings.Osd = new OsdSettings();
        }

        if (isNewSettings)
        {
            EnsureOsdDefaults(settings);
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, _serializerOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static void EnsureSoundSettings(AppSettings settings)
    {
        settings.MicOnSound ??= SoundSetting.DefaultOn();
        settings.MicMutedSound ??= SoundSetting.DefaultOff();

        if (string.IsNullOrWhiteSpace(settings.MicOnSound.FilePath) || !File.Exists(settings.MicOnSound.FilePath))
        {
            settings.MicOnSound.FilePath = SoundDefaults.DefaultOnSoundPath;
        }

        if (string.IsNullOrWhiteSpace(settings.MicMutedSound.FilePath) || !File.Exists(settings.MicMutedSound.FilePath))
        {
            settings.MicMutedSound.FilePath = SoundDefaults.DefaultOffSoundPath;
        }
    }

    private static void EnsureOsdDefaults(AppSettings settings)
    {
        if (!double.IsNaN(settings.Osd.PositionX) && !double.IsNaN(settings.Osd.PositionY))
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var width = settings.Osd.Width;
        var height = settings.Osd.Height;

        var left = workArea.Left + (workArea.Width - width) / 2;
        var top = workArea.Top + workArea.Height - height - 80;

        if (double.IsNaN(left))
        {
            left = workArea.Left;
        }

        var minTop = workArea.Top + 20;
        if (double.IsNaN(top))
        {
            top = minTop;
        }
        else
        {
            top = Math.Max(minTop, top);
        }

        settings.Osd.PositionX = left;
        settings.Osd.PositionY = top;
    }
}

