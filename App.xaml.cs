using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using MicMuteTool.Models;
using MicMuteTool.Services;
using MicMuteTool.Views;
using Forms = System.Windows.Forms;

namespace MicMuteTool;

public partial class App : System.Windows.Application
{
    private const int HotkeyId = 0xA000;

    private readonly SettingsService _settingsService = new();
    private readonly AudioManagerService _audioManagerService = new();
    private readonly SoundPlayerService _soundPlayerService = new();

    private AppSettings _settings = new();
    private Forms.NotifyIcon? _notifyIcon;
    private Icon? _iconMicOn;
    private Icon? _iconMicMuted;
    private Icon? _iconSettings;
    private HwndSource? _hotkeySource;
    private bool _isMuted;
    private OsdWindow? _osdWindow;
    private Forms.ToolStripMenuItem? _settingsMenuItem;
    private Forms.ToolStripMenuItem? _osdToggleMenuItem;
    private Forms.ToolStripMenuItem? _soundToggleMenuItem;
    private bool _updatingMenuChecks;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = _settingsService.Load();
        _isMuted = _audioManagerService.GetMuteState(_settings.Microphones);

        LoadIcons();
        InitializeTrayIcon();
        RegisterGlobalHotkey(_settings.ToggleHotkey);

        _osdWindow = new OsdWindow();
        _osdWindow.ApplySettings(_settings.Osd);
        UpdateStartupRegistration(_settings.RunOnStartup);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;

        _iconMicOn?.Dispose();
        _iconMicMuted?.Dispose();
        _iconSettings?.Dispose();

        if (_hotkeySource is not null)
        {
            UnregisterHotKey(_hotkeySource.Handle, HotkeyId);
            _hotkeySource.Dispose();
        }

        _osdWindow?.Close();
        _audioManagerService.Dispose();

        base.OnExit(e);
    }

    private void LoadIcons()
    {
        var baseDir = AppContext.BaseDirectory;
        var onPath = Path.Combine(baseDir, "Assets", "Icons", "mic_on.ico");
        var offPath = Path.Combine(baseDir, "Assets", "Icons", "mic_off.ico");
        var settingsPath = Path.Combine(baseDir, "Assets", "setting.ico");

        _iconMicOn = File.Exists(onPath) ? new Icon(onPath) : SystemIcons.Information;
        _iconMicMuted = File.Exists(offPath) ? new Icon(offPath) : SystemIcons.Error;
        _iconSettings = File.Exists(settingsPath) ? new Icon(settingsPath) : SystemIcons.Application;
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = GetTrayText(_isMuted),
            Icon = _isMuted ? _iconMicMuted : _iconMicOn
        };

        _notifyIcon.MouseDown += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
            {
                ToggleMicrophone();
            }
        };

        var contextMenu = new Forms.ContextMenuStrip();
        _settingsMenuItem = new Forms.ToolStripMenuItem("设置")
        {
            Image = (_iconSettings ?? SystemIcons.Application).ToBitmap()
        };
        _settingsMenuItem.Click += (_, _) => ShowSettingsWindow(SettingsTab.Hotkey);
        contextMenu.Items.Add(_settingsMenuItem);

        _osdToggleMenuItem = new Forms.ToolStripMenuItem("OSD显示")
        {
            CheckOnClick = true
        };
        _osdToggleMenuItem.CheckedChanged += (_, _) => ToggleOsdFeature();
        contextMenu.Items.Add(_osdToggleMenuItem);

        _soundToggleMenuItem = new Forms.ToolStripMenuItem("提示音通知")
        {
            CheckOnClick = true
        };
        _soundToggleMenuItem.CheckedChanged += (_, _) => ToggleSoundFeature();
        contextMenu.Items.Add(_soundToggleMenuItem);

        contextMenu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => Shutdown();
        contextMenu.Items.Add(exitItem);
        _notifyIcon.ContextMenuStrip = contextMenu;

        UpdateFeatureMenuChecks();
    }

    private string GetTrayText(bool muted) => muted ? "麦克风已静音" : "麦克风已开启";

    private void ToggleMicrophone()
    {
        bool newState;
        try
        {
            newState = _audioManagerService.ToggleMute(_settings.Microphones);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.Message, "MicMuteTool", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _isMuted = newState;
        UpdateTrayIcon();
        PlayStatusSound();
        if (_settings.EnableOsd)
        {
            _osdWindow?.ShowStatus(_isMuted);
        }
        else
        {
            _osdWindow?.HidePreview();
            _osdWindow?.Hide();
        }
    }

    private void UpdateTrayIcon()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Text = GetTrayText(_isMuted);
        _notifyIcon.Icon = _isMuted ? _iconMicMuted : _iconMicOn;
    }

    private void PlayStatusSound()
    {
        if (!_settings.EnableSound)
        {
            return;
        }

        var sound = _isMuted ? _settings.MicMutedSound : _settings.MicOnSound;
        _soundPlayerService.Play(sound);
    }

    private void RegisterGlobalHotkey(HotkeySetting hotkey)
    {
        if (_hotkeySource is not null)
        {
            UnregisterHotKey(_hotkeySource.Handle, HotkeyId);
            _hotkeySource.Dispose();
            _hotkeySource = null;
        }

        if (hotkey.Key == Key.None)
        {
            return;
        }

        var parameters = new HwndSourceParameters("MicMuteToolHotkeyWindow")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            ParentWindow = IntPtr.Zero,
            WindowStyle = unchecked((int)0x80000000)
        };

        _hotkeySource = new HwndSource(parameters);
        _hotkeySource.AddHook(HwndHook);

        var modifiers = GetModifierFlags(hotkey.Modifiers);
        var key = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);

        if (!RegisterHotKey(_hotkeySource.Handle, HotkeyId, modifiers, key))
        {
            System.Windows.MessageBox.Show("注册全局热键失败，请稍后再试。", "MicMuteTool", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowSettingsWindow(SettingsTab tab)
    {
        var settingsCopy = _settings.Clone();
        var window = new SettingsWindow(settingsCopy, tab, ApplySettings, _soundPlayerService, _audioManagerService, _osdWindow);

        if (_osdWindow is not null && _osdWindow.IsLoaded)
        {
            window.Owner = _osdWindow;
        }

        window.ShowDialog();
    }

    private void ApplySettings(AppSettings newSettings)
    {
        _settings = newSettings;
        _settingsService.Save(_settings);
        RegisterGlobalHotkey(_settings.ToggleHotkey);
        _osdWindow?.ApplySettings(_settings.Osd);
        UpdateFeatureMenuChecks();
        UpdateStartupRegistration(_settings.RunOnStartup);
        try
        {
            _audioManagerService.SetMute(_isMuted, _settings.Microphones);
        }
        catch
        {
            // ignore failures when reapplying mute state
        }

        if (!_settings.EnableOsd)
        {
            _osdWindow?.Hide();
        }
    }

    private static uint GetModifierFlags(ModifierKeys modifierKeys)
    {
        uint flags = 0;

        if (modifierKeys.HasFlag(ModifierKeys.Alt))
        {
            flags |= 0x0001;
        }

        if (modifierKeys.HasFlag(ModifierKeys.Control))
        {
            flags |= 0x0002;
        }

        if (modifierKeys.HasFlag(ModifierKeys.Shift))
        {
            flags |= 0x0004;
        }

        if (modifierKeys.HasFlag(ModifierKeys.Windows))
        {
            flags |= 0x0008;
        }

        return flags;
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY && wParam == (IntPtr)HotkeyId)
        {
            ToggleMicrophone();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void UpdateFeatureMenuChecks()
    {
        if (_osdToggleMenuItem is null || _soundToggleMenuItem is null)
        {
            return;
        }

        _updatingMenuChecks = true;
        _osdToggleMenuItem.Checked = _settings.EnableOsd;
        _soundToggleMenuItem.Checked = _settings.EnableSound;
        _updatingMenuChecks = false;
    }

    private void ToggleOsdFeature()
    {
        if (_updatingMenuChecks || _osdToggleMenuItem is null)
        {
            return;
        }

        _settings.EnableOsd = _osdToggleMenuItem.Checked;
        if (!_settings.EnableOsd)
        {
            _osdWindow?.Hide();
        }
        PersistFeatureToggle();
    }

    private void ToggleSoundFeature()
    {
        if (_updatingMenuChecks || _soundToggleMenuItem is null)
        {
            return;
        }

        _settings.EnableSound = _soundToggleMenuItem.Checked;
        PersistFeatureToggle();
    }

    private void PersistFeatureToggle()
    {
        _settingsService.Save(_settings);
    }

    private void UpdateStartupRegistration(bool enable)
    {
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            if (key is null)
            {
                return;
            }

            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "MicMuteTool.exe");
            }

            if (enable)
            {
                key.SetValue("MicMuteTool", exePath);
            }
            else if (key.GetValue("MicMuteTool") is not null)
            {
                key.DeleteValue("MicMuteTool", false);
            }
        }
        catch
        {
            // ignore registry errors
        }
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

