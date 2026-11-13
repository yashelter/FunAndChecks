using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("UserProfiles");
        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).ValueGeneratedNever();
        builder.Property(p => p.AboutMe).HasMaxLength(2000);
        builder.Property(p => p.GitHubUrl).HasMaxLength(500);
        builder.Property(p => p.PortfolioUrl).HasMaxLength(500);
        builder.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserProfile>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AchievementAward>().WithMany().HasForeignKey(p => p.ActiveAwardId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProfileMediaAssetConfiguration : IEntityTypeConfiguration<ProfileMediaAsset>
{
    public void Configure(EntityTypeBuilder<ProfileMediaAsset> builder)
    {
        builder.ToTable("ProfileMediaAssets", t => t.HasCheckConstraint("CK_ProfileMediaAssets_OneOwner",
            "(\"UserProfileId\" IS NOT NULL AND \"AchievementDefinitionId\" IS NULL) OR (\"UserProfileId\" IS NULL AND \"AchievementDefinitionId\" IS NOT NULL)"));
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.ContentType).HasMaxLength(100);
        builder.Property(m => m.PosterContentType).HasMaxLength(100);
        builder.HasOne<UserProfile>().WithOne().HasForeignKey<ProfileMediaAsset>(m => m.UserProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AchievementDefinition>().WithMany().HasForeignKey(m => m.AchievementDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        // One picture per slot of an achievement (badge, frame, pattern, banner, decor).
        builder.HasIndex(m => new { m.AchievementDefinitionId, m.Slot }).IsUnique()
            .HasFilter("\"AchievementDefinitionId\" IS NOT NULL");
    }
}

public class AchievementDefinitionConfiguration : IEntityTypeConfiguration<AchievementDefinition>
{
    public void Configure(EntityTypeBuilder<AchievementDefinition> builder)
    {
        builder.ToTable("AchievementDefinitions");
        builder.Property(a => a.Name).HasMaxLength(100);
        builder.Property(a => a.Description).HasMaxLength(2000);
        builder.Property(a => a.CreatedByName).HasMaxLength(201);
        builder.Property(a => a.EffectsJson).HasMaxLength(8000);
        builder.Property(a => a.CustomCss).HasMaxLength(8000);
        builder.Property(a => a.CompiledCss).HasMaxLength(64000);
        builder.HasIndex(a => new { a.IsArchived, a.Name });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class AchievementAwardConfiguration : IEntityTypeConfiguration<AchievementAward>
{
    public void Configure(EntityTypeBuilder<AchievementAward> builder)
    {
        builder.ToTable("AchievementAwards");
        builder.Property(a => a.IssuedByName).HasMaxLength(201);
        builder.Property(a => a.RevokedByName).HasMaxLength(201);
        builder.Property(a => a.Version).IsConcurrencyToken();
        builder.HasIndex(a => new { a.AchievementId, a.UserId }).IsUnique().HasFilter("\"RevokedAtUtc\" IS NULL");
        builder.HasIndex(a => new { a.UserId, a.GrantedAtUtc });
        builder.HasOne(a => a.Achievement).WithMany().HasForeignKey(a => a.AchievementId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.IssuedByUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.RevokedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
