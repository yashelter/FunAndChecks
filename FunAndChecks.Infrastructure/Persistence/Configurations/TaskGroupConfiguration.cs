using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class TaskGroupConfiguration : IEntityTypeConfiguration<TaskGroup>
{
    public void Configure(EntityTypeBuilder<TaskGroup> builder)
    {
        builder.ToTable("TaskGroups");

        builder.Property(g => g.Name).HasMaxLength(200);
        builder.HasIndex(g => new { g.SubjectId, g.Name }).IsUnique();

        builder.HasOne(g => g.Subject)
            .WithMany(s => s.TaskGroups)
            .HasForeignKey(g => g.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
