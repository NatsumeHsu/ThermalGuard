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

    private readonly Dictionary<Camera, string> _soundingCameras = [];
    private readonly Dictionary<Camera, EventAlertWindow> _windows = [];
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

            if (!camera.ShowPopup || _windows.ContainsKey(camera)) return;
            var window = new EventAlertWindow(camera, () => Acknowledge(camera))
            {
                Owner = Application.Current.MainWindow
            };
            window.Closed += (_, _) => _windows.Remove(camera);
            _windows[camera] = window;
            window.Show();
        });
    }

    public void Resolve(Camera camera)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _soundingCameras.Remove(camera);
            RefreshSound();
            if (_windows.Remove(camera, out var window)) window.Close();
        });
    }

    public void Acknowledge(Camera camera)
    {
        _soundingCameras.Remove(camera);
        RefreshSound();
    }

    private void RefreshSound()
    {
        if (_soundingCameras.Count == 0)
        {
            StopSound();
            return;
        }
        Play(_soundingCameras.Values.Last());
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
        _windows.Clear();
        _soundingCameras.Clear();
    }
}
