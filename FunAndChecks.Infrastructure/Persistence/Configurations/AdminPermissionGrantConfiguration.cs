using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public sealed class AdminPermissionGrantConfiguration : IEntityTypeConfiguration<AdminPermissionGrant>
{
    public void Configure(EntityTypeBuilder<AdminPermissionGrant> builder)
    {
        builder.HasKey(grant => new { grant.AdminId, grant.Key });
        builder.Property(grant => grant.Key).HasMaxLength(100);
        builder.HasOne<Admin>().WithMany().HasForeignKey(grant => grant.AdminId).OnDelete(DeleteBehavior.Cascade);
    }
}
