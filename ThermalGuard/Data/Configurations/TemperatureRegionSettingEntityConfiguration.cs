using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThermalGuard.Data.Entities;

namespace ThermalGuard.Data.Configurations;

public sealed class TemperatureRegionSettingEntityConfiguration : IEntityTypeConfiguration<TemperatureRegionSettingEntity>
{
    public void Configure(EntityTypeBuilder<TemperatureRegionSettingEntity> entity)
    {
        entity.ToTable("temperature_region_setting");
        entity.HasKey(item => new { item.CameraId, item.RegionNumber });
        entity.Property(item => item.CameraId).HasColumnName("camera_id");
        entity.Property(item => item.RegionNumber).HasColumnName("region_number");
        entity.Property(item => item.ModbusEnabled).HasColumnName("modbus_enabled");
        entity.Property(item => item.InternalWarningEnabled).HasColumnName("internal_warning_enabled");
        entity.Property(item => item.InternalAlarmEnabled).HasColumnName("internal_alarm_enabled");
        entity.Property(item => item.InternalRiseEnabled).HasColumnName("internal_rise_enabled");
        entity.Property(item => item.ThermalWarningEnabled).HasColumnName("thermal_warning_enabled");
        entity.Property(item => item.ThermalAlarmEnabled).HasColumnName("thermal_alarm_enabled");
        entity.HasOne(item => item.Camera).WithMany(camera => camera.TemperatureRegionSettings)
            .HasForeignKey(item => item.CameraId).OnDelete(DeleteBehavior.Cascade);
    }
}
