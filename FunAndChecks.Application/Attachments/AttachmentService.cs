using System.Security.Cryptography;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Common.Interfaces;
using FunAndChecks.Application.Forum;
using FunAndChecks.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FunAndChecks.Application.Attachments;

public record AttachmentDto(Guid Id, string FileName, string ContentType, long SizeBytes, bool IsImage, string Url);

public record AttachmentFileDto(Stream Content, string ContentType, string FileName, bool Inline);

public interface IAttachmentService
{
    Task<AttachmentDto> UploadAsync(Guid userId, Stream content, string? fileName, CancellationToken ct = default);
    Task<AttachmentFileDto> OpenAsync(Guid id, long expires, string? signature, CancellationToken ct = default);
    Task DeleteUnusedAsync(Guid userId, Guid id, CancellationToken ct = default);
}

public class AttachmentService(IApplicationDbContext db, IFileStorage storage, IAttachmentUrlSigner signer,
    IForumAccess forumAccess) : IAttachmentService
{
    /// <summary>Сколько непривязанных загрузок может висеть у пользователя — защита диска от мусора.</summary>
    internal const int MaxPendingPerUser = 20;

    public async Task<AttachmentDto> UploadAsync(Guid userId, Stream content, string? fileName, CancellationToken ct = default)
    {
        await forumAccess.GetViewerAsync(userId, ct);
        if (await db.ForumBans.AnyAsync(b => b.UserId == userId && b.UntilUtc > DateTime.UtcNow, ct))
            throw new ForbiddenException("You cannot post on the forum right now.", "forum.banned");
        if (await db.Attachments.CountAsync(a => a.UploaderId == userId && a.ForumPostId == null, ct) >= MaxPendingPerUser)
            throw new ConflictException("Too many files are waiting to be attached. Publish or remove them first.",
                "attachment.too_many_pending");

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > AttachmentContent.MaxFileBytes)
                throw new FluentValidation.ValidationException("The file is larger than 10 MB.");
            buffer.Write(chunk, 0, read);
        }
        if (buffer.Length == 0) throw new FluentValidation.ValidationException("The file is empty.");

        var name = AttachmentContent.CleanFileName(fileName);
        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var detected = AttachmentContent.Detect(bytes[..Math.Min(bytes.Length, 64 * 1024)], name)
            ?? throw new ConflictException("This file type is not allowed. Use images, PDF, archives, office documents or source code.",
                "attachment.type_not_allowed");
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        buffer.Position = 0;
        var key = await storage.SaveAsync(buffer, ct);
        var attachment = new Attachment
        {
            UploaderId = userId, FileName = name, ContentType = detected.ContentType, SizeBytes = buffer.Length,
            Sha256 = sha, StorageKey = key, IsImage = detected.IsImage, CreatedAtUtc = DateTime.UtcNow,
        };
        try
        {
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(key, CancellationToken.None);
            throw;
        }
        return ToDto(attachment, signer);
    }

    public async Task<AttachmentFileDto> OpenAsync(Guid id, long expires, string? signature, CancellationToken ct = default)
    {
        if (!signer.Verify(id, expires, signature))
            throw new NotFoundException("Attachment not found.", "attachment.not_found");
        var attachment = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Attachment not found.", "attachment.not_found");
        var stream = await storage.OpenReadAsync(attachment.StorageKey, ct);
        return new(stream, attachment.ContentType, attachment.FileName, attachment.IsImage);
    }

    public async Task DeleteUnusedAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var attachment = await db.Attachments.SingleOrDefaultAsync(a => a.Id == id && a.UploaderId == userId, ct)
            ?? throw new NotFoundException("Attachment not found.", "attachment.not_found");
        if (attachment.ForumPostId is not null)
            throw new ConflictException("The file is already attached to a message.", "attachment.in_use");
        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(attachment.StorageKey, ct);
    }

    public static AttachmentDto ToDto(Attachment a, IAttachmentUrlSigner signer) =>
        new(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.IsImage, signer.CreateUrl(a.Id));

    /// <summary>Загрузки пользователя, которые ещё ни к чему не привязаны и могут стать вложениями.</summary>
    internal static async Task<List<Attachment>> ClaimAsync(IApplicationDbContext db, Guid userId,
        IReadOnlyCollection<Guid> ids, long? postId, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var found = await db.Attachments.Where(a => ids.Contains(a.Id) && a.UploaderId == userId &&
            (a.ForumPostId == null || (postId != null && a.ForumPostId == postId))).ToListAsync(ct);
        if (found.Count != ids.Count)
            throw new ConflictException("Some files are missing or already used elsewhere. Upload them again.",
                "attachment.invalid");
        return found;
    }
}
