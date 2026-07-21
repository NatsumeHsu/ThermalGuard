using Microsoft.EntityFrameworkCore;
using ThermalGuard.Data.Entities;

namespace ThermalGuard.Data;

public sealed class ThermalGuardDbContext(DbContextOptions<ThermalGuardDbContext> options) : DbContext(options)
{
    public DbSet<CameraEntity> Cameras => Set<CameraEntity>();
    public DbSet<RecordingSettingEntity> RecordingSettings => Set<RecordingSettingEntity>();
    public DbSet<EventSettingEntity> EventSettings => Set<EventSettingEntity>();
    public DbSet<ModbusSettingEntity> ModbusSettings => Set<ModbusSettingEntity>();
    public DbSet<TemperatureRegionSettingEntity> TemperatureRegionSettings => Set<TemperatureRegionSettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ThermalGuardDbContext).Assembly);
    }
}
