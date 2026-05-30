using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;
using MicMuteTool.Models;
using MicMuteTool.Services;
using MicMuteTool.Utilities;
using MicMuteTool.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace MicMuteTool.Views;

public partial class WebSettingsWindow : Window
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
    private readonly Action? _enableLegacySettingsMenuOnce;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    private AppSettings _originalSettings;
    private PreviewMode _currentPreviewMode = PreviewMode.None;
    private PreviewMode _selectedPreviewStyle = PreviewMode.MicOn;
    private bool _webReady;

    public WebSettingsWindow(AppSettings settings, SettingsTab initialTab, Action<AppSettings> onSave, SoundPlayerService soundPlayerService, CoreClient coreClient, OsdWindow? osdWindow = null, Action? enableLegacySettingsMenuOnce = null)
    {
        InitializeComponent();
        _originalSettings = settings.Clone();
        _viewModel = new SettingsViewModel(settings);
        _onSave = onSave;
        _soundPlayerService = soundPlayerService;
        _coreClient = coreClient;
        _osdWindow = osdWindow;
        _enableLegacySettingsMenuOnce = enableLegacySettingsMenuOnce;
        _viewModel.OsdSettingsChanged += OnOsdSettingsChanged;

        if (_osdWindow is not null)
        {
            _osdWindow.PositionChanged += OnOsdPositionChanged;
            _osdWindow.ApplySettings(_viewModel.Osd);
        }

        Loaded += async (_, _) => await InitializeWebViewAsync(initialTab);
        RefreshMicrophoneList(showErrors: false);
    }

    private async Task InitializeWebViewAsync(SettingsTab initialTab)
    {
        await SettingsWebView.EnsureCoreWebView2Async();
        SettingsWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        SettingsWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        SettingsWebView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
        SettingsWebView.NavigationCompleted += (_, _) =>
        {
            _webReady = true;
            PostState(initialTab.ToString().ToLowerInvariant());
        };
        SettingsWebView.Source = new Uri(GetSettingsHtmlPath());
    }

    private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        WebCommand? command;
        try
        {
            command = JsonSerializer.Deserialize<WebCommand>(e.WebMessageAsJson, _jsonOptions);
        }
        catch (Exception ex)
        {
            PostToast($"解析 WebUI 操作失败：{ex.Message}", "error");
            return;
        }

        if (command is null || string.IsNullOrWhiteSpace(command.Action))
        {
            return;
        }

        try
        {
            HandleCommand(command);
        }
        catch (Exception ex)
        {
            PostToast(ex.Message, "error");
        }
    }

    private void HandleCommand(WebCommand command)
    {
        switch (command.Action)
        {
            case "ready":
                _webReady = true;
                PostState(command.Tab);
                break;
            case "update":
                ApplyPatch(command.Patch);
                PostState(command.Tab, silent: true);
                break;
            case "setHotkey":
                SetHotkey(command.Hotkey);
                PostState(command.Tab, silent: true);
                break;
            case "resetHotkey":
                _viewModel.UpdateHotkey(HotkeySetting.Default());
                PostState(command.Tab, silent: true);
                break;
            case "browseSound":
                BrowseSound(command.Target, command.Tab);
                break;
            case "resetSound":
                ResetSound(command.Target);
                PostState(command.Tab, silent: true);
                break;
            case "testSound":
                TestSound(command.Target);
                break;
            case "refreshMicrophones":
                RefreshMicrophoneList();
                PostState(command.Tab, silent: true);
                break;
            case "setDeviceSelected":
                SetDeviceSelected(command.DeviceId, command.Selected);
                PostState(command.Tab, silent: true);
                break;
            case "resetOsdPosition":
                _viewModel.UpdateOsdPosition(double.NaN, double.NaN);
                PostState(command.Tab, silent: true);
                break;
            case "nudgeOsd":
                NudgeOsd(command.Direction);
                PostState(command.Tab, silent: true);
                break;
            case "setPreviewStyle":
                _selectedPreviewStyle = command.Target == "MicMuted" ? PreviewMode.MicMuted : PreviewMode.MicOn;
                if (_currentPreviewMode != PreviewMode.None)
                {
                    StartPreview(_selectedPreviewStyle);
                }
                PostState(command.Tab, silent: true);
                break;
            case "togglePreview":
                if (_currentPreviewMode == PreviewMode.None)
                {
                    StartPreview(_selectedPreviewStyle);
                }
                else
                {
                    StopPreview();
                }
                PostState(command.Tab, silent: true);
                break;
            case "resetOsd":
                _viewModel.ResetOsd();
                StopPreview();
                PostState(command.Tab, silent: true);
                break;
            case "save":
                SaveSettings();
                PostState(command.Tab, silent: true);
                PostToast("设置已保存", "success");
                break;
            case "cancel":
                CancelAndClose();
                break;
            case "openExternal":
                OpenExternal(command.Url);
                break;
            case "enableLegacySettingsMenuOnce":
                EnableLegacySettingsMenuOnce();
                break;
        }
    }

    private void ApplyPatch(Dictionary<string, JsonElement>? patch)
    {
        if (patch is null)
        {
            return;
        }

        foreach (var (key, value) in patch)
        {
            switch (key)
            {
                case nameof(SettingsViewModel.EnableOsd):
                    _viewModel.EnableOsd = value.GetBoolean();
                    break;
                case nameof(SettingsViewModel.EnableSound):
                    _viewModel.EnableSound = value.GetBoolean();
                    break;
                case nameof(SettingsViewModel.RunOnStartup):
                    _viewModel.RunOnStartup = value.GetBoolean();
                    break;
                case nameof(SettingsViewModel.MicrophoneMuteAll):
                    _viewModel.MicrophoneMuteAll = value.GetBoolean();
                    break;
                case nameof(SettingsViewModel.MicOnSoundPath):
                    _viewModel.MicOnSoundPath = value.GetString();
                    break;
                case nameof(SettingsViewModel.MicMutedSoundPath):
                    _viewModel.MicMutedSoundPath = value.GetString();
                    break;
                case nameof(SettingsViewModel.MicOnVolume):
                    _viewModel.MicOnVolume = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.MicMutedVolume):
                    _viewModel.MicMutedVolume = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.OsdMicOnText):
                    _viewModel.OsdMicOnText = value.GetString() ?? string.Empty;
                    break;
                case nameof(SettingsViewModel.OsdMicMutedText):
                    _viewModel.OsdMicMutedText = value.GetString() ?? string.Empty;
                    break;
                case nameof(SettingsViewModel.OsdMicOnColor):
                    _viewModel.OsdMicOnColor = value.GetString() ?? "#FF2ECC71";
                    break;
                case nameof(SettingsViewModel.OsdMicMutedColor):
                    _viewModel.OsdMicMutedColor = value.GetString() ?? "#FFE74C3C";
                    break;
                case nameof(SettingsViewModel.OsdWidth):
                    _viewModel.OsdWidth = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.OsdHeight):
                    _viewModel.OsdHeight = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.OsdFontSize):
                    _viewModel.OsdFontSize = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.OsdOpacity):
                    _viewModel.OsdOpacity = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.OsdContentOpacity):
                    _viewModel.OsdContentOpacity = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.OsdShowStatusDot):
                    _viewModel.OsdShowStatusDot = value.GetBoolean();
                    break;
                case nameof(SettingsViewModel.OsdEnableFadeIn):
                    _viewModel.OsdEnableFadeIn = value.GetBoolean();
                    break;
                case nameof(SettingsViewModel.OsdEnableFadeOut):
                    _viewModel.OsdEnableFadeOut = value.GetBoolean();
                    break;
                case nameof(SettingsViewModel.OsdFadeInDuration):
                    _viewModel.OsdFadeInDuration = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.OsdFadeOutDuration):
                    _viewModel.OsdFadeOutDuration = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.OsdDisplayDuration):
                    _viewModel.OsdDisplayDuration = value.GetDouble();
                    break;
                case nameof(SettingsViewModel.OsdIsLocked):
                    _viewModel.OsdIsLocked = value.GetBoolean();
                    _osdWindow?.SetLockState(_viewModel.OsdIsLocked);
                    break;
                case nameof(SettingsViewModel.OsdPositionX):
                    _viewModel.OsdPositionX = ReadNullableDouble(value) ?? double.NaN;
                    break;
                case nameof(SettingsViewModel.OsdPositionY):
                    _viewModel.OsdPositionY = ReadNullableDouble(value) ?? double.NaN;
                    break;
            }
        }
    }

    private static double? ReadNullableDouble(JsonElement value)
    {
        return value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ? null : value.GetDouble();
    }

    private void SetHotkey(WebHotkey? hotkey)
    {
        if (hotkey is null || string.IsNullOrWhiteSpace(hotkey.Key))
        {
            return;
        }

        if (!Enum.TryParse<Key>(hotkey.Key, true, out var key))
        {
            return;
        }

        var modifiers = ModifierKeys.None;
        foreach (var modifier in hotkey.Modifiers ?? Array.Empty<string>())
        {
            if (modifier.Equals("Control", StringComparison.OrdinalIgnoreCase) || modifier.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Control;
            }
            else if (modifier.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Shift;
            }
            else if (modifier.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Alt;
            }
            else if (modifier.Equals("Windows", StringComparison.OrdinalIgnoreCase) || modifier.Equals("Win", StringComparison.OrdinalIgnoreCase) || modifier.Equals("Meta", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Windows;
            }
        }

        _viewModel.UpdateHotkey(new HotkeySetting
        {
            Key = key,
            Modifiers = modifiers
        });
    }

    private void BrowseSound(string? target, string? tab)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "音频文件 (*.wav;*.mp3)|*.wav;*.mp3|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Title = "选择音频文件"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (target == "MicOn")
        {
            _viewModel.MicOnSoundPath = dialog.FileName;
        }
        else if (target == "MicMuted")
        {
            _viewModel.MicMutedSoundPath = dialog.FileName;
        }

        PostState(tab, silent: true);
    }

    private void ResetSound(string? target)
    {
        if (target == "MicOn")
        {
            _viewModel.MicOnSoundPath = SoundSetting.DefaultOn().FilePath;
        }
        else if (target == "MicMuted")
        {
            _viewModel.MicMutedSoundPath = SoundSetting.DefaultOff().FilePath;
        }
    }

    private void TestSound(string? target)
    {
        var setting = target == "MicOn"
            ? new SoundSetting { FilePath = _viewModel.MicOnSoundPath, Volume = _viewModel.MicOnVolume }
            : new SoundSetting { FilePath = _viewModel.MicMutedSoundPath, Volume = _viewModel.MicMutedVolume };

        _soundPlayerService.Play(setting);
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
                PostToast($"刷新麦克风列表失败：{ex.Message}", "error");
            }
        }
    }

    private void SetDeviceSelected(string? deviceId, bool selected)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        var device = _viewModel.MicrophoneDevices.FirstOrDefault(item => item.Id == deviceId);
        if (device is not null)
        {
            device.IsSelected = selected;
        }
    }

    private void NudgeOsd(string? direction)
    {
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

    private static void OpenExternal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private void SaveSettings()
    {
        var snapshot = _viewModel.ToSettings();
        _onSave(snapshot);
        _originalSettings = snapshot.Clone();
    }

    private void EnableLegacySettingsMenuOnce()
    {
        if (_enableLegacySettingsMenuOnce is null)
        {
            PostToast("旧版设置入口不可用", "error");
            return;
        }

        _enableLegacySettingsMenuOnce.Invoke();
        PostToast("已在托盘右键菜单临时添加旧版设置入口，下次打开右键菜单时仅显示一次。", "success");
    }

    private void CancelAndClose()
    {
        StopPreview();
        _osdWindow?.ApplySettings(_originalSettings.Osd);
        DialogResult = false;
        Close();
    }

    private void StartPreview(PreviewMode mode)
    {
        _osdWindow?.ApplySettings(_viewModel.Osd);
        _osdWindow?.ShowPreview(mode == PreviewMode.MicMuted);
        _currentPreviewMode = mode;
    }

    private void StopPreview()
    {
        if (_currentPreviewMode == PreviewMode.None)
        {
            return;
        }

        _osdWindow?.HidePreview();
        _currentPreviewMode = PreviewMode.None;
    }

    private void OnOsdSettingsChanged(OsdSettings settings)
    {
        _osdWindow?.ApplySettings(settings);
    }

    private void OnOsdPositionChanged(double x, double y)
    {
        _viewModel.UpdateOsdPosition(x, y);
        PostState(silent: true);
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

        if (SettingsWebView.CoreWebView2 is not null)
        {
            SettingsWebView.CoreWebView2.WebMessageReceived -= WebView_WebMessageReceived;
        }
    }

    private void PostState(string? activeTab = null, bool silent = false)
    {
        if (!_webReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var payload = new
        {
            type = "state",
            silent,
            activeTab,
            state = BuildState()
        };
        SettingsWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, _jsonOptions));
    }

    private void PostToast(string message, string kind = "info")
    {
        if (!_webReady || SettingsWebView.CoreWebView2 is null)
        {
            return;
        }

        var payload = new { type = "toast", message, kind };
        SettingsWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(payload, _jsonOptions));
    }

    private static string GetSettingsHtmlPath()
    {
        var htmlPath = Path.Combine(AppContext.BaseDirectory, "WebAssets", "settings.html");
        if (!File.Exists(htmlPath))
        {
            throw new FileNotFoundException("找不到 WebUI 设置页面文件。", htmlPath);
        }

        return htmlPath;
    }

    private object BuildState() => new
    {
        enableOsd = _viewModel.EnableOsd,
        enableSound = _viewModel.EnableSound,
        runOnStartup = _viewModel.RunOnStartup,
        hotkeyDisplay = _viewModel.HotkeyDisplay,
        hotkey = new
        {
            key = _viewModel.Hotkey.Key.ToString(),
            modifiers = _viewModel.Hotkey.Modifiers.ToString()
        },
        micOnSoundPath = _viewModel.MicOnSoundPath,
        micMutedSoundPath = _viewModel.MicMutedSoundPath,
        micOnVolume = _viewModel.MicOnVolume,
        micMutedVolume = _viewModel.MicMutedVolume,
        microphoneMuteAll = _viewModel.MicrophoneMuteAll,
        microphoneSelectionEnabled = _viewModel.MicrophoneSelectionEnabled,
        microphoneDevices = _viewModel.MicrophoneDevices.Select(device => new
        {
            id = device.Id,
            displayName = device.DisplayName,
            isAvailable = device.IsAvailable,
            isSelected = device.IsSelected
        }).ToArray(),
        osd = new
        {
            micOnText = _viewModel.OsdMicOnText,
            micMutedText = _viewModel.OsdMicMutedText,
            micOnColor = _viewModel.OsdMicOnColor,
            micMutedColor = _viewModel.OsdMicMutedColor,
            width = _viewModel.OsdWidth,
            height = _viewModel.OsdHeight,
            fontSize = _viewModel.OsdFontSize,
            opacity = _viewModel.OsdOpacity,
            contentOpacity = _viewModel.OsdContentOpacity,
            showStatusDot = _viewModel.OsdShowStatusDot,
            enableFadeIn = _viewModel.OsdEnableFadeIn,
            enableFadeOut = _viewModel.OsdEnableFadeOut,
            fadeInDuration = _viewModel.OsdFadeInDuration,
            fadeOutDuration = _viewModel.OsdFadeOutDuration,
            displayDuration = _viewModel.OsdDisplayDuration,
            isLocked = _viewModel.OsdIsLocked,
            positionX = double.IsNaN(_viewModel.OsdPositionX) ? null : (double?)_viewModel.OsdPositionX,
            positionY = double.IsNaN(_viewModel.OsdPositionY) ? null : (double?)_viewModel.OsdPositionY
        },
        preview = new
        {
            running = _currentPreviewMode != PreviewMode.None,
            selectedStyle = _selectedPreviewStyle == PreviewMode.MicMuted ? "MicMuted" : "MicOn"
        },
        theme = new
        {
            accentColor = GetWindowsAccentColor()
        }
    };

    private static string GetWindowsAccentColor()
    {
        const string fallback = "#6750A4";

        try
        {
            var raw = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
            if (raw is not int accent)
            {
                return fallback;
            }

            var value = unchecked((uint)accent);
            var red = (byte)(value & 0xFF);
            var green = (byte)((value >> 8) & 0xFF);
            var blue = (byte)((value >> 16) & 0xFF);

            return string.Create(CultureInfo.InvariantCulture, $"#{red:X2}{green:X2}{blue:X2}");
        }
        catch
        {
            return fallback;
        }
    }

    private static string GetHtml() => """
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>MicMuteTool WebUI 设置</title>
<style>
:root{--bg:#070a16;--panel:#11172d;--card:#1a2035;--card2:#202845;--line:#3c456a;--muted:#a8aec7;--text:#f3f5ff;--accent:#8b6bff;--accent2:#9b7bff;--ok:#2ecc71;--danger:#e74c3c;--shadow:0 24px 80px rgba(0,0,0,.35)}
*{box-sizing:border-box}body{margin:0;background:radial-gradient(circle at 10% 0%,#1c2450 0,#070a16 34%,#0d1429 100%);color:var(--text);font-family:"Microsoft YaHei UI","Segoe UI",system-ui,sans-serif;min-height:100vh;overflow:hidden}button,input{font:inherit}button{border:0;cursor:pointer;color:#fff}.app{display:grid;grid-template-columns:300px 1fr;height:100vh;padding:14px;gap:0}.sidebar{background:rgba(16,22,43,.96);border:1px solid #313a5d;border-right:0;border-radius:18px 0 0 18px;padding:20px 14px;box-shadow:var(--shadow)}.brand{display:flex;align-items:center;gap:12px;margin:0 4px 24px}.logo{width:44px;height:44px;border-radius:50%;display:grid;place-items:center;background:linear-gradient(135deg,#7d66d9,#a58fff);font-size:22px}.brand h1{margin:0;font-size:24px}.brand p{margin:3px 0 0;color:var(--muted);font-size:12px}.nav{display:grid;gap:8px}.nav button{background:transparent;text-align:left;border-radius:12px;padding:16px 18px;color:#dee3f8;border:1px solid transparent;transition:.18s}.nav button:hover{background:#273052;border-color:#46527a}.nav button.active{background:#7d66d9;border-color:#a58fff;color:#fff}.content{background:linear-gradient(135deg,rgba(9,12,27,.96),rgba(19,27,52,.96));border:1px solid #3c456a;border-radius:0 18px 18px 0;padding:22px;display:grid;grid-template-rows:1fr auto;min-width:0}.pages{overflow:auto;padding-right:6px}.page{display:none;max-width:980px}.page.active{display:block}.page h2{font-size:40px;margin:0 0 18px}.grid2{display:grid;grid-template-columns:1fr 1fr;gap:14px}.card{background:rgba(26,32,53,.95);border:1px solid var(--line);border-radius:16px;padding:18px;margin:0 0 14px}.card h3{margin:0 0 14px;font-size:18px}.hint{color:var(--muted);font-size:14px;line-height:1.7}.row{display:flex;gap:10px;align-items:center}.spread{justify-content:space-between}.field{display:grid;gap:7px;margin:0 0 12px}.field label,.label{color:var(--muted);font-size:14px}.input{width:100%;height:38px;padding:8px 10px;border-radius:10px;border:1px solid #485279;background:#1e2540;color:#f2f4ff;outline:none}.input:focus{border-color:#8b6bff;box-shadow:0 0 0 3px rgba(139,107,255,.18)}.btn{height:40px;padding:0 18px;border-radius:12px;background:var(--accent);font-weight:700;white-space:nowrap}.btn:hover{filter:brightness(1.07)}.btn.secondary{background:#242c47;border:1px solid #56618a}.btn.danger{background:#5b2832;border:1px solid #9b4856}.switch{display:flex;align-items:center;justify-content:space-between;padding:12px 0;border-top:1px solid rgba(255,255,255,.08)}.switch:first-of-type{border-top:0}.switch input{width:46px;height:24px;accent-color:var(--accent)}.pathrow{display:grid;grid-template-columns:1fr auto auto auto;gap:8px}.hotkeybox{height:46px;border:1px solid #485279;background:#1e2540;border-radius:10px;display:grid;place-items:center;font-size:20px;font-weight:800}.capture{outline:2px solid #ffcc66}.range{width:100%;accent-color:var(--accent)}.rangeRow{display:grid;grid-template-columns:1fr 80px;gap:12px;align-items:center}.devices{border:1px solid #485279;background:#1b223a;border-radius:12px;max-height:360px;overflow:auto}.device{display:flex;align-items:center;gap:10px;padding:12px 14px;border-bottom:1px solid rgba(255,255,255,.06)}.device:last-child{border-bottom:0}.device.disabled{opacity:.55}.footer{display:flex;justify-content:flex-end;gap:12px;padding-top:14px;border-top:1px solid rgba(255,255,255,.08)}.toast{position:fixed;right:24px;bottom:24px;background:#202845;border:1px solid #56618a;border-radius:14px;padding:12px 16px;box-shadow:var(--shadow);opacity:0;transform:translateY(12px);transition:.2s}.toast.show{opacity:1;transform:translateY(0)}.toast.success{border-color:#2ecc71}.toast.error{border-color:#e74c3c}.nudge button{width:40px;padding:0}.preview{width:100%;height:132px;border-radius:16px;border:1px dashed #56618a;display:grid;place-items:center;background:#10162b}.previewPanel{min-width:260px;min-height:82px;border-radius:18px;display:flex;align-items:center;justify-content:center;gap:12px;font-weight:800}.dot{width:14px;height:14px;border-radius:50%;background:#fff}.small{font-size:12px;color:var(--muted)}@media(max-width:1050px){.app{grid-template-columns:250px 1fr}.grid2{grid-template-columns:1fr}.pathrow{grid-template-columns:1fr}.page h2{font-size:32px}}
</style>
</head>
<body>
<div class="app">
  <aside class="sidebar">
    <div class="brand"><div class="logo">🎤</div><div><h1>MicMuteTool</h1><p>WebUI 设置</p></div></div>
    <nav class="nav">
      <button data-tab="hotkey" class="active">常规与热键</button>
      <button data-tab="sound">提示音设置</button>
      <button data-tab="microphone">麦克风管理</button>
      <button data-tab="osd">OSD 设置</button>
    </nav>
  </aside>
  <main class="content">
    <section class="pages">
      <div id="hotkey" class="page active">
        <h2>常规与热键</h2>
        <div class="card"><h3>常规设置</h3>
          <label class="switch"><span>启用 OSD</span><input type="checkbox" data-field="EnableOsd"></label>
          <label class="switch"><span>启用提示音</span><input type="checkbox" data-field="EnableSound"></label>
          <label class="switch"><span>开机自启</span><input type="checkbox" data-field="RunOnStartup"></label>
        </div>
        <div class="card"><h3>自定义热键</h3><p class="hint">点击“修改热键”后按下组合键，按 Esc 取消。浏览器安全限制下 Win 键可能显示为 Meta。</p>
          <div class="row"><div id="hotkeyDisplay" class="hotkeybox" style="flex:1">未设置</div><button class="btn" id="captureHotkey">修改热键</button><button class="btn secondary" data-action="resetHotkey">恢复默认</button></div>
          <p id="hotkeyHint" class="hint">等待操作</p>
        </div>
      </div>
      <div id="sound" class="page">
        <h2>提示音设置</h2>
        <div class="card"><h3>开启音效文件</h3><div class="pathrow"><input class="input" data-field="MicOnSoundPath"><button class="btn secondary" data-action="browseSound" data-target="MicOn">浏览</button><button class="btn secondary" data-action="resetSound" data-target="MicOn">恢复默认</button><button class="btn secondary" data-action="testSound" data-target="MicOn">预览 ▶</button></div><div class="field" style="margin-top:12px"><label>开启音量</label><div class="rangeRow"><input class="range" type="range" min="0" max="1" step="0.01" data-field="MicOnVolume"><span id="micOnVolText">100%</span></div></div></div>
        <div class="card"><h3>关闭音效文件</h3><div class="pathrow"><input class="input" data-field="MicMutedSoundPath"><button class="btn secondary" data-action="browseSound" data-target="MicMuted">浏览</button><button class="btn secondary" data-action="resetSound" data-target="MicMuted">恢复默认</button><button class="btn secondary" data-action="testSound" data-target="MicMuted">预览 ▶</button></div><div class="field" style="margin-top:12px"><label>关闭音量</label><div class="rangeRow"><input class="range" type="range" min="0" max="1" step="0.01" data-field="MicMutedVolume"><span id="micMutedVolText">100%</span></div></div></div>
      </div>
      <div id="microphone" class="page">
        <h2>麦克风管理</h2>
        <div class="card"><div class="row spread"><label class="row"><input type="checkbox" data-field="MicrophoneMuteAll">静音所有麦克风</label><button class="btn secondary" data-action="refreshMicrophones">刷新设备</button></div></div>
        <div class="card"><h3>选择需要控制的麦克风</h3><div id="devices" class="devices"></div><p class="hint">选择的设备将受热键控制并显示 OSD 状态。</p></div>
      </div>
      <div id="osd" class="page">
        <h2>OSD 设置</h2>
        <div class="grid2">
          <div class="card"><h3>文案与颜色</h3><div class="field"><label>开启麦克风文案</label><input class="input" data-osd="OsdMicOnText"></div><div class="field"><label>静音麦克风文案</label><input class="input" data-osd="OsdMicMutedText"></div><div class="field"><label>开启背景颜色</label><input class="input" data-osd="OsdMicOnColor"></div><div class="field"><label>静音背景颜色</label><input class="input" data-osd="OsdMicMutedColor"></div></div>
          <div class="card"><h3>尺寸与动画</h3>
            <div class="field"><label>OSD 宽度</label><div class="rangeRow"><input class="range" type="range" min="50" max="800" step="1" data-osd="OsdWidth"><span id="osdWidthText"></span></div></div>
            <div class="field"><label>OSD 高度</label><div class="rangeRow"><input class="range" type="range" min="50" max="300" step="1" data-osd="OsdHeight"><span id="osdHeightText"></span></div></div>
            <div class="field"><label>字体大小</label><div class="rangeRow"><input class="range" type="range" min="16" max="60" step="1" data-osd="OsdFontSize"><span id="osdFontText"></span></div></div>
            <div class="field"><label>背景透明度</label><input class="range" type="range" min="0.05" max="1" step="0.01" data-osd="OsdOpacity"></div>
            <div class="field"><label>文字透明度</label><input class="range" type="range" min="0.05" max="1" step="0.01" data-osd="OsdContentOpacity"></div>
            <label class="switch"><span>启用淡入</span><input type="checkbox" data-osd="OsdEnableFadeIn"></label><input class="input" type="number" min="0" max="2000" data-osd="OsdFadeInDuration">
            <label class="switch"><span>启用淡出</span><input type="checkbox" data-osd="OsdEnableFadeOut"></label><input class="input" type="number" min="0" max="2000" data-osd="OsdFadeOutDuration">
            <div class="field" style="margin-top:12px"><label>显示时长</label><input class="input" type="number" min="200" max="10000" data-osd="OsdDisplayDuration"></div>
          </div>
        </div>
        <div class="card"><h3>位置与预览</h3>
          <div class="row"><span>X:</span><input class="input" style="width:110px" type="number" data-osd="OsdPositionX"><span>Y:</span><input class="input" style="width:110px" type="number" data-osd="OsdPositionY"><label class="row"><input type="checkbox" data-osd="OsdIsLocked">锁定位置</label><button class="btn secondary" data-action="resetOsdPosition">重置位置</button></div>
          <label class="switch"><span>显示状态圆点</span><input type="checkbox" data-osd="OsdShowStatusDot"></label>
          <div class="row nudge"><button class="btn secondary" data-action="nudgeOsd" data-direction="Left">←</button><button class="btn secondary" data-action="nudgeOsd" data-direction="Right">→</button><button class="btn secondary" data-action="nudgeOsd" data-direction="Up">↑</button><button class="btn secondary" data-action="nudgeOsd" data-direction="Down">↓</button></div>
          <div class="row" style="margin-top:12px"><label><input type="radio" name="previewStyle" value="MicOn" checked> 开启样式</label><label><input type="radio" name="previewStyle" value="MicMuted"> 静音样式</label></div>
          <div class="preview" style="margin-top:12px"><div id="previewPanel" class="previewPanel"><span id="previewDot" class="dot"></span><span id="previewText">麦克风已开启</span></div></div>
          <div class="row" style="margin-top:12px"><button id="previewToggle" class="btn" data-action="togglePreview">开启预览</button><button class="btn secondary" data-action="resetOsd">重置 OSD</button></div>
        </div>
      </div>
    </section>
    <footer class="footer"><button class="btn" data-action="save">保存</button><button class="btn secondary" data-action="cancel">取消</button></footer>
  </main>
</div>
<div id="toast" class="toast"></div>
<script>
let state=null, activeTab='hotkey', capturing=false;
const post=(msg)=>chrome.webview.postMessage({...msg,tab:activeTab});
const propMap={EnableOsd:'enableOsd',EnableSound:'enableSound',RunOnStartup:'runOnStartup',MicOnSoundPath:'micOnSoundPath',MicMutedSoundPath:'micMutedSoundPath',MicOnVolume:'micOnVolume',MicMutedVolume:'micMutedVolume',MicrophoneMuteAll:'microphoneMuteAll'};
const osdMap={OsdMicOnText:'micOnText',OsdMicMutedText:'micMutedText',OsdMicOnColor:'micOnColor',OsdMicMutedColor:'micMutedColor',OsdWidth:'width',OsdHeight:'height',OsdFontSize:'fontSize',OsdOpacity:'opacity',OsdContentOpacity:'contentOpacity',OsdShowStatusDot:'showStatusDot',OsdEnableFadeIn:'enableFadeIn',OsdEnableFadeOut:'enableFadeOut',OsdFadeInDuration:'fadeInDuration',OsdFadeOutDuration:'fadeOutDuration',OsdDisplayDuration:'displayDuration',OsdIsLocked:'isLocked',OsdPositionX:'positionX',OsdPositionY:'positionY'};
function val(el){if(el.type==='checkbox')return el.checked;if(el.type==='number'||el.type==='range')return el.value===''?null:Number(el.value);return el.value}
function setVal(el,v){if(el.type==='checkbox')el.checked=!!v;else el.value=(v??'')}
function switchTab(tab){activeTab=tab;document.querySelectorAll('.nav button').forEach(b=>b.classList.toggle('active',b.dataset.tab===tab));document.querySelectorAll('.page').forEach(p=>p.classList.toggle('active',p.id===tab))}
function render(s){state=s;document.getElementById('hotkeyDisplay').textContent=s.hotkeyDisplay;document.querySelectorAll('[data-field]').forEach(el=>setVal(el,s[propMap[el.dataset.field]]));document.querySelectorAll('[data-osd]').forEach(el=>setVal(el,s.osd[osdMap[el.dataset.osd]]));document.getElementById('micOnVolText').textContent=Math.round(s.micOnVolume*100)+'%';document.getElementById('micMutedVolText').textContent=Math.round(s.micMutedVolume*100)+'%';document.getElementById('osdWidthText').textContent=Math.round(s.osd.width)+'px';document.getElementById('osdHeightText').textContent=Math.round(s.osd.height)+'px';document.getElementById('osdFontText').textContent=Math.round(s.osd.fontSize);document.querySelectorAll('[data-osd="OsdPositionX"],[data-osd="OsdPositionY"]').forEach(el=>el.disabled=s.osd.isLocked);renderDevices(s);renderPreview(s)}
function renderDevices(s){const box=document.getElementById('devices');box.innerHTML='';if(!s.microphoneDevices.length){box.innerHTML='<div class="device"><span class="hint">没有检测到可用麦克风</span></div>';return}s.microphoneDevices.forEach(d=>{const row=document.createElement('label');row.className='device'+(!s.microphoneSelectionEnabled?' disabled':'');row.innerHTML=`<input type="checkbox" ${d.isSelected?'checked':''} ${!s.microphoneSelectionEnabled?'disabled':''}><span>${escapeHtml(d.displayName)}</span>`;row.querySelector('input').addEventListener('change',e=>post({action:'setDeviceSelected',deviceId:d.id,selected:e.target.checked}));box.appendChild(row)})}
function renderPreview(s){const muted=s.preview.selectedStyle==='MicMuted';const p=document.getElementById('previewPanel');const text=document.getElementById('previewText');const dot=document.getElementById('previewDot');p.style.width=s.osd.width+'px';p.style.height=s.osd.height+'px';p.style.maxWidth='92%';p.style.background=muted?s.osd.micMutedColor:s.osd.micOnColor;p.style.opacity=s.osd.opacity;text.style.opacity=s.osd.contentOpacity;text.style.fontSize=s.osd.fontSize+'px';text.textContent=muted?s.osd.micMutedText:s.osd.micOnText;dot.style.display=s.osd.showStatusDot?'inline-block':'none';document.getElementById('previewToggle').textContent=s.preview.running?'停止预览':'开启预览';document.querySelectorAll('input[name="previewStyle"]').forEach(r=>r.checked=r.value===s.preview.selectedStyle)}
function toast(message,kind='info'){const t=document.getElementById('toast');t.textContent=message;t.className='toast show '+kind;setTimeout(()=>t.className='toast '+kind,2300)}
function escapeHtml(v){return String(v).replace(/[&<>"]/g,c=>({'&':'&','<':'<','>':'>','"':'"'}[c]))}
document.querySelectorAll('.nav button').forEach(b=>b.addEventListener('click',()=>switchTab(b.dataset.tab)));
document.querySelectorAll('[data-field]').forEach(el=>el.addEventListener('input',()=>post({action:'update',patch:{[el.dataset.field]:val(el)}})));
document.querySelectorAll('[data-osd]').forEach(el=>el.addEventListener('input',()=>post({action:'update',patch:{[el.dataset.osd]:val(el)}})));
document.querySelectorAll('[data-action]').forEach(el=>el.addEventListener('click',()=>post({action:el.dataset.action,target:el.dataset.target,direction:el.dataset.direction})));
document.querySelectorAll('input[name="previewStyle"]').forEach(el=>el.addEventListener('change',()=>post({action:'setPreviewStyle',target:el.value})));
document.getElementById('captureHotkey').addEventListener('click',()=>{capturing=true;document.getElementById('hotkeyDisplay').classList.add('capture');document.getElementById('hotkeyHint').textContent='等待按键输入... (ESC 取消)'});
window.addEventListener('keydown',e=>{if(!capturing)return;e.preventDefault();if(e.key==='Escape'){capturing=false;document.getElementById('hotkeyDisplay').classList.remove('capture');document.getElementById('hotkeyHint').textContent='已取消';return}const mods=[];if(e.ctrlKey)mods.push('Control');if(e.shiftKey)mods.push('Shift');if(e.altKey)mods.push('Alt');if(e.metaKey)mods.push('Windows');const skip=['Control','Shift','Alt','Meta'];if(skip.includes(e.key))return;const key=normalizeKey(e.key);capturing=false;document.getElementById('hotkeyDisplay').classList.remove('capture');document.getElementById('hotkeyHint').textContent='已设置';post({action:'setHotkey',hotkey:{key,modifiers:mods}})});
function normalizeKey(k){if(k.length===1)return k.toUpperCase();const map={ArrowUp:'Up',ArrowDown:'Down',ArrowLeft:'Left',ArrowRight:'Right',' ':'Space',Escape:'Escape',Enter:'Enter',Tab:'Tab',Backspace:'Back',Delete:'Delete',Insert:'Insert',Home:'Home',End:'End',PageUp:'PageUp',PageDown:'PageDown'};return map[k]||k}
chrome.webview.addEventListener('message',ev=>{const m=ev.data;if(m.type==='state'){if(m.activeTab)switchTab(m.activeTab);render(m.state)}else if(m.type==='toast')toast(m.message,m.kind)});
post({action:'ready'});
</script>
</body>
</html>
""";

    private sealed class WebCommand
    {
        public string? Action { get; set; }
        public string? Tab { get; set; }
        public string? Target { get; set; }
        public string? Direction { get; set; }
        public string? Url { get; set; }
        public string? DeviceId { get; set; }
        public bool Selected { get; set; }
        public WebHotkey? Hotkey { get; set; }
        public Dictionary<string, JsonElement>? Patch { get; set; }
    }

    private sealed class WebHotkey
    {
        public string? Key { get; set; }
        public string[]? Modifiers { get; set; }
    }
}
