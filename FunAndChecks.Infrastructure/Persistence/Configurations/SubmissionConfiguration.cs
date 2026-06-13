using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.HasOne(s => s.Student)
            .WithMany(st => st.Submissions)
            .HasForeignKey(s => s.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Запрещаем удаление админа, у которого есть проверки.
        builder.HasOne(s => s.Admin)
            .WithMany()
            .HasForeignKey(s => s.AdminId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.QueueEvent)
            .WithMany()
            .HasForeignKey(s => s.QueueEventId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => new { s.StudentId, s.TaskId });
    }
}
