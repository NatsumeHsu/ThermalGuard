using System.Net.Sockets;
using System.Windows;
using NModbus;
using ThermalGuard.Models;

namespace ThermalGuard.Services;

public sealed class ModbusTemperatureService : IAsyncDisposable
{
    private readonly Dictionary<string, Camera> _cameras;
    private CancellationTokenSource? _stop;
    private Task[] _workers = [];

    public ModbusTemperatureService(IEnumerable<Camera> cameras) =>
        _cameras = cameras.ToDictionary(camera => camera.Id, StringComparer.OrdinalIgnoreCase);

    public void AddCamera(Camera camera) => _cameras[camera.Id] = camera;
    public void RemoveCamera(Camera camera) => _cameras.Remove(camera.Id);

    public async Task ApplyAsync(IEnumerable<ModbusSettings> settings)
    {
        await StopAsync();
        _stop = new CancellationTokenSource();
        _workers = settings
            .Where(setting => setting.Enabled && _cameras.ContainsKey(setting.CameraId))
            .Select(setting => PollAsync(_cameras[setting.CameraId], setting, _stop.Token))
            .ToArray();
    }

    private static async Task PollAsync(Camera camera, ModbusSettings setting, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(setting.Host, setting.Port, token);
                using var master = new ModbusFactory().CreateMaster(client);
                master.Transport.ReadTimeout = Math.Max(1000, setting.PollIntervalMs * 2);
                master.Transport.Retries = 1;
                SetStatus(camera, true, "溫度讀取中");

                while (!token.IsCancellationRequested && client.Connected)
                {
                    const ushort registerCount = 105;
                    var registers = setting.AccessMode == "唯讀"
                        ? await master.ReadInputRegistersAsync(1, 0, registerCount)
                        : await master.ReadHoldingRegistersAsync(1, 0, registerCount);
                    var temperatures = new float[21];
                    var eventCodes = new int[21];
                    for (var index = 0; index < temperatures.Length; index++)
                    {
                        var raw = (short)registers[index * 5];
                        temperatures[index] = raw * 0.1f;
                        eventCodes[index] = registers[index * 5 + 4] switch
                        {
                            1 => 1,
                            2 => 2,
                            _ => 0
                        };
                    }
                    _ = Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        camera.UpdateThermalRegions(temperatures, eventCodes);
                    });
                    await Task.Delay(setting.PollIntervalMs, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                SetStatus(camera, false, "Modbus 連線失敗，5 秒後重試");
                try { await Task.Delay(TimeSpan.FromSeconds(5), token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            }
        }
    }

    private static void SetStatus(Camera camera, bool online, string status) =>
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            camera.Online = online;
            camera.TemperatureOnline = online;
            camera.TemperatureStatus = status;
        });

    private async Task StopAsync()
    {
        if (_stop is null) return;
        await _stop.CancelAsync();
        try { await Task.WhenAll(_workers); }
        catch (OperationCanceledException) { }
        _stop.Dispose();
        _stop = null;
        _workers = [];
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
