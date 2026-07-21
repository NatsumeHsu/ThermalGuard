namespace ThermalGuard.Data.Entities;

public sealed class TemperatureRegionSettingEntity
{
    public string CameraId { get; set; } = "";
    public int RegionNumber { get; set; }
    public bool ModbusEnabled { get; set; }
    public bool InternalWarningEnabled { get; set; }
    public bool InternalAlarmEnabled { get; set; }
    public bool InternalRiseEnabled { get; set; }
    public bool ThermalWarningEnabled { get; set; }
    public bool ThermalAlarmEnabled { get; set; }
    public CameraEntity? Camera { get; set; }
}
