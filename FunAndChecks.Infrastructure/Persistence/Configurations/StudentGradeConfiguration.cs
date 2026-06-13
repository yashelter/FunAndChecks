using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class StudentGradeConfiguration : IEntityTypeConfiguration<StudentGrade>
{
    public void Configure(EntityTypeBuilder<StudentGrade> builder)
    {
        builder.Property(g => g.Comment).HasMaxLength(2000);

        builder.HasOne(g => g.Student)
            .WithMany(s => s.Grades)
            .HasForeignKey(g => g.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Запрещаем удаление админа, который выставлял оценки.
        builder.HasOne(g => g.Admin)
            .WithMany()
            .HasForeignKey(g => g.AdminId)
            .OnDelete(DeleteBehavior.Restrict);

        // Одна оценка на пару (колонка, студент).
        builder.HasIndex(g => new { g.GradeComponentId, g.StudentId }).IsUnique();
    }
}
