using System.IO;
using MicMuteTool.Models;
using NAudio.Wave;

namespace MicMuteTool.Services;

public class SoundPlayerService : IDisposable
{
    private readonly object _syncRoot = new();
    private WaveOutEvent? _waveOut;
    private AudioFileReader? _audioFile;

    public void Play(SoundSetting? setting)
    {
        if (setting is null || string.IsNullOrWhiteSpace(setting.FilePath))
        {
            return;
        }

        if (!File.Exists(setting.FilePath))
        {
            return;
        }

        lock (_syncRoot)
        {
            StopPlaybackLocked();

            try
            {
                _audioFile = new AudioFileReader(setting.FilePath)
                {
                    Volume = (float)Math.Clamp(setting.Volume, 0.0, 1.0)
                };
                _waveOut = new WaveOutEvent();
                _waveOut.PlaybackStopped += OnPlaybackStopped;
                _waveOut.Init(_audioFile);
                _waveOut.Play();
            }
            catch
            {
                StopPlaybackLocked();
            }
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (_syncRoot)
        {
            StopPlaybackLocked();
        }
    }

    private void StopPlaybackLocked()
    {
        if (_waveOut is not null)
        {
            _waveOut.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                _waveOut.Stop();
            }
            catch
            {
                // 忽略停止异常
            }
            _waveOut.Dispose();
            _waveOut = null;
        }

        _audioFile?.Dispose();
        _audioFile = null;
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            StopPlaybackLocked();
        }
    }
}
