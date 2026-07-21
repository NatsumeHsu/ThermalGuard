namespace ThermalGuard.Data.Entities;

public sealed class RecordingSettingEntity
{
    public int Id { get; set; }
    public bool Enabled { get; set; }
    public int SegmentSeconds { get; set; } = 5;
    public int BufferMinutes { get; set; } = 3;
    public int PostEventSeconds { get; set; } = 10;
    public string OutputDirectory { get; set; } = "";
    public int ReservedSpaceGb { get; set; } = 10;
    public bool OverwriteOldestEvents { get; set; } = true;
}
