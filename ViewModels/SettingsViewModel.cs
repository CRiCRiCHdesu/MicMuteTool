using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using MicMuteTool.Models;

namespace MicMuteTool.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<OsdSettings>? OsdSettingsChanged;

    public HotkeySetting Hotkey { get; private set; }
    public SoundSetting MicOnSound { get; private set; }
    public SoundSetting MicMutedSound { get; private set; }
    public OsdSettings Osd { get; private set; }
    public MicrophoneSettings Microphones { get; private set; }
    private bool _enableOsd;
    private bool _enableSound;
    private bool _runOnStartup;
    private bool _microphoneMuteAll;

    public ObservableCollection<MicrophoneDeviceItem> MicrophoneDevices { get; } = new();

    public SettingsViewModel(AppSettings settings)
    {
        Hotkey = settings.ToggleHotkey.Clone();
        MicOnSound = settings.MicOnSound.Clone();
        MicMutedSound = settings.MicMutedSound.Clone();
        Osd = settings.Osd.Clone();
        Microphones = settings.Microphones.Clone();
        _enableOsd = settings.EnableOsd;
        _enableSound = settings.EnableSound;
        _runOnStartup = settings.RunOnStartup;
        _microphoneMuteAll = Microphones.MuteAll;
    }

    public string HotkeyDisplay => FormatHotkey(Hotkey);

    public string? MicOnSoundPath
    {
        get => MicOnSound.FilePath;
        set
        {
            if (MicOnSound.FilePath == value)
            {
                return;
            }

            MicOnSound.FilePath = value;
            OnPropertyChanged();
        }
    }

    public bool MicrophoneMuteAll
    {
        get => _microphoneMuteAll;
        set
        {
            if (_microphoneMuteAll == value)
            {
                return;
            }

            _microphoneMuteAll = value;
            Microphones.MuteAll = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MicrophoneSelectionEnabled));
        }
    }

    public bool MicrophoneSelectionEnabled => !_microphoneMuteAll;

    public double MicOnVolume
    {
        get => MicOnSound.Volume;
        set
        {
            if (Math.Abs(MicOnSound.Volume - value) < 0.0001)
            {
                return;
            }

            MicOnSound.Volume = Math.Clamp(value, 0.0, 1.0);
            OnPropertyChanged();
        }
    }

    public string? MicMutedSoundPath
    {
        get => MicMutedSound.FilePath;
        set
        {
            if (MicMutedSound.FilePath == value)
            {
                return;
            }

            MicMutedSound.FilePath = value;
            OnPropertyChanged();
        }
    }

    public double MicMutedVolume
    {
        get => MicMutedSound.Volume;
        set
        {
            if (Math.Abs(MicMutedSound.Volume - value) < 0.0001)
            {
                return;
            }

            MicMutedSound.Volume = Math.Clamp(value, 0.0, 1.0);
            OnPropertyChanged();
        }
    }

    public string OsdMicOnText
    {
        get => Osd.MicOnText;
        set
        {
            if (Osd.MicOnText == value)
            {
                return;
            }

            Osd.MicOnText = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public string OsdMicMutedText
    {
        get => Osd.MicMutedText;
        set
        {
            if (Osd.MicMutedText == value)
            {
                return;
            }

            Osd.MicMutedText = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public string OsdMicOnColor
    {
        get => Osd.MicOnColor;
        set
        {
            if (Osd.MicOnColor == value)
            {
                return;
            }

            Osd.MicOnColor = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public string OsdMicMutedColor
    {
        get => Osd.MicMutedColor;
        set
        {
            if (Osd.MicMutedColor == value)
            {
                return;
            }

            Osd.MicMutedColor = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public bool EnableOsd
    {
        get => _enableOsd;
        set
        {
            if (_enableOsd == value)
            {
                return;
            }

            _enableOsd = value;
            OnPropertyChanged();
        }
    }

    public bool EnableSound
    {
        get => _enableSound;
        set
        {
            if (_enableSound == value)
            {
                return;
            }

            _enableSound = value;
            OnPropertyChanged();
        }
    }

    public bool RunOnStartup
    {
        get => _runOnStartup;
        set
        {
            if (_runOnStartup == value)
            {
                return;
            }

            _runOnStartup = value;
            OnPropertyChanged();
        }
    }

    public double OsdWidth
    {
        get => Osd.Width;
        set
        {
            if (Math.Abs(Osd.Width - value) < 0.0001)
            {
                return;
            }

            Osd.Width = Math.Clamp(value, 50, 800);
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public double OsdHeight
    {
        get => Osd.Height;
        set
        {
            if (Math.Abs(Osd.Height - value) < 0.0001)
            {
                return;
            }

            Osd.Height = Math.Clamp(value, 50, 300);
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public double OsdFontSize
    {
        get => Osd.FontSize;
        set
        {
            if (Math.Abs(Osd.FontSize - value) < 0.0001)
            {
                return;
            }

            Osd.FontSize = Math.Clamp(value, 16, 60);
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public double OsdOpacity
    {
        get => Osd.BackgroundOpacity;
        set
        {
            if (Math.Abs(Osd.BackgroundOpacity - value) < 0.0001)
            {
                return;
            }

            Osd.BackgroundOpacity = Math.Clamp(value, 0.05, 1.0);
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public double OsdContentOpacity
    {
        get => Osd.ContentOpacity;
        set
        {
            if (Math.Abs(Osd.ContentOpacity - value) < 0.0001)
            {
                return;
            }

            Osd.ContentOpacity = Math.Clamp(value, 0.05, 1.0);
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public bool OsdShowStatusDot
    {
        get => Osd.ShowStatusDot;
        set
        {
            if (Osd.ShowStatusDot == value)
            {
                return;
            }

            Osd.ShowStatusDot = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public bool OsdEnableFadeIn
    {
        get => Osd.EnableFadeIn;
        set
        {
            if (Osd.EnableFadeIn == value)
            {
                return;
            }

            Osd.EnableFadeIn = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public bool OsdEnableFadeOut
    {
        get => Osd.EnableFadeOut;
        set
        {
            if (Osd.EnableFadeOut == value)
            {
                return;
            }

            Osd.EnableFadeOut = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public double OsdFadeInDuration
    {
        get => Osd.FadeInDurationMs;
        set
        {
            if (Math.Abs(Osd.FadeInDurationMs - value) < 0.0001)
            {
                return;
            }

            Osd.FadeInDurationMs = Math.Clamp(value, 0, 2000);
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public double OsdFadeOutDuration
    {
        get => Osd.FadeOutDurationMs;
        set
        {
            if (Math.Abs(Osd.FadeOutDurationMs - value) < 0.0001)
            {
                return;
            }

            Osd.FadeOutDurationMs = Math.Clamp(value, 0, 2000);
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public double OsdDisplayDuration
    {
        get => Osd.DisplayDurationMs;
        set
        {
            if (Math.Abs(Osd.DisplayDurationMs - value) < 0.0001)
            {
                return;
            }

            Osd.DisplayDurationMs = Math.Clamp(value, 200, 10000);
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public bool OsdIsLocked
    {
        get => Osd.IsPositionLocked;
        set
        {
            if (Osd.IsPositionLocked == value)
            {
                return;
            }

            Osd.IsPositionLocked = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public double OsdPositionX
    {
        get => Osd.PositionX;
        set
        {
            if (Math.Abs(Osd.PositionX - value) < 0.0001)
            {
                return;
            }

            Osd.PositionX = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public double OsdPositionY
    {
        get => Osd.PositionY;
        set
        {
            if (Math.Abs(Osd.PositionY - value) < 0.0001)
            {
                return;
            }

            Osd.PositionY = value;
            OnPropertyChanged();
            NotifyOsdChanged();
        }
    }

    public void UpdateHotkey(HotkeySetting hotkey)
    {
        Hotkey = hotkey.Clone();
        OnPropertyChanged(nameof(Hotkey));
        OnPropertyChanged(nameof(HotkeyDisplay));
    }

    public void UpdateOsdPosition(double x, double y)
    {
        Osd.PositionX = x;
        Osd.PositionY = y;
        OnPropertyChanged(nameof(OsdPositionX));
        OnPropertyChanged(nameof(OsdPositionY));
        NotifyOsdChanged();
    }

    public void ResetOsd()
    {
        Osd = new OsdSettings();
        RaiseOsdAllProperties();
        NotifyOsdChanged();
    }

    public void LoadOsd(OsdSettings settings)
    {
        Osd = settings.Clone();
        RaiseOsdAllProperties();
        NotifyOsdChanged();
    }

    public void LoadFrom(AppSettings settings)
    {
        Hotkey = settings.ToggleHotkey.Clone();
        MicOnSound = settings.MicOnSound.Clone();
        MicMutedSound = settings.MicMutedSound.Clone();
        Osd = settings.Osd.Clone();
        Microphones = settings.Microphones.Clone();
        _enableOsd = settings.EnableOsd;
        _enableSound = settings.EnableSound;
        _runOnStartup = settings.RunOnStartup;
        _microphoneMuteAll = Microphones.MuteAll;

        OnPropertyChanged(nameof(Hotkey));
        OnPropertyChanged(nameof(HotkeyDisplay));
        OnPropertyChanged(nameof(MicOnSoundPath));
        OnPropertyChanged(nameof(MicOnVolume));
        OnPropertyChanged(nameof(MicMutedSoundPath));
        OnPropertyChanged(nameof(MicMutedVolume));
        RaiseOsdAllProperties();
        OnPropertyChanged(nameof(EnableOsd));
        OnPropertyChanged(nameof(EnableSound));
        OnPropertyChanged(nameof(RunOnStartup));
        OnPropertyChanged(nameof(MicrophoneMuteAll));
        OnPropertyChanged(nameof(MicrophoneSelectionEnabled));
        RefreshMicrophoneSelections();
        NotifyOsdChanged();
    }

    public AppSettings ToSettings() => new()
    {
        ToggleHotkey = Hotkey.Clone(),
        MicOnSound = MicOnSound.Clone(),
        MicMutedSound = MicMutedSound.Clone(),
        Osd = Osd.Clone(),
        Microphones = new MicrophoneSettings
        {
            MuteAll = _microphoneMuteAll,
            SelectedDeviceIds = new List<string>(Microphones.SelectedDeviceIds)
        },
        EnableOsd = _enableOsd,
        EnableSound = _enableSound,
        RunOnStartup = _runOnStartup
    };

    public void UpdateMicrophoneDevices(IEnumerable<MicrophoneDeviceInfo> devices)
    {
        foreach (var item in MicrophoneDevices)
        {
            item.PropertyChanged -= OnDeviceItemPropertyChanged;
        }

        MicrophoneDevices.Clear();

        var deviceList = devices?.ToList() ?? new List<MicrophoneDeviceInfo>();
        foreach (var info in deviceList
                     .Where(d => d.IsAvailable)
                     .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var deviceItem = new MicrophoneDeviceItem
            {
                Id = info.Id,
                DisplayName = info.Name,
                IsAvailable = true,
                IsSelected = Microphones.SelectedDeviceIds.Contains(info.Id)
            };

            deviceItem.PropertyChanged += OnDeviceItemPropertyChanged;
            MicrophoneDevices.Add(deviceItem);
        }

        UpdateSelectedDeviceIds();
        OnPropertyChanged(nameof(MicrophoneDevices));
    }

    public void RefreshMicrophoneSelections()
    {
        foreach (var item in MicrophoneDevices)
        {
            item.PropertyChanged -= OnDeviceItemPropertyChanged;
            item.IsSelected = Microphones.SelectedDeviceIds.Contains(item.Id);
            item.PropertyChanged += OnDeviceItemPropertyChanged;
        }

        OnPropertyChanged(nameof(MicrophoneDevices));
    }

    private void OnDeviceItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MicrophoneDeviceItem.IsSelected))
        {
            UpdateSelectedDeviceIds();
        }
    }

    private void UpdateSelectedDeviceIds()
    {
        if (Microphones is null)
        {
            return;
        }

        Microphones.SelectedDeviceIds = MicrophoneDevices
            .Where(device => device.IsSelected)
            .Select(device => device.Id)
            .Distinct()
            .ToList();
    }

    private static string FormatHotkey(HotkeySetting hotkey)
    {
        if (hotkey.Key == Key.None)
        {
            return "未设置";
        }

        var builder = new StringBuilder();

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control))
        {
            builder.Append("Ctrl + ");
        }

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            builder.Append("Shift + ");
        }

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            builder.Append("Alt + ");
        }

        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            builder.Append("Win + ");
        }

        builder.Append(hotkey.Key);
        return builder.ToString();
    }

    private void RaiseOsdAllProperties()
    {
        OnPropertyChanged(nameof(OsdMicOnText));
        OnPropertyChanged(nameof(OsdMicMutedText));
        OnPropertyChanged(nameof(OsdMicOnColor));
        OnPropertyChanged(nameof(OsdMicMutedColor));
        OnPropertyChanged(nameof(OsdWidth));
        OnPropertyChanged(nameof(OsdHeight));
        OnPropertyChanged(nameof(OsdFontSize));
        OnPropertyChanged(nameof(OsdOpacity));
        OnPropertyChanged(nameof(OsdContentOpacity));
        OnPropertyChanged(nameof(OsdIsLocked));
        OnPropertyChanged(nameof(OsdShowStatusDot));
        OnPropertyChanged(nameof(OsdEnableFadeIn));
        OnPropertyChanged(nameof(OsdEnableFadeOut));
        OnPropertyChanged(nameof(OsdFadeInDuration));
        OnPropertyChanged(nameof(OsdFadeOutDuration));
        OnPropertyChanged(nameof(OsdDisplayDuration));
        OnPropertyChanged(nameof(OsdPositionX));
        OnPropertyChanged(nameof(OsdPositionY));
    }

    private void NotifyOsdChanged()
    {
        OsdSettingsChanged?.Invoke(Osd.Clone());
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

