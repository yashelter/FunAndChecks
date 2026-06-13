using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class QueueEventConfiguration : IEntityTypeConfiguration<QueueEvent>
{
    public void Configure(EntityTypeBuilder<QueueEvent> builder)
    {
        builder.Property(e => e.Name).HasMaxLength(200);

        builder.HasOne(e => e.Subject)
            .WithMany()
            .HasForeignKey(e => e.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
