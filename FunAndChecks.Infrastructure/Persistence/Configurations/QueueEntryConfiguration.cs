using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class QueueEntryConfiguration : IEntityTypeConfiguration<QueueEntry>
{
    public void Configure(EntityTypeBuilder<QueueEntry> builder)
    {
        builder.HasOne(e => e.QueueEvent)
            .WithMany(ev => ev.Participants)
            .HasForeignKey(e => e.QueueEventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Student)
            .WithMany(s => s.QueueEntries)
            .HasForeignKey(e => e.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        // При удалении админа запись в очереди остаётся, проверяющий обнуляется.
        builder.HasOne(e => e.CurrentAdmin)
            .WithMany()
            .HasForeignKey(e => e.CurrentAdminId)
            .OnDelete(DeleteBehavior.SetNull);

        // Студент не может стоять в одной очереди дважды.
        builder.HasIndex(e => new { e.QueueEventId, e.StudentId }).IsUnique();
    }
}
