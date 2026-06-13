using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class GradeComponentConfiguration : IEntityTypeConfiguration<GradeComponent>
{
    public void Configure(EntityTypeBuilder<GradeComponent> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(200);

        builder.HasOne(c => c.Subject)
            .WithMany(s => s.GradeComponents)
            .HasForeignKey(c => c.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Grades)
            .WithOne(g => g.GradeComponent)
            .HasForeignKey(g => g.GradeComponentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
