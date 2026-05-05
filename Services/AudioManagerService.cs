using System.Collections.Generic;
using System.Linq;
using MicMuteTool.Models;
using NAudio.CoreAudioApi;

namespace MicMuteTool.Services;

public class AudioManagerService : IDisposable
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();

    public bool GetMuteState(MicrophoneSettings? settings = null)
    {
        using var device = GetReferenceDevice(settings);
        return device?.AudioEndpointVolume.Mute ?? false;
    }

    public bool ToggleMute(MicrophoneSettings? settings = null)
    {
        var current = GetMuteState(settings);
        var newState = !current;
        ApplyMuteState(newState, settings);
        return newState;
    }

    public void SetMute(bool mute, MicrophoneSettings? settings = null)
    {
        ApplyMuteState(mute, settings);
    }

    public IReadOnlyList<MicrophoneDeviceInfo> GetMicrophoneDevices()
    {
        var list = new List<MicrophoneDeviceInfo>();
        MMDeviceCollection? devices = null;
        try
        {
            devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in devices)
            {
                try
                {
                    list.Add(new MicrophoneDeviceInfo
                    {
                        Id = device.ID,
                        Name = device.FriendlyName,
                        IsAvailable = device.State == DeviceState.Active
                    });
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch
        {
            // ignore enumeration failures
        }

        return list;
    }

    private void ApplyMuteState(bool mute, MicrophoneSettings? settings)
    {
        var targetIds = GetTargetDeviceIds(settings).ToList();
        var applied = false;

        foreach (var id in targetIds)
        {
            using var device = GetDeviceById(id);
            if (device is null)
            {
                continue;
            }

            try
            {
                device.AudioEndpointVolume.Mute = mute;
                applied = true;
            }
            catch
            {
                // ignore device-level failures
            }
        }

        if (!applied)
        {
            using var fallback = GetDefaultCaptureDevice();
            if (fallback is null)
            {
                throw new InvalidOperationException("未检测到任何可用的麦克风设备。");
            }

            fallback.AudioEndpointVolume.Mute = mute;
        }
    }

    private IEnumerable<string> GetTargetDeviceIds(MicrophoneSettings? settings)
    {
        if (settings is null)
        {
            yield break;
        }

        if (settings.MuteAll || settings.SelectedDeviceIds.Count == 0)
        {
            foreach (var id in EnumerateActiveDeviceIds())
            {
                yield return id;
            }
            yield break;
        }

        foreach (var id in settings.SelectedDeviceIds)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            yield return id;
        }
    }

    private IEnumerable<string> EnumerateActiveDeviceIds()
    {
        var ids = new List<string>();
        MMDeviceCollection? devices = null;
        try
        {
            devices = _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in devices)
            {
                try
                {
                    ids.Add(device.ID);
                }
                finally
                {
                    device.Dispose();
                }
            }
        }
        catch
        {
            // ignore enumeration failures
        }

        return ids;
    }

    private MMDevice? GetReferenceDevice(MicrophoneSettings? settings)
    {
        foreach (var id in GetTargetDeviceIds(settings))
        {
            var device = GetDeviceById(id);
            if (device is not null)
            {
                return device;
            }
        }

        return GetDefaultCaptureDevice();
    }

    private MMDevice? GetDeviceById(string id)
    {
        try
        {
            return _deviceEnumerator.GetDevice(id);
        }
        catch
        {
            return null;
        }
    }

    private MMDevice? GetDefaultCaptureDevice()
    {
        try
        {
            return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _deviceEnumerator.Dispose();
    }
}

