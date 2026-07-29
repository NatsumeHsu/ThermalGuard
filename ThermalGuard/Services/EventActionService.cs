using System.IO;
using System.Media;
using System.Windows;
using ThermalGuard.Models;
using ThermalGuard.Views;

namespace ThermalGuard.Services;

public sealed class EventActionService : IDisposable
{
    public static IReadOnlyList<string> AlarmSounds { get; } =
        ["警報音 1", "警報音 2", "警報音 3"];

    private readonly Dictionary<string, string> _soundingAlarms = [];
    private readonly Dictionary<string, EventAlertWindow> _windows = [];
    private readonly Dictionary<Camera, string> _soundingCameras = [];
    private readonly Dictionary<Camera, EventAlertWindow> _cameraWindows = [];
    private SoundPlayer? _player;
    private MemoryStream? _soundStream;

    public void Execute(Camera camera)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (camera.PlayAlarmSound)
            {
                _soundingCameras[camera] = camera.AlarmSound;
                Play(camera.AlarmSound);
            }

            if (!camera.ShowPopup || _cameraWindows.ContainsKey(camera)) return;
            var window = new EventAlertWindow(camera, () => Acknowledge(camera))
            {
                Owner = Application.Current.MainWindow
            };
            window.Closed += (_, _) => _cameraWindows.Remove(camera);
            _cameraWindows[camera] = window;
            window.Show();
        });
    }

    public void Resolve(Camera camera)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _soundingCameras.Remove(camera);
            RefreshSound();
            if (_cameraWindows.Remove(camera, out var window)) window.Close();
        });
    }

    public void Execute(Camera camera, ActiveRegionAlarm alarm)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var setting = alarm.Settings;
            if (setting.PlayAlarmSound)
            {
                _soundingAlarms[alarm.Key] = setting.AlarmSound;
                Play(setting.AlarmSound);
            }
            if (!setting.ShowPopup || _windows.ContainsKey(alarm.Key)) return;
            var window = new EventAlertWindow(camera, alarm, () => Acknowledge(alarm.Key))
            {
                Owner = Application.Current.MainWindow
            };
            window.Closed += (_, _) => _windows.Remove(alarm.Key);
            _windows[alarm.Key] = window;
            window.Show();
        });
    }

    public void Resolve(string alarmKey)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _soundingAlarms.Remove(alarmKey);
            RefreshSound();
            if (_windows.Remove(alarmKey, out var window)) window.Close();
        });
    }

    public void Acknowledge(string alarmKey)
    {
        _soundingAlarms.Remove(alarmKey);
        RefreshSound();
    }

    public void Acknowledge(Camera camera)
    {
        _soundingCameras.Remove(camera);
        RefreshSound();
    }

    private void RefreshSound()
    {
        if (_soundingAlarms.Count == 0 && _soundingCameras.Count == 0)
        {
            StopSound();
            return;
        }
        Play(_soundingAlarms.Count > 0 ? _soundingAlarms.Values.Last() : _soundingCameras.Values.Last());
    }

    private void Play(string sound)
    {
        StopSound();
        var (firstFrequency, secondFrequency) = sound switch
        {
            "警報音 2" => (880, 660),
            "警報音 3" => (1200, 900),
            _ => (760, 960)
        };
        _soundStream = CreateAlarmWave(firstFrequency, secondFrequency);
        _player = new SoundPlayer(_soundStream);
        _player.PlayLooping();
    }

    private void StopSound()
    {
        _player?.Stop();
        _player?.Dispose();
        _soundStream?.Dispose();
        _player = null;
        _soundStream = null;
    }

    private static MemoryStream CreateAlarmWave(int firstFrequency, int secondFrequency)
    {
        const int sampleRate = 16000;
        const int durationMilliseconds = 800;
        var sampleCount = sampleRate * durationMilliseconds / 1000;
        var dataLength = sampleCount * sizeof(short);
        var stream = new MemoryStream(44 + dataLength);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataLength);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(dataLength);
            for (var i = 0; i < sampleCount; i++)
            {
                var frequency = i < sampleCount / 2 ? firstFrequency : secondFrequency;
                var value = Math.Sin(2 * Math.PI * frequency * i / sampleRate) * short.MaxValue * 0.22;
                writer.Write((short)value);
            }
        }
        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        StopSound();
        foreach (var window in _windows.Values.ToArray()) window.Close();
        foreach (var window in _cameraWindows.Values.ToArray()) window.Close();
        _windows.Clear();
        _cameraWindows.Clear();
        _soundingCameras.Clear();
        _soundingAlarms.Clear();
    }
}
