namespace ThermalGuard.Data.Entities;

public sealed class ModbusSettingEntity
{
    public string CameraId { get; set; } = "";
    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 502;
    public byte SlaveId { get; set; } = 1;
    public ushort RegisterAddress { get; set; }
    public ushort AlarmRegisterAddress { get; set; } = 4;
    public string AccessMode { get; set; } = "唯讀";
    public int EnabledRegionMask { get; set; }
    public int PollIntervalMs { get; set; } = 1000;
    public CameraEntity? Camera { get; set; }
}
