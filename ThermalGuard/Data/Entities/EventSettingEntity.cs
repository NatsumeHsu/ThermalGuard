namespace ThermalGuard.Data.Entities;

public sealed class EventSettingEntity
{
    public string CameraId { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public double WarningTemperature { get; set; } = 50;
    public double AlarmTemperature { get; set; } = 70;
    public int TriggerSeconds { get; set; } = 3;
    public double TemperatureRise { get; set; } = 10;
    public int RiseWindowSeconds { get; set; } = 60;
    public int HighTemperatureConfirmCycle { get; set; } = 3;
    public double HighTemperatureDeadband { get; set; } = 2;
    public int RiseConfirmCycle { get; set; } = 3;
    public double RiseDeadband { get; set; } = 1;
    public bool ShowPopup { get; set; } = true;
    public bool PlayAlarmSound { get; set; } = true;
    public string AlarmSound { get; set; } = "警報音 1";
    public int ThermalWarningRegionMask { get; set; }
    public int ThermalAlarmRegionMask { get; set; }
    public bool InternalWarningEnabled { get; set; } = true;
    public bool InternalAlarmEnabled { get; set; } = true;
    public bool InternalRiseEnabled { get; set; } = true;
    public int InternalWarningRegionMask { get; set; }
    public int InternalAlarmRegionMask { get; set; }
    public int InternalRiseRegionMask { get; set; }
    public CameraEntity? Camera { get; set; }
}
