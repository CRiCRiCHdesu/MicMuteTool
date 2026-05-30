using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;
using System.Windows.Threading;
using MicMuteTool.Models;
using MicMuteTool.Services;
using MicMuteTool.Views;
using Forms = System.Windows.Forms;

namespace MicMuteTool;

public partial class App : System.Windows.Application
{
    private const int HotkeyId = 0xA000;

    private readonly SettingsService _settingsService = new();
    private readonly CoreClient _coreClient = new();
    private readonly SoundPlayerService _soundPlayerService = new();
    private readonly CoreEventListener _coreEventListener;

    private AppSettings _settings = new();
    private Forms.NotifyIcon? _notifyIcon;
    private Icon? _iconMicOn;
    private Icon? _iconMicMuted;
    private Icon? _iconSettings;
    private TrayMenuWindow? _trayMenuWindow;
    private HwndSource? _hotkeySource;
    private bool _isMuted;
    private OsdWindow? _osdWindow;
    private bool _showLegacySettingsMenuOnce;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private bool _hotkeyKeyDown;
    private long _lastToggleTick;
    private readonly DispatcherTimer _hotkeyPollTimer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private readonly DispatcherTimer _coreStatePollTimer = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    private bool _polledHotkeyDown;

    public App()
    {
        _coreEventListener = new CoreEventListener(OnCoreMuteChanged);
        _keyboardProc = KeyboardHookCallback;
        _hotkeyPollTimer.Tick += (_, _) => CheckPolledHotkey();
        _coreStatePollTimer.Tick += (_, _) => PollCoreMuteState();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = _settingsService.Load();

        LoadIcons();
        InitializeTrayIcon();

        _osdWindow = new OsdWindow();
        _osdWindow.ApplySettings(_settings.Osd);
        UpdateStartupRegistration(_settings.RunOnStartup);
        _coreEventListener.Start();
        _coreStatePollTimer.Start();
        _ = Dispatcher.InvokeAsync(InitializeCoreConnection);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayMenuWindow?.Close();
        _trayMenuWindow = null;

        _notifyIcon?.Dispose();
        _notifyIcon = null;

        _iconMicOn?.Dispose();
        _iconMicMuted?.Dispose();
        _iconSettings?.Dispose();

        _osdWindow?.Close();
        _coreStatePollTimer.Stop();
        _coreEventListener.Dispose();
        _coreClient.ShutdownCore();

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

    private static Icon CreateMicrophoneTrayIcon(bool muted)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var pen = new Pen(muted ? Color.FromArgb(234, 51, 35) : Color.FromArgb(227, 227, 227), 3.2f)
        {
            StartCap = System.Drawing.Drawing2D.LineCap.Round,
            EndCap = System.Drawing.Drawing2D.LineCap.Round
        };
        using var brush = new SolidBrush(muted ? Color.FromArgb(234, 51, 35) : Color.FromArgb(227, 227, 227));

        graphics.DrawLine(pen, 16, 23, 16, 29);
        graphics.DrawLine(pen, 11, 29, 21, 29);
        graphics.DrawArc(pen, 7, 13, 18, 14, 0, 180);
        using (var microphoneBody = CreateRoundedRectanglePath(new RectangleF(11, 3, 10, 18), 5))
        {
            graphics.FillPath(brush, microphoneBody);
        }

        if (muted)
        {
            graphics.DrawLine(pen, 5, 5, 27, 27);
        }

        var handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectanglePath(RectangleF rectangle, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
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
            else if (args.Button == Forms.MouseButtons.Right)
            {
                ShowTrayMenu();
            }
        };
    }

    private void ShowTrayMenu()
    {
        _trayMenuWindow?.Close();

        var showLegacy = _showLegacySettingsMenuOnce;
        _showLegacySettingsMenuOnce = false;
        var state = new TrayMenuState(_isMuted, _settings.EnableOsd, _settings.EnableSound, showLegacy);
        var actions = new TrayMenuActions(
            () => ShowWebSettingsWindow(SettingsTab.Hotkey),
            () => ShowSettingsWindow(SettingsTab.Hotkey),
            ToggleOsdFeature,
            ToggleSoundFeature,
            Shutdown);

        _trayMenuWindow = new TrayMenuWindow(state, actions);
        _trayMenuWindow.Closed += (_, _) => _trayMenuWindow = null;
        _trayMenuWindow.ShowAt(new System.Windows.Point(Forms.Cursor.Position.X, Forms.Cursor.Position.Y));
    }

    private string GetTrayText(bool muted) => muted ? "麦克风已静音" : "麦克风已开启";

    private void ToggleMicrophone()
    {
        bool newState;
        try
        {
            EnsureCoreConnection();
            newState = _coreClient.ToggleMute(_settings.Microphones);
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

    private void PollCoreMuteState()
    {
        bool latest;
        try
        {
            latest = _coreClient.GetMuteState(_settings.Microphones);
        }
        catch
        {
            return;
        }

        if (latest == _isMuted)
        {
            return;
        }

        ApplyMuteStateFeedback(latest);
    }

    private void OnCoreMuteChanged(bool muteState)
    {
        Dispatcher.BeginInvoke(() => ApplyMuteStateFeedback(muteState));
    }

    private void ApplyMuteStateFeedback(bool muteState)
    {
        if (muteState == _isMuted)
        {
            return;
        }

        _isMuted = muteState;
        UpdateTrayIcon();
        PlayStatusSound();
        if (_settings.EnableOsd)
        {
            _osdWindow?.ShowStatus(_isMuted);
        }
    }

    private void InitializeCoreConnection()
    {
        try
        {
            EnsureCoreConnection();
            _coreClient.UpdateHotkey(_settings.ToggleHotkey, _settings.Microphones);
            _isMuted = _coreClient.GetMuteState(_settings.Microphones);
            UpdateTrayIcon();
        }
        catch
        {
            // Keep the UI alive even if the elevated Core process is unavailable.
        }
    }

    private void EnsureCoreConnection()
    {
        if (!CoreClient.TryPing(TimeSpan.FromMilliseconds(250)))
        {
            CoreProcessLauncher.EnsureCoreStarted();
        }
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
        RegisterRawInputSink(_hotkeySource.Handle);

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
        var window = new SettingsWindow(settingsCopy, tab, ApplySettings, _soundPlayerService, _coreClient, _osdWindow);

        if (_osdWindow is not null && _osdWindow.IsLoaded)
        {
            window.Owner = _osdWindow;
        }

        window.ShowDialog();
    }

    private void ShowWebSettingsWindow(SettingsTab tab)
    {
        var settingsCopy = _settings.Clone();
        var window = new WebSettingsWindow(settingsCopy, tab, ApplySettings, _soundPlayerService, _coreClient, _osdWindow, EnableLegacySettingsMenuOnce);

        if (_osdWindow is not null && _osdWindow.IsLoaded)
        {
            window.Owner = _osdWindow;
        }

        window.ShowDialog();
    }

    private void EnableLegacySettingsMenuOnce()
    {
        _showLegacySettingsMenuOnce = true;
    }

    private void ApplySettings(AppSettings newSettings)
    {
        _settings = newSettings;
        _settingsService.Save(_settings);
        try
        {
            EnsureCoreConnection();
            _coreClient.UpdateHotkey(_settings.ToggleHotkey, _settings.Microphones);
        }
        catch
        {
            // Core may be unavailable; UI settings are still persisted.
        }
        _osdWindow?.ApplySettings(_settings.Osd);
        UpdateStartupRegistration(_settings.RunOnStartup);
        try
        {
            EnsureCoreConnection();
            _coreClient.SetMute(_isMuted, _settings.Microphones);
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
        const int WM_INPUT = 0x00FF;

        if (msg == WM_HOTKEY && wParam == (IntPtr)HotkeyId)
        {
            TryToggleFromHotkeySource();
            handled = true;
        }
        else if (msg == WM_INPUT)
        {
            HandleRawInput(lParam);
        }

        return IntPtr.Zero;
    }

    private void RegisterRawInputSink(IntPtr hwnd)
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x06,
                Flags = 0x00000100,
                Target = hwnd
            }
        };

        RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    private void HandleRawInput(IntPtr lParam)
    {
        var configured = _settings.ToggleHotkey;
        if (configured.Key == Key.None)
        {
            return;
        }

        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(lParam, 0x10000003, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, 0x10000003, buffer, ref size, headerSize) != size)
            {
                return;
            }

            var raw = Marshal.PtrToStructure<RawInput>(buffer);
            if (raw.Header.Type != 1)
            {
                return;
            }

            var key = KeyInterop.KeyFromVirtualKey(raw.Keyboard.VKey);
            var keyUp = (raw.Keyboard.Flags & 0x0001) != 0;

            if (key == configured.Key)
            {
                if (!keyUp)
                {
                    if (!_hotkeyKeyDown && IsCurrentModifiersMatch(configured.Modifiers))
                    {
                        _hotkeyKeyDown = true;
                        TryToggleFromHotkeySource();
                    }
                }
                else
                {
                    _hotkeyKeyDown = false;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
    }

    private void UninstallKeyboardHook()
    {
        if (_keyboardHook == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_keyboardHook);
        _keyboardHook = IntPtr.Zero;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = (int)wParam;
            if (message == WmKeyDown || message == WmSysKeyDown)
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                var configured = _settings.ToggleHotkey;
                var pressed = KeyInterop.KeyFromVirtualKey((int)data.vkCode);

                if (configured.Key != Key.None && pressed == configured.Key && IsCurrentModifiersMatch(configured.Modifiers))
                {
                    if (!_hotkeyKeyDown)
                    {
                        _hotkeyKeyDown = true;
                        TryToggleFromHotkeySource();
                    }
                }
            }
            else if (message == WmKeyUp || message == WmSysKeyUp)
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                var configured = _settings.ToggleHotkey;
                var released = KeyInterop.KeyFromVirtualKey((int)data.vkCode);
                if (released == configured.Key)
                {
                    _hotkeyKeyDown = false;
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void TryToggleFromHotkeySource()
    {
        var now = Environment.TickCount64;
        if (now - _lastToggleTick < 150)
        {
            return;
        }

        _lastToggleTick = now;
        ToggleMicrophone();
    }

    private void CheckPolledHotkey()
    {
        var configured = _settings.ToggleHotkey;
        if (configured.Key == Key.None)
        {
            _polledHotkeyDown = false;
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(configured.Key);
        if (vk <= 0)
        {
            _polledHotkeyDown = false;
            return;
        }

        var isDown = IsCurrentModifiersMatch(configured.Modifiers) && (GetAsyncKeyState(vk) & 0x8000) != 0;
        if (isDown)
        {
            if (!_polledHotkeyDown)
            {
                _polledHotkeyDown = true;
                TryToggleFromHotkeySource();
            }
        }
        else
        {
            _polledHotkeyDown = false;
        }
    }

    private static bool IsCurrentModifiersMatch(ModifierKeys expected)
    {
        var current = ModifierKeys.None;

        if ((GetAsyncKeyState(VkControl) & 0x8000) != 0)
        {
            current |= ModifierKeys.Control;
        }

        if ((GetAsyncKeyState(VkShift) & 0x8000) != 0)
        {
            current |= ModifierKeys.Shift;
        }

        if ((GetAsyncKeyState(VkMenu) & 0x8000) != 0)
        {
            current |= ModifierKeys.Alt;
        }

        if ((GetAsyncKeyState(VkLwin) & 0x8000) != 0 || (GetAsyncKeyState(VkRwin) & 0x8000) != 0)
        {
            current |= ModifierKeys.Windows;
        }

        return current == expected;
    }

    private void ToggleOsdFeature()
    {
        _settings.EnableOsd = !_settings.EnableOsd;
        if (!_settings.EnableOsd)
        {
            _osdWindow?.Hide();
        }
        PersistFeatureToggle();
    }

    private void ToggleSoundFeature()
    {
        _settings.EnableSound = !_settings.EnableSound;
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

    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawKeyboard Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
}
