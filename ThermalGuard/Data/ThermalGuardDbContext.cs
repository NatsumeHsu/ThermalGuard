using Microsoft.EntityFrameworkCore;
using ThermalGuard.Data.Entities;

namespace ThermalGuard.Data;

public sealed class ThermalGuardDbContext(DbContextOptions<ThermalGuardDbContext> options) : DbContext(options)
{
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<RecordingSetting> RecordingSettings => Set<RecordingSetting>();
    public DbSet<EventSetting> EventSettings => Set<EventSetting>();
    public DbSet<ModbusSetting> ModbusSettings => Set<ModbusSetting>();
    public DbSet<TemperatureRegionSetting> TemperatureRegionSettings => Set<TemperatureRegionSetting>();
    public DbSet<RegionAlarmSetting> RegionAlarmSettings => Set<RegionAlarmSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Camera>()
            .HasIndex(item => item.SortOrder).IsUnique();

        modelBuilder.Entity<RecordingSetting>()
            .Property(item => item.Id).ValueGeneratedNever();

        var eventSetting = modelBuilder.Entity<EventSetting>();
        eventSetting.HasKey(item => item.CameraId);
        eventSetting.HasOne(item => item.Camera).WithOne()
            .HasForeignKey<EventSetting>(item => item.CameraId)
            .OnDelete(DeleteBehavior.Cascade);

        var modbus = modelBuilder.Entity<ModbusSetting>();
        modbus.HasKey(item => item.CameraId);
        modbus.HasOne(item => item.Camera).WithOne()
            .HasForeignKey<ModbusSetting>(item => item.CameraId)
            .OnDelete(DeleteBehavior.Cascade);

        var region = modelBuilder.Entity<TemperatureRegionSetting>();
        region.HasKey(item => new { item.CameraId, item.RegionNumber });
        region.HasOne(item => item.Camera).WithMany(item => item.TemperatureRegionSettings)
            .HasForeignKey(item => item.CameraId).OnDelete(DeleteBehavior.Cascade);

        var alarm = modelBuilder.Entity<RegionAlarmSetting>();
        alarm.HasKey(item => new { item.CameraId, item.RegionNumber });
        alarm.OwnsOne(item => item.InternalWarning);
        alarm.OwnsOne(item => item.InternalAlarm);
        alarm.OwnsOne(item => item.InternalRise);
        alarm.OwnsOne(item => item.ThermalWarning);
        alarm.OwnsOne(item => item.ThermalAlarm);
        alarm.HasOne(item => item.Camera).WithMany().HasForeignKey(item => item.CameraId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
