using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThermalGuard.Data.Entities;

namespace ThermalGuard.Data.Configurations;

public sealed class ModbusSettingEntityConfiguration : IEntityTypeConfiguration<ModbusSettingEntity>
{
    public void Configure(EntityTypeBuilder<ModbusSettingEntity> entity)
    {
        entity.ToTable("modbus_setting");
        entity.HasKey(setting => setting.CameraId);
        entity.Property(setting => setting.CameraId).HasColumnName("camera_id");
        entity.Property(setting => setting.Enabled).HasColumnName("enabled");
        entity.Property(setting => setting.Host).HasColumnName("host").IsRequired();
        entity.Property(setting => setting.Port).HasColumnName("port");
        entity.Property(setting => setting.SlaveId).HasColumnName("slave_id");
        entity.Property(setting => setting.RegisterAddress).HasColumnName("register_address");
        entity.Property(setting => setting.AlarmRegisterAddress).HasColumnName("alarm_register_address");
        entity.Property(setting => setting.AccessMode).HasColumnName("access_mode").IsRequired();
        entity.Property(setting => setting.EnabledRegionMask).HasColumnName("enabled_region_mask");
        entity.Property(setting => setting.PollIntervalMs).HasColumnName("poll_interval_ms");
        entity.HasOne(setting => setting.Camera).WithOne()
            .HasForeignKey<ModbusSettingEntity>(setting => setting.CameraId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
