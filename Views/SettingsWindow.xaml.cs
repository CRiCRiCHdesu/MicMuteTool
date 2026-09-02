using System.Windows;
using System.Windows.Input;
using MicMuteTool.Models;
using MicMuteTool.Services;
using MicMuteTool.ViewModels;

namespace MicMuteTool.Views;

public partial class SettingsWindow : Window
{
    private enum PreviewMode
    {
        None,
        MicOn,
        MicMuted
    }

    private readonly SettingsViewModel _viewModel;
    private readonly SoundPlayerService _soundPlayerService;
    private readonly CoreClient _coreClient;
    private readonly Action<AppSettings> _onSave;
    private readonly OsdWindow? _osdWindow;
    private readonly string _defaultHint = "点击“设置热键”后，按下新的按键组合。按 ESC 取消。";

    private bool _isCapturingHotkey;
    private SettingsTab _currentTab;
    private AppSettings _originalSettings;
    private PreviewMode _currentPreviewMode = PreviewMode.None;
    private PreviewMode _selectedPreviewStyle = PreviewMode.MicOn;

    public SettingsWindow(AppSettings settings, SettingsTab initialTab, Action<AppSettings> onSave, SoundPlayerService soundPlayerService, CoreClient coreClient, OsdWindow? osdWindow = null)
    {
        InitializeComponent();
        _originalSettings = settings.Clone();
        _viewModel = new SettingsViewModel(settings);
        DataContext = _viewModel;
        _onSave = onSave;
        _soundPlayerService = soundPlayerService;
        _coreClient = coreClient;
        _osdWindow = osdWindow;
        _viewModel.OsdSettingsChanged += OnOsdSettingsChanged;
        if (_osdWindow is not null)
        {
            _osdWindow.PositionChanged += OnOsdPositionChanged;
            _osdWindow.ApplySettings(_viewModel.Osd);
        }
        SetActiveTab(initialTab);
        RefreshMicrophoneList(showErrors: false);
    }

    private void SetActiveTab(SettingsTab tab)
    {
        _currentTab = tab;
        HotkeyTab.Visibility = tab == SettingsTab.Hotkey ? Visibility.Visible : Visibility.Collapsed;
        SoundTab.Visibility = tab == SettingsTab.Sound ? Visibility.Visible : Visibility.Collapsed;
        MicrophoneTab.Visibility = tab == SettingsTab.Microphone ? Visibility.Visible : Visibility.Collapsed;
        OsdTab.Visibility = tab == SettingsTab.Osd ? Visibility.Visible : Visibility.Collapsed;

        HotkeyTabButton.IsEnabled = tab != SettingsTab.Hotkey;
        SoundTabButton.IsEnabled = tab != SettingsTab.Sound;
        MicrophoneTabButton.IsEnabled = tab != SettingsTab.Microphone;
        OsdTabButton.IsEnabled = tab != SettingsTab.Osd;
    }

    private void HotkeyTabButton_Click(object sender, RoutedEventArgs e) => SetActiveTab(SettingsTab.Hotkey);

    private void SoundTabButton_Click(object sender, RoutedEventArgs e) => SetActiveTab(SettingsTab.Sound);

    private void MicrophoneTabButton_Click(object sender, RoutedEventArgs e) => SetActiveTab(SettingsTab.Microphone);

    private void OsdTabButton_Click(object sender, RoutedEventArgs e) => SetActiveTab(SettingsTab.Osd);

    private void StartHotkeyCapture_Click(object sender, RoutedEventArgs e)
    {
        _isCapturingHotkey = true;
        HotkeyHintText.Text = "等待按键输入... (ESC 取消)";
        Keyboard.Focus(this);
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.UpdateHotkey(HotkeySetting.Default());
        StopHotkeyCapture();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isCapturingHotkey)
        {
            return;
        }

        e.Handled = true;

        var pressedKey = ResolvePressedKey(e);

        if (pressedKey == Key.None)
        {
            return;
        }

        if (pressedKey == Key.Escape)
        {
            StopHotkeyCapture();
            return;
        }

        if (IsModifierKey(pressedKey))
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var newHotkey = new HotkeySetting
        {
            Key = pressedKey,
            Modifiers = modifiers
        };

        _viewModel.UpdateHotkey(newHotkey);
        StopHotkeyCapture();
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or
                                                  Key.LeftShift or Key.RightShift or
                                                  Key.LeftAlt or Key.RightAlt or
                                                  Key.LWin or Key.RWin;

    private static Key ResolvePressedKey(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey != Key.None)
        {
            return e.SystemKey;
        }

        if (e.Key != Key.None && e.Key != Key.ImeProcessed && e.Key != Key.DeadCharProcessed)
        {
            return e.Key;
        }

        if (e.ImeProcessedKey != Key.None)
        {
            return e.ImeProcessedKey;
        }

        if (e.DeadCharProcessedKey != Key.None)
        {
            return e.DeadCharProcessedKey;
        }

        return Key.None;
    }

    private void StopHotkeyCapture()
    {
        _isCapturingHotkey = false;
        HotkeyHintText.Text = _defaultHint;
    }

    private void BrowseSoundButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Title = "选择音频文件"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (tag == "MicOn")
        {
            _viewModel.MicOnSoundPath = dialog.FileName;
        }
        else if (tag == "MicMuted")
        {
            _viewModel.MicMutedSoundPath = dialog.FileName;
        }
    }

    private void TestSoundButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        SoundSetting setting = tag == "MicOn"
            ? new SoundSetting { FilePath = _viewModel.MicOnSoundPath, Volume = _viewModel.MicOnVolume }
            : new SoundSetting { FilePath = _viewModel.MicMutedSoundPath, Volume = _viewModel.MicMutedVolume };

        _soundPlayerService.Play(setting);
    }

    private void ResetSoundButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        if (tag == "MicOn")
        {
            _viewModel.MicOnSoundPath = SoundSetting.DefaultOn().FilePath;
        }
        else if (tag == "MicMuted")
        {
            _viewModel.MicMutedSoundPath = SoundSetting.DefaultOff().FilePath;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _viewModel.ToSettings();
        _onSave(snapshot);
        _originalSettings = snapshot.Clone();
        System.Windows.MessageBox.Show("设置已保存", "MicMuteTool", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        StopPreview();
        _osdWindow?.ApplySettings(_originalSettings.Osd);
        DialogResult = false;
        Close();
    }

    private void PreviewToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPreviewMode == PreviewMode.None)
        {
            StartPreview(_selectedPreviewStyle);
        }
        else
        {
            StopPreview();
        }
    }

    private void ResetOsdButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetOsd();
        if (_currentPreviewMode != PreviewMode.None)
        {
            StopPreview();
        }
    }

    private void ResetOsdPosition_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.UpdateOsdPosition(double.NaN, double.NaN);
    }

    private void NudgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string direction })
        {
            return;
        }

        double deltaX = 0;
        double deltaY = 0;
        switch (direction)
        {
            case "Left":
                deltaX = -1;
                break;
            case "Right":
                deltaX = 1;
                break;
            case "Up":
                deltaY = -1;
                break;
            case "Down":
                deltaY = 1;
                break;
            default:
                return;
        }

        var currentX = double.IsNaN(_viewModel.OsdPositionX) ? (_osdWindow?.Left ?? 0) : _viewModel.OsdPositionX;
        var currentY = double.IsNaN(_viewModel.OsdPositionY) ? (_osdWindow?.Top ?? 0) : _viewModel.OsdPositionY;

        _viewModel.UpdateOsdPosition(currentX + deltaX, currentY + deltaY);
    }

    private void LockOsdCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        _viewModel.OsdIsLocked = true;
        _osdWindow?.SetLockState(true);
    }

    private void LockOsdCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        _viewModel.OsdIsLocked = false;
        _osdWindow?.SetLockState(false);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        StopPreview();
        _viewModel.OsdSettingsChanged -= OnOsdSettingsChanged;
        if (_osdWindow is not null)
        {
            _osdWindow.PositionChanged -= OnOsdPositionChanged;
            _osdWindow.ApplySettings(_originalSettings.Osd);
        }
    }

    private void RefreshMicrophonesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshMicrophoneList();
    }

    private void RefreshMicrophoneList(bool showErrors = true)
    {
        try
        {
            if (!CoreClient.TryPing(TimeSpan.FromMilliseconds(250)))
            {
                CoreProcessLauncher.EnsureCoreStarted();
            }

            var devices = _coreClient.GetMicrophoneDevices();
            _viewModel.UpdateMicrophoneDevices(devices);
        }
        catch (Exception ex)
        {
            if (showErrors)
            {
                System.Windows.MessageBox.Show($"刷新麦克风列表失败：{ex.Message}", "MicMuteTool", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnOsdSettingsChanged(OsdSettings settings)
    {
        _osdWindow?.ApplySettings(settings);
    }

    private void OnOsdPositionChanged(double x, double y)
    {
        _viewModel.UpdateOsdPosition(x, y);
    }

    private void StartPreview(PreviewMode mode)
    {
        _osdWindow?.ApplySettings(_viewModel.Osd);

        if (mode == PreviewMode.MicOn)
        {
            _osdWindow?.ShowPreview(false);
        }
        else
        {
            _osdWindow?.ShowPreview(true);
        }

        _currentPreviewMode = mode;
        UpdatePreviewToggle();
    }

    private void StopPreview()
    {
        if (_currentPreviewMode == PreviewMode.None)
        {
            return;
        }

        _osdWindow?.HidePreview();
        _currentPreviewMode = PreviewMode.None;
        UpdatePreviewToggle();
    }

    private void UpdatePreviewToggle()
    {
        if (PreviewToggleButton is not null)
        {
            PreviewToggleButton.Content = _currentPreviewMode == PreviewMode.None ? "开启预览" : "停止预览";
        }
    }

    private void PreviewStyleOnRadio_Checked(object sender, RoutedEventArgs e)
    {
        SetPreviewStyle(PreviewMode.MicOn);
    }

    private void PreviewStyleMutedRadio_Checked(object sender, RoutedEventArgs e)
    {
        SetPreviewStyle(PreviewMode.MicMuted);
    }

    private void SetPreviewStyle(PreviewMode mode)
    {
        _selectedPreviewStyle = mode;

        if (_currentPreviewMode != PreviewMode.None)
        {
            StartPreview(mode);
        }
    }
}

