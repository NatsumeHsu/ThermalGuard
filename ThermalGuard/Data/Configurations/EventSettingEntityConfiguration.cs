using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThermalGuard.Data.Entities;

namespace ThermalGuard.Data.Configurations;

public sealed class EventSettingEntityConfiguration : IEntityTypeConfiguration<EventSettingEntity>
{
    public void Configure(EntityTypeBuilder<EventSettingEntity> entity)
    {
        entity.ToTable("event_setting");
        entity.HasKey(setting => setting.CameraId);
        entity.Property(setting => setting.CameraId).HasColumnName("camera_id");
        entity.Property(setting => setting.Enabled).HasColumnName("enabled");
        entity.Property(setting => setting.WarningTemperature).HasColumnName("warning_temperature");
        entity.Property(setting => setting.AlarmTemperature).HasColumnName("alarm_temperature");
        entity.Property(setting => setting.TriggerSeconds).HasColumnName("trigger_seconds");
        entity.Property(setting => setting.TemperatureRise).HasColumnName("temperature_rise");
        entity.Property(setting => setting.RiseWindowSeconds).HasColumnName("rise_window_seconds");
        entity.Property(setting => setting.HighTemperatureConfirmCycle).HasColumnName("high_temperature_confirm_cycle");
        entity.Property(setting => setting.HighTemperatureDeadband).HasColumnName("high_temperature_deadband");
        entity.Property(setting => setting.RiseConfirmCycle).HasColumnName("rise_confirm_cycle");
        entity.Property(setting => setting.RiseDeadband).HasColumnName("rise_deadband");
        entity.Property(setting => setting.ShowPopup).HasColumnName("show_popup");
        entity.Property(setting => setting.PlayAlarmSound).HasColumnName("play_alarm_sound");
        entity.Property(setting => setting.AlarmSound).HasColumnName("alarm_sound").IsRequired();
        entity.Property(setting => setting.ThermalWarningRegionMask).HasColumnName("thermal_warning_region_mask");
        entity.Property(setting => setting.ThermalAlarmRegionMask).HasColumnName("thermal_alarm_region_mask");
        entity.Property(setting => setting.InternalWarningEnabled).HasColumnName("internal_warning_enabled");
        entity.Property(setting => setting.InternalAlarmEnabled).HasColumnName("internal_alarm_enabled");
        entity.Property(setting => setting.InternalRiseEnabled).HasColumnName("internal_rise_enabled");
        entity.Property(setting => setting.InternalWarningRegionMask).HasColumnName("internal_warning_region_mask");
        entity.Property(setting => setting.InternalAlarmRegionMask).HasColumnName("internal_alarm_region_mask");
        entity.Property(setting => setting.InternalRiseRegionMask).HasColumnName("internal_rise_region_mask");
        entity.HasOne(setting => setting.Camera)
            .WithOne()
            .HasForeignKey<EventSettingEntity>(setting => setting.CameraId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
