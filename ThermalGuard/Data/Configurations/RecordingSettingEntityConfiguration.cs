using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThermalGuard.Data.Entities;

namespace ThermalGuard.Data.Configurations;

public sealed class RecordingSettingEntityConfiguration : IEntityTypeConfiguration<RecordingSettingEntity>
{
    public void Configure(EntityTypeBuilder<RecordingSettingEntity> entity)
    {
        entity.ToTable("recording_setting");
        entity.HasKey(setting => setting.Id);
        entity.Property(setting => setting.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(setting => setting.Enabled).HasColumnName("enabled");
        entity.Property(setting => setting.SegmentSeconds).HasColumnName("segment_seconds");
        entity.Property(setting => setting.BufferMinutes).HasColumnName("buffer_minutes");
        entity.Property(setting => setting.PostEventSeconds).HasColumnName("post_event_seconds");
        entity.Property(setting => setting.OutputDirectory).HasColumnName("output_directory").IsRequired();
        entity.Property(setting => setting.ReservedSpaceGb).HasColumnName("reserved_space_gb");
        entity.Property(setting => setting.OverwriteOldestEvents).HasColumnName("overwrite_oldest_events");
    }
}
