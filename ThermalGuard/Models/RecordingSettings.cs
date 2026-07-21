namespace ThermalGuard.Models;

public sealed record RecordingSettings(
    bool Enabled,
    int SegmentSeconds,
    int BufferMinutes,
    int PostEventSeconds,
    string OutputDirectory,
    int ReservedSpaceGb,
    bool OverwriteOldestEvents);
