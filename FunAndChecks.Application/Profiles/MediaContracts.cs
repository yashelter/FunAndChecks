namespace FunAndChecks.Application.Profiles;

/// <summary>Crop coordinates refer to the source after EXIF/video orientation is applied.</summary>
public record MediaRenderRequest(double X, double Y, double Width, double Height,
    double StartSeconds, double EndSeconds, double FrameSeconds, string Mode);

public record MediaEditorSessionDto(Guid Id, int Width, int Height, double DurationSeconds,
    string Kind, bool CanAnimate, DateTime ExpiresAtUtc);

public record ProcessedProfileMedia(byte[] Data, string ContentType, byte[] PosterData,
    string PosterContentType, int Width, int Height, double DurationSeconds, bool IsAnimated);

public record MediaStreamResult(Stream Data, string ContentType);

/// <summary>Holds the editor session until the owning database transaction commits.</summary>
public interface IProfileMediaRenderLease : IAsyncDisposable
{
    ProcessedProfileMedia Media { get; }
    Task CommitAsync(CancellationToken cancellationToken = default);
}

public interface IProfileMediaProcessor
{
    Task<MediaEditorSessionDto> PrepareAsync(Guid userId, Stream source, string fileName,
        string contentType, CancellationToken cancellationToken = default);
    Task<MediaStreamResult> GetPreviewAsync(Guid userId, Guid sessionId, bool poster = false,
        CancellationToken cancellationToken = default);
    Task<MediaStreamResult> GetFrameAsync(Guid userId, Guid sessionId, double timeSeconds,
        CancellationToken cancellationToken = default);
    Task<IProfileMediaRenderLease> RenderAsync(Guid userId, Guid sessionId, MediaRenderRequest request,
        int outputSize, CancellationToken cancellationToken = default);
    /// <summary>Renders a crop of another shape, e.g. a wide profile banner. Square sizes go to the method above.</summary>
    Task<IProfileMediaRenderLease> RenderAsync(Guid userId, Guid sessionId, MediaRenderRequest request,
        int outputWidth, int outputHeight, CancellationToken cancellationToken = default) =>
        outputWidth == outputHeight
            ? RenderAsync(userId, sessionId, request, outputWidth, cancellationToken)
            : throw new NotSupportedException("This media processor renders square pictures only.");
    Task CancelAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
}
