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

        // Прошлых кругов сколько угодно, но активная запись (Checking = 0, Waiting = 1) — одна:
        // фильтрованный уникальный индекс работает и в PostgreSQL, и в SQLite.
        builder.HasIndex(e => new { e.QueueEventId, e.StudentId })
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1)")
            .HasDatabaseName("IX_QueueEntries_QueueEventId_StudentId_Active");
        builder.HasIndex(e => new { e.QueueEventId, e.StudentId, e.Round });
    }
}
