namespace FunAndChecks.Domain.Entities;

/// <summary>
/// Загруженный файл. Байты лежат во внешнем хранилище по <see cref="StorageKey"/>, в БД — только метаданные.
/// Пока файл ни к чему не привязан, он «висячий» и удаляется фоновой очисткой.
/// </summary>
public class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UploaderId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Sha256 { get; set; }
    public required string StorageKey { get; set; }
    public bool IsImage { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public long? ForumPostId { get; set; }
    public ForumPost? ForumPost { get; set; }
}
