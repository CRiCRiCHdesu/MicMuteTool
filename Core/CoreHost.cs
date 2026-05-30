using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using MicMuteTool.Ipc;
using MicMuteTool.Models;
using MicMuteTool.Services;
using Forms = System.Windows.Forms;

namespace MicMuteTool.Core;

public sealed class CoreHost : Forms.ApplicationContext
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AudioManagerService _audioManager = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly HotkeyMessageWindow _hotkeyWindow;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private readonly Forms.Timer _hotkeyPollTimer = new() { Interval = 20 };
    private readonly Forms.Timer _muteGuardTimer = new() { Interval = 500 };
    private MicrophoneSettings _microphones = new();
    private HotkeySetting _hotkey = HotkeySetting.Default();
    private IntPtr _keyboardHook = IntPtr.Zero;
    private bool _hotkeyKeyDown;
    private bool _polledHotkeyDown;
    private bool? _intendedMuteState;
    private bool _isEnforcingMute;
    private long _lastToggleTick;

    public CoreHost()
    {
        _keyboardProc = KeyboardHookCallback;
        _hotkeyWindow = new HotkeyMessageWindow(TryToggleFromHotkey);
        _hotkeyPollTimer.Tick += (_, _) => CheckPolledHotkey();
        _muteGuardTimer.Tick += (_, _) => EnforceIntendedMuteState();
        RegisterHotkey(_hotkey);
        InstallKeyboardHook();
        _hotkeyPollTimer.Start();
        _muteGuardTimer.Start();
        StartPipeLoop();
    }

    private void StartPipeLoop()
    {
        _ = Task.Run(async () =>
        {
            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    using var server = NamedPipeServerStreamAcl.Create(
                        CoreIpc.PipeName,
                        PipeDirection.InOut,
                        8,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        0,
                        0,
                        CreatePipeSecurity());
                    await server.WaitForConnectionAsync(_shutdown.Token);
                    await HandleClientAsync(server);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Keep serving after an individual IPC failure.
                }
            }
        });
    }

    private async Task HandleClientAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };

        CoreResponse response;
        try
        {
            var line = await reader.ReadLineAsync();
            var request = string.IsNullOrWhiteSpace(line)
                ? null
                : JsonSerializer.Deserialize<CoreRequest>(line, JsonOptions);

            response = request is null
                ? new CoreResponse { Success = false, Error = "Core 请求为空。" }
                : HandleRequest(request);
        }
        catch (Exception ex)
        {
            response = new CoreResponse { Success = false, Error = ex.Message };
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private CoreResponse HandleRequest(CoreRequest request)
    {
        switch (request.Command)
        {
            case "ping":
                return Ok();
            case "getMuteState":
                UpdateMicrophones(request.Microphones);
                return Ok(GetReportedMuteState());
            case "toggleMute":
                UpdateMicrophones(request.Microphones);
                var toggledState = ToggleMuteState();
                NotifyMuteChanged(toggledState);
                return Ok(toggledState);
            case "setMute":
                UpdateMicrophones(request.Microphones);
                _audioManager.SetMute(request.Mute, _microphones);
                _intendedMuteState = request.Mute;
                NotifyMuteChanged(request.Mute);
                return Ok(request.Mute);
            case "getDevices":
                return new CoreResponse { Success = true, Devices = _audioManager.GetMicrophoneDevices().ToList() };
            case "updateHotkey":
                UpdateMicrophones(request.Microphones);
                _hotkey = request.Hotkey?.Clone() ?? new HotkeySetting { Key = Key.None };
                RegisterHotkey(_hotkey);
                return Ok();
            case "shutdown":
                BeginExit();
                return Ok();
            default:
                return new CoreResponse { Success = false, Error = $"未知 Core 命令：{request.Command}" };
        }
    }

    private void UpdateMicrophones(MicrophoneSettings? microphones)
    {
        if (microphones is not null)
        {
            _microphones = microphones.Clone();
        }
    }

    private void TryToggleFromHotkey()
    {
        var now = Environment.TickCount64;
        if (now - _lastToggleTick < 80)
        {
            return;
        }

        _lastToggleTick = now;
        try
        {
            var newState = ToggleMuteState();
            NotifyMuteChanged(newState);
        }
        catch
        {
            // Core hotkey path is UI-less; direct UI actions still report errors.
        }
    }

    private bool ToggleMuteState()
    {
        var current = _intendedMuteState ?? _audioManager.GetMuteState(_microphones);
        var newState = !current;
        _audioManager.SetMute(newState, _microphones);
        _intendedMuteState = newState;
        return newState;
    }

    private void EnforceIntendedMuteState()
    {
        if (_shutdown.IsCancellationRequested || _isEnforcingMute || _intendedMuteState != true)
        {
            return;
        }

        _isEnforcingMute = true;
        try
        {
            if (!_audioManager.GetMuteState(_microphones))
            {
                _audioManager.SetMute(true, _microphones);
            }
        }
        catch
        {
            // Keep the guard alive even if a device briefly disappears.
        }
        finally
        {
            _isEnforcingMute = false;
        }
    }

    private bool GetReportedMuteState()
    {
        if (_intendedMuteState != true)
        {
            return _audioManager.GetMuteState(_microphones);
        }

        if (!_audioManager.GetMuteState(_microphones))
        {
            _audioManager.SetMute(true, _microphones);
        }

        return true;
    }

    private static void NotifyMuteChanged(bool muteState)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
                using var client = new NamedPipeClientStream(".", CoreIpc.UiEventPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                await client.ConnectAsync(cancellation.Token);
                await using var writer = new StreamWriter(client) { AutoFlush = true };
                var payload = JsonSerializer.Serialize(new CoreEvent
                {
                    Type = "muteChanged",
                    MuteState = muteState
                }, JsonOptions);
                await writer.WriteLineAsync(payload);
            }
            catch
            {
                // UI event delivery is best effort; audio state has already changed.
            }
        });
    }

    private void RegisterHotkey(HotkeySetting hotkey)
    {
        _hotkeyWindow.Register(hotkey);
    }

    private void BeginExit()
    {
        _shutdown.Cancel();
        _hotkeyPollTimer.Stop();
        _muteGuardTimer.Stop();
        UninstallKeyboardHook();
        _hotkeyWindow.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shutdown.Cancel();
            _hotkeyPollTimer.Stop();
            _muteGuardTimer.Stop();
            UninstallKeyboardHook();
            _hotkeyWindow.Dispose();
            _audioManager.Dispose();
            _hotkeyPollTimer.Dispose();
            _muteGuardTimer.Dispose();
            _shutdown.Dispose();
        }

        base.Dispose(disposing);
    }

    private static CoreResponse Ok(bool muteState = false) => new() { Success = true, MuteState = muteState };

    private void InstallKeyboardHook()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            return;
        }

        using var process = System.Diagnostics.Process.GetCurrentProcess();
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
                var pressed = KeyInterop.KeyFromVirtualKey((int)data.vkCode);

                if (_hotkey.Key != Key.None && pressed == _hotkey.Key && IsCurrentModifiersMatch(_hotkey.Modifiers))
                {
                    if (!_hotkeyKeyDown)
                    {
                        _hotkeyKeyDown = true;
                        TryToggleFromHotkey();
                    }
                }
            }
            else if (message == WmKeyUp || message == WmSysKeyUp)
            {
                var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                var released = KeyInterop.KeyFromVirtualKey((int)data.vkCode);
                if (released == _hotkey.Key)
                {
                    _hotkeyKeyDown = false;
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void CheckPolledHotkey()
    {
        if (_hotkey.Key == Key.None)
        {
            _polledHotkeyDown = false;
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(_hotkey.Key);
        if (vk <= 0)
        {
            _polledHotkeyDown = false;
            return;
        }

        var isDown = IsCurrentModifiersMatch(_hotkey.Modifiers) && (GetAsyncKeyState(vk) & 0x8000) != 0;
        if (isDown)
        {
            if (!_polledHotkeyDown)
            {
                _polledHotkeyDown = true;
                TryToggleFromHotkey();
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
        if ((GetAsyncKeyState(VkControl) & 0x8000) != 0) current |= ModifierKeys.Control;
        if ((GetAsyncKeyState(VkShift) & 0x8000) != 0) current |= ModifierKeys.Shift;
        if ((GetAsyncKeyState(VkMenu) & 0x8000) != 0) current |= ModifierKeys.Alt;
        if ((GetAsyncKeyState(VkLwin) & 0x8000) != 0 || (GetAsyncKeyState(VkRwin) & 0x8000) != 0) current |= ModifierKeys.Windows;
        return current == expected;
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        return security;
    }

    private sealed class HotkeyMessageWindow : Forms.NativeWindow, IDisposable
    {
        private const int HotkeyId = 0xA100;
        private const int WmHotkey = 0x0312;
        private readonly Action _onHotkey;

        public HotkeyMessageWindow(Action onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new Forms.CreateParams());
        }

        public void Register(HotkeySetting hotkey)
        {
            UnregisterHotKey(Handle, HotkeyId);
            if (hotkey.Key == Key.None)
            {
                return;
            }

            var modifiers = GetModifierFlags(hotkey.Modifiers);
            var key = (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key);
            RegisterHotKey(Handle, HotkeyId, modifiers, key);
        }

        protected override void WndProc(ref Forms.Message m)
        {
            if (m.Msg == WmHotkey && m.WParam == (IntPtr)HotkeyId)
            {
                _onHotkey();
                return;
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }

        private static uint GetModifierFlags(ModifierKeys modifierKeys)
        {
            uint flags = 0;
            if (modifierKeys.HasFlag(ModifierKeys.Alt)) flags |= 0x0001;
            if (modifierKeys.HasFlag(ModifierKeys.Control)) flags |= 0x0002;
            if (modifierKeys.HasFlag(ModifierKeys.Shift)) flags |= 0x0004;
            if (modifierKeys.HasFlag(ModifierKeys.Windows)) flags |= 0x0008;
            return flags;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
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
    private struct KbdLlHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

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
}
