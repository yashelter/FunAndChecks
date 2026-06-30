using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class CourseTaskConfiguration : IEntityTypeConfiguration<CourseTask>
{
    public void Configure(EntityTypeBuilder<CourseTask> builder)
    {
        builder.ToTable("Tasks");

        builder.Property(t => t.Name).HasMaxLength(200);
        builder.HasIndex(t => new { t.SubjectId, t.Name }).IsUnique();

        builder.HasMany(t => t.Submissions)
            .WithOne(s => s.Task)
            .HasForeignKey(s => s.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
