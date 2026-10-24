using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class AttendanceSessionConfiguration : IEntityTypeConfiguration<AttendanceSession>
{
    public void Configure(EntityTypeBuilder<AttendanceSession> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.HasIndex(s => new { s.SubjectId, s.StartsAt });
        builder.HasOne(s => s.Subject).WithMany().HasForeignKey(s => s.SubjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.CreatedByAdmin).WithMany().HasForeignKey(s => s.CreatedByAdminId).OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(s => s.Groups).WithMany().UsingEntity("AttendanceSessionGroups");
    }
}

public class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.HasKey(r => new { r.SessionId, r.StudentId });
        builder.Property(r => r.GroupName).HasMaxLength(200);
        builder.Property(r => r.Version).IsConcurrencyToken();
        builder.HasIndex(r => new { r.StudentId, r.SessionId });
        builder.HasIndex(r => r.GroupId);
        builder.HasOne(r => r.Session).WithMany(s => s.Records).HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Student).WithMany().HasForeignKey(r => r.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.MarkedByAdmin).WithMany().HasForeignKey(r => r.MarkedByAdminId).OnDelete(DeleteBehavior.SetNull);
    }
}
