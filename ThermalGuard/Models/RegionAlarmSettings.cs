namespace ThermalGuard.Models;

public sealed record RegionAlarmSettings(
    string CameraId,
    int RegionNumber,
    string EventType,
    bool Enabled,
    double TriggerValue,
    int TriggerSeconds,
    int ConfirmCycles,
    double Deadband,
    int RiseWindowSeconds,
    bool ShowPopup,
    bool PlayAlarmSound,
    string AlarmSound,
    string RetentionMode,
    int DurationSeconds);

public sealed record ActiveRegionAlarm(
    string Key,
    int RegionNumber,
    string EventType,
    float Temperature,
    RegionAlarmSettings Settings);
