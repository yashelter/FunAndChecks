using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public sealed class AchievementDraftConfiguration : IEntityTypeConfiguration<AchievementDraft>,
    IEntityTypeConfiguration<AchievementDraftMedia>
{
    public void Configure(EntityTypeBuilder<AchievementDraft> builder)
    {
        builder.HasKey(draft => new { draft.OwnerId, draft.Id });
        builder.HasIndex(draft => draft.ExpiresAtUtc);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(draft => draft.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public void Configure(EntityTypeBuilder<AchievementDraftMedia> builder)
    {
        builder.Property(media => media.Id).ValueGeneratedNever();
        builder.Property(media => media.ContentType).HasMaxLength(100);
        builder.Property(media => media.PosterContentType).HasMaxLength(100);
        builder.HasIndex(media => new { media.OwnerId, media.DraftId, media.Slot }).IsUnique();
        builder.HasOne<AchievementDraft>().WithMany().HasForeignKey(media => new { media.OwnerId, media.DraftId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
