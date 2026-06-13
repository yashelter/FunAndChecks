using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class AdminSubjectAccessConfiguration : IEntityTypeConfiguration<AdminSubjectAccess>
{
    public void Configure(EntityTypeBuilder<AdminSubjectAccess> builder)
    {
        builder.HasKey(a => new { a.AdminId, a.SubjectId });

        builder.HasOne(a => a.Admin)
            .WithMany()
            .HasForeignKey(a => a.AdminId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Subject)
            .WithMany()
            .HasForeignKey(a => a.SubjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AdminGroupAccessConfiguration : IEntityTypeConfiguration<AdminGroupAccess>
{
    public void Configure(EntityTypeBuilder<AdminGroupAccess> builder)
    {
        builder.HasKey(a => new { a.AdminId, a.GroupId });

        builder.HasOne(a => a.Admin)
            .WithMany()
            .HasForeignKey(a => a.AdminId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.Group)
            .WithMany()
            .HasForeignKey(a => a.GroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
