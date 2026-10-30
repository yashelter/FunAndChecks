using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class VariantSetConfiguration : IEntityTypeConfiguration<VariantSet>
{
    public void Configure(EntityTypeBuilder<VariantSet> builder)
    {
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.HasIndex(s => s.SubjectId);
        builder.HasOne(s => s.Subject).WithMany().HasForeignKey(s => s.SubjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> builder)
    {
        builder.Property(v => v.Title).HasMaxLength(200);
        builder.Property(v => v.Description).HasMaxLength(2000);
        builder.HasIndex(v => new { v.VariantSetId, v.Number }).IsUnique();
        builder.HasOne(v => v.VariantSet).WithMany(s => s.Variants).HasForeignKey(v => v.VariantSetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class VariantAssignmentConfiguration : IEntityTypeConfiguration<VariantAssignment>
{
    public void Configure(EntityTypeBuilder<VariantAssignment> builder)
    {
        // Один вариант на студента в наборе — гонку двойного клика гасит ключ.
        builder.HasKey(a => new { a.VariantSetId, a.StudentId });
        builder.HasIndex(a => a.VariantId);
        builder.HasIndex(a => a.StudentId);
        builder.HasOne(a => a.VariantSet).WithMany(s => s.Assignments).HasForeignKey(a => a.VariantSetId).OnDelete(DeleteBehavior.Cascade);
        // Выданный вариант удалить нельзя (сервис отвечает variant.in_use). NO ACTION, а не RESTRICT: Postgres проверяет его
        // в конце оператора, поэтому каскад от набора или предмета удаляет варианты и выдачи вместе.
        builder.HasOne(a => a.Variant).WithMany().HasForeignKey(a => a.VariantId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.Student).WithMany().HasForeignKey(a => a.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(a => a.AssignedByAdmin).WithMany().HasForeignKey(a => a.AssignedByAdminId).OnDelete(DeleteBehavior.SetNull);
    }
}
