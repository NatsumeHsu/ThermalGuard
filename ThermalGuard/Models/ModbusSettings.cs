namespace ThermalGuard.Models;

public sealed record ModbusSettings(
    string CameraId,
    bool Enabled,
    string Host,
    int Port,
    byte SlaveId,
    ushort RegisterAddress,
    ushort AlarmRegisterAddress,
    string AccessMode,
    int PollIntervalMs);
