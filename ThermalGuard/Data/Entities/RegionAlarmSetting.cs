namespace ThermalGuard.Data.Entities;

public sealed class RegionAlarmSetting
{
    public string CameraId { get; set; } = "";
    public int RegionNumber { get; set; }
    public InternalEventProfile InternalWarning { get; set; } = new();
    public InternalEventProfile InternalAlarm { get; set; } = new();
    public InternalRiseEventProfile InternalRise { get; set; } = new();
    public ThermalEventProfile ThermalWarning { get; set; } = new();
    public ThermalEventProfile ThermalAlarm { get; set; } = new();
    public Camera? Camera { get; set; }
}

public class EventActionProfile
{
    public bool Enabled { get; set; }
    public bool ShowPopup { get; set; }
    public bool PlayAlarmSound { get; set; }
    public string AlarmSound { get; set; } = "警報音 1";
    public string RetentionMode { get; set; } = "直到確認或事件結束";
    public int DurationSeconds { get; set; } = 10;
}

public sealed class ThermalEventProfile : EventActionProfile;

public class InternalEventProfile : EventActionProfile
{
    public double TriggerValue { get; set; }
    public int ConfirmCycles { get; set; } = 1;
    public double Deadband { get; set; }
}

public sealed class InternalRiseEventProfile : InternalEventProfile
{
    public int RiseWindowSeconds { get; set; } = 60;
}
