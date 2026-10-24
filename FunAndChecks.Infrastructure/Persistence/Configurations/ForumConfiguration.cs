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
    }
}

public class ForumReactionConfiguration : IEntityTypeConfiguration<ForumReaction>
{
    public void Configure(EntityTypeBuilder<ForumReaction> builder)
    {
        builder.ToTable("ForumReactions");
        builder.HasKey(r => new { r.PostId, r.UserId, r.Kind });
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
