using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThermalGuard.Data.Entities;

namespace ThermalGuard.Data.Configurations;

public sealed class CameraEntityConfiguration : IEntityTypeConfiguration<CameraEntity>
{
    public void Configure(EntityTypeBuilder<CameraEntity> entity)
    {
        entity.ToTable("camera");
        entity.HasKey(camera => camera.Id);
        entity.Property(camera => camera.Id).HasColumnName("id");
        entity.Property(camera => camera.Name).HasColumnName("name").IsRequired();
        entity.Property(camera => camera.Rtsp).HasColumnName("rtsp").IsRequired();
        entity.Property(camera => camera.SourceType).HasColumnName("source_type")
            .HasDefaultValue("RTSP 串流").IsRequired();
        entity.Property(camera => camera.ImagingType).HasColumnName("imaging_type")
            .HasDefaultValue("可見光").IsRequired();
        entity.Property(camera => camera.StreamType).HasColumnName("stream_type")
            .HasDefaultValue("主碼流").IsRequired();
        entity.Property(camera => camera.SortOrder).HasColumnName("sort_order");
        entity.HasIndex(camera => camera.SortOrder).IsUnique();
    }
}
