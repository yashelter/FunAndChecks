using FunAndChecks.Domain.Entities;
using FunAndChecks.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunAndChecks.Infrastructure.Persistence.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.FileName).HasMaxLength(200);
        builder.Property(a => a.ContentType).HasMaxLength(100);
        builder.Property(a => a.Sha256).HasMaxLength(64);
        builder.Property(a => a.StorageKey).HasMaxLength(64);
        builder.HasIndex(a => a.StorageKey).IsUnique();
        // Висячие файлы ищет фоновая очистка.
        builder.HasIndex(a => new { a.ForumPostId, a.CreatedAtUtc });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(a => a.UploaderId).OnDelete(DeleteBehavior.SetNull);
        // При удалении сообщения файл становится висячим и будет стёрт с диска очисткой.
        builder.HasOne(a => a.ForumPost).WithMany(p => p.Attachments).HasForeignKey(a => a.ForumPostId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.Property(n => n.Subject).HasMaxLength(300);
        builder.Property(n => n.ActorName).HasMaxLength(201);
        builder.Property(n => n.Link).HasMaxLength(300);
        builder.HasIndex(n => new { n.UserId, n.ReadAtUtc });
        builder.HasIndex(n => new { n.UserId, n.CreatedAtUtc });
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ForumTopicConfiguration : IEntityTypeConfiguration<ForumTopic>
{
    public void Configure(EntityTypeBuilder<ForumTopic> builder)
    {
        builder.ToTable("ForumTopics");
        builder.Property(t => t.Title).HasMaxLength(200);
        builder.Property(t => t.AuthorName).HasMaxLength(201);
        builder.HasIndex(t => new { t.SubjectId, t.IsDeleted, t.IsPinned, t.LastActivityAtUtc });
        builder.Property(t => t.DirectKey).HasMaxLength(90);
        // NULL не мешает уникальности: темы разделов ключа не имеют.
        builder.HasIndex(t => t.DirectKey).IsUnique();
        // Предмет удалён — его обсуждения уходят вместе с ним, а не в «Общее».
        builder.HasOne(t => t.Subject).WithMany().HasForeignKey(t => t.SubjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.AuthorId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.DeletedById).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ForumPostConfiguration : IEntityTypeConfiguration<ForumPost>
{
    public void Configure(EntityTypeBuilder<ForumPost> builder)
    {
        builder.ToTable("ForumPosts");
        builder.Property(p => p.AuthorName).HasMaxLength(201);
        builder.Property(p => p.Body).HasMaxLength(10000);
        builder.HasIndex(p => new { p.TopicId, p.Id });
        builder.HasIndex(p => new { p.AuthorId, p.IsDeleted });
        builder.HasOne(p => p.Topic).WithMany(t => t.Posts).HasForeignKey(p => p.TopicId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ForumPost>().WithMany().HasForeignKey(p => p.ReplyToPostId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.AuthorId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.PinnedById).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.DeletedById).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ForumSticker>().WithMany().HasForeignKey(p => p.StickerId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ForumPost>().WithMany().HasForeignKey(p => p.ForwardedFromPostId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ForumTopicMemberConfiguration : IEntityTypeConfiguration<ForumTopicMember>
{
    public void Configure(EntityTypeBuilder<ForumTopicMember> builder)
    {
        builder.ToTable("ForumTopicMembers");
        builder.HasKey(m => new { m.TopicId, m.UserId });
        builder.HasIndex(m => m.UserId);
        builder.HasOne(m => m.Topic).WithMany(t => t.Members).HasForeignKey(m => m.TopicId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ForumReactionTypeConfiguration : IEntityTypeConfiguration<ForumReactionType>
{
    public void Configure(EntityTypeBuilder<ForumReactionType> builder)
    {
        builder.ToTable("ForumReactionTypes");
        builder.Property(r => r.Name).HasMaxLength(40);
        builder.Property(r => r.Emoji).HasMaxLength(16);
        builder.Property(r => r.ImageContentType).HasMaxLength(40);
        // Прежний фиксированный набор: id совпадают с бывшим перечислением + 1, миграция переносит старые реакции.
        builder.HasData(
            new ForumReactionType { Id = 1, Name = "Нравится", Emoji = "👍", SortOrder = 1 },
            new ForumReactionType { Id = 2, Name = "Сердце", Emoji = "❤️", SortOrder = 2 },
            new ForumReactionType { Id = 3, Name = "Смешно", Emoji = "😂", SortOrder = 3 },
            new ForumReactionType { Id = 4, Name = "Праздник", Emoji = "🎉", SortOrder = 4 },
            new ForumReactionType { Id = 5, Name = "Думаю", Emoji = "🤔", SortOrder = 5 },
            new ForumReactionType { Id = 6, Name = "Смотрю", Emoji = "👀", SortOrder = 6 });
    }
}

public class ForumStickerPackConfiguration : IEntityTypeConfiguration<ForumStickerPack>
{
    public void Configure(EntityTypeBuilder<ForumStickerPack> builder)
    {
        builder.ToTable("ForumStickerPacks");
        builder.Property(p => p.Name).HasMaxLength(60);
    }
}

public class ForumStickerConfiguration : IEntityTypeConfiguration<ForumSticker>
{
    public void Configure(EntityTypeBuilder<ForumSticker> builder)
    {
        builder.ToTable("ForumStickers");
        builder.Property(s => s.Name).HasMaxLength(60);
        builder.Property(s => s.ContentType).HasMaxLength(40);
        builder.HasIndex(s => new { s.PackId, s.SortOrder });
        builder.HasOne(s => s.Pack).WithMany(p => p.Stickers).HasForeignKey(s => s.PackId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ForumReactionConfiguration : IEntityTypeConfiguration<ForumReaction>
{
    public void Configure(EntityTypeBuilder<ForumReaction> builder)
    {
        builder.ToTable("ForumReactions");
        builder.HasKey(r => new { r.PostId, r.UserId, r.ReactionTypeId });
        builder.HasOne(r => r.ReactionType).WithMany().HasForeignKey(r => r.ReactionTypeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(r => r.Post).WithMany(p => p.Reactions).HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ForumTopicReadConfiguration : IEntityTypeConfiguration<ForumTopicRead>
{
    public void Configure(EntityTypeBuilder<ForumTopicRead> builder)
    {
        builder.ToTable("ForumTopicReads");
        builder.HasKey(r => new { r.TopicId, r.UserId });
        builder.HasOne<ForumTopic>().WithMany().HasForeignKey(r => r.TopicId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ForumSubscriptionConfiguration : IEntityTypeConfiguration<ForumSubscription>
{
    public void Configure(EntityTypeBuilder<ForumSubscription> builder)
    {
        builder.ToTable("ForumSubscriptions");
        builder.HasKey(s => new { s.TopicId, s.UserId });
        builder.HasOne<ForumTopic>().WithMany().HasForeignKey(s => s.TopicId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ForumReportConfiguration : IEntityTypeConfiguration<ForumReport>
{
    public void Configure(EntityTypeBuilder<ForumReport> builder)
    {
        builder.ToTable("ForumReports");
        builder.Property(r => r.ReporterName).HasMaxLength(201);
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.HasIndex(r => new { r.ResolvedAtUtc, r.CreatedAtUtc });
        builder.HasOne(r => r.Post).WithMany().HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ReporterId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ResolvedById).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ForumBanConfiguration : IEntityTypeConfiguration<ForumBan>
{
    public void Configure(EntityTypeBuilder<ForumBan> builder)
    {
        builder.ToTable("ForumBans");
        builder.HasKey(b => b.UserId);
        builder.Property(b => b.UserId).ValueGeneratedNever();
        builder.Property(b => b.Reason).HasMaxLength(500);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.CreatedById).OnDelete(DeleteBehavior.SetNull);
    }
}
