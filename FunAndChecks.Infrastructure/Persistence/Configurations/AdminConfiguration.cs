using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class AdminConfiguration : IEntityTypeConfiguration<Admin>
{
    public void Configure(EntityTypeBuilder<Admin> builder)
    {
        builder.ToTable("Admins");

        builder.HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<Admin>(a => a.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(a => a.Color).HasMaxLength(16);
        builder.Property(a => a.Letter).HasMaxLength(8);
    }
}
