namespace ThermalGuard.Data.Entities;

public sealed class Camera
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Rtsp { get; set; } = "";
    public string SourceType { get; set; } = "RTSP 串流";
    public string ImagingType { get; set; } = "可見光";
    public string StreamType { get; set; } = "主碼流";
    public int SortOrder { get; set; }
    public ICollection<TemperatureRegionSetting> TemperatureRegionSettings { get; set; } = [];
}
