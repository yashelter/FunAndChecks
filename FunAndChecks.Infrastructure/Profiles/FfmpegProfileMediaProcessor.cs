using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Infrastructure.Profiles;

/// <summary>Private, bounded editor sessions. A render lease keeps its source until the database commit succeeds.</summary>
public sealed class FfmpegProfileMediaProcessor : IProfileMediaProcessor
{
    public const long MaxUploadBytes = 20L * 1024 * 1024;
    public const long MaxResultBytes = 2L * 1024 * 1024;
    public const long MaxPreviewBytes = 10L * 1024 * 1024;
    private const long SessionReservationBytes = 44L * 1024 * 1024;
    private const string Formats = "image2,image2pipe,jpeg_pipe,png_pipe,webp_pipe,gif,mov,matroska,webm";
    private readonly ProfileMediaOptions _options;
    private readonly ILogger<FfmpegProfileMediaProcessor> _logger;
    private readonly TimeProvider _clock;
    private readonly string _root;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Session> _sessions = [];
    private readonly SemaphoreSlim _workers = new(2, 2);
    private Task? _dependencyCheck;

    public FfmpegProfileMediaProcessor(IOptions<ProfileMediaOptions> options,
        ILogger<FfmpegProfileMediaProcessor> logger, TimeProvider? clock = null)
    {
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _root = Path.GetFullPath(_options.TemporaryDirectory);
        if (_options.SessionLifetimeMinutes is < 1 or > 120 || _options.CleanupIntervalMinutes is < 1 or > 60 ||
            _options.MaxSessionsPerUser is < 1 or > 2 || _options.MaxSessions is < 1 or > 32 ||
            _options.TemporaryQuotaBytes is < SessionReservationBytes or > 1024L * 1024 * 1024 ||
            _options.ProcessingTimeoutSeconds is < 1 or > 60 ||
            _options.ProcessMemoryLimitBytes is < 64L * 1024 * 1024 or > 1024L * 1024 * 1024)
            throw new InvalidOperationException("ProfileMedia limits must remain within their supported bounds.");
    }

    public async Task<MediaEditorSessionDto> PrepareAsync(Guid actorId, Stream source, string fileName,
        string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureDependenciesAsync(cancellationToken);
        await CleanupExpiredAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Session session;
        lock (_sync)
        {
            EnsureRoot();
            if (_sessions.Values.Count(s => s.OwnerId == actorId) >= _options.MaxSessionsPerUser ||
                _sessions.Count >= _options.MaxSessions ||
                (_sessions.Count + 1L) * SessionReservationBytes + OrphanBytes() > _options.TemporaryQuotaBytes)
                throw new RateLimitException("Close another media editor before uploading a new file.", "media.session_limit");
            session = new Session(Guid.NewGuid(), actorId, UtcNow.AddMinutes(_options.SessionLifetimeMinutes), _root);
            Directory.CreateDirectory(session.Directory);
            _sessions.Add(session.Id, session);
        }
        // The new session has no other users yet; cancellation is handled inside the cleanup-protected body.
        await session.Gate.WaitAsync(CancellationToken.None);
        try
        {
            // The original name never becomes a path or a command argument.
            var uploadPath = Path.Combine(session.Directory, "upload.bin");
            await using (var output = new FileStream(uploadPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    total += read;
                    if (total > MaxUploadBytes)
                        throw Invalid("media_source_too_large", "The source file must be no larger than 20 MiB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (total == 0) throw Invalid("media_invalid_source", "The source file is empty.");
            }
            var kind = await DetectAsync(uploadPath, contentType, cancellationToken);
            session.SourcePath = Path.Combine(session.Directory, "source" + kind.Extension);
            File.Move(uploadPath, session.SourcePath);
            session.Kind = kind.Kind;
            await ProbeAsync(session, cancellationToken);
            await CreatePosterAsync(session.SourcePath, Path.Combine(session.Directory, "poster.webp"),
                "scale=w='min(768,iw)':h='min(768,ih)':force_original_aspect_ratio=decrease", 0, cancellationToken);
            session.PosterPath = Path.Combine(session.Directory, "poster.webp");
            if (session.CanAnimate)
                await CreatePreviewAsync(session, cancellationToken);
            else session.PreviewPath = session.PosterPath;
            session.Ready = true;
            return session.Dto;
        }
        catch
        {
            RemoveSession(session);
            throw;
        }
        finally { session.Gate.Release(); }
    }

    public async Task<MediaStreamResult> GetPreviewAsync(Guid actorId, Guid sessionId, bool poster = false,
        CancellationToken cancellationToken = default)
    {
        var session = Find(actorId, sessionId);
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            EnsureUsable(session);
            var path = poster ? session.PosterPath : session.PreviewPath;
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            return new MediaStreamResult(new MemoryStream(bytes, writable: false),
                poster || !session.CanAnimate ? "image/webp" : "video/mp4");
        }
        finally { session.Gate.Release(); }
    }

    public async Task<MediaStreamResult> GetFrameAsync(Guid actorId, Guid sessionId, double timeSeconds,
        CancellationToken cancellationToken = default)
    {
        var session = Find(actorId, sessionId);
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            EnsureUsable(session);
            if (!double.IsFinite(timeSeconds) || timeSeconds < 0 || timeSeconds > session.DurationSeconds)
                throw Invalid("media_invalid_selection", "The selected frame is outside the source.");
            var path = Path.Combine(session.Directory, "frame.webp");
            await CreatePosterAsync(session.SourcePath, path,
                "scale=w='min(1024,iw)':h='min(1024,ih)':force_original_aspect_ratio=decrease", SafeFrameTime(session, timeSeconds),
                cancellationToken);
            return new MediaStreamResult(new MemoryStream(await File.ReadAllBytesAsync(path, cancellationToken), false), "image/webp");
        }
        finally { session.Gate.Release(); }
    }

    public async Task<IProfileMediaRenderLease> RenderAsync(Guid actorId, Guid sessionId,
        MediaRenderRequest request, int size, CancellationToken cancellationToken = default)
    {
        var session = Find(actorId, sessionId);
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            EnsureUsable(session);
            var selection = ValidateSelection(session, request, size);
            var media = await RenderCoreAsync(session, request, selection, size, cancellationToken);
            // Ownership and expiry stay protected until the consumer has committed its database transaction.
            return new RenderLease(this, session, media);
        }
        catch { session.Gate.Release(); throw; }
    }

    public async Task CancelAsync(Guid actorId, Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = Find(actorId, sessionId);
        await session.Gate.WaitAsync(cancellationToken);
        try { RemoveSession(session); }
        finally { session.Gate.Release(); }
    }

    public async Task EnsureDependenciesAsync(CancellationToken cancellationToken = default)
    {
        Task check;
        lock (_sync) check = _dependencyCheck ??= VerifyDependenciesCoreAsync();
        try { await check.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Profile media processing requires ffmpeg/ffprobe with libx264, libwebp and libwebp_anim.");
            throw new ConflictException("Media processing is unavailable. Configure FFmpeg and ffprobe with WebP and H.264 support.",
                "media.processing_unavailable");
        }
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        Session[] expired;
        lock (_sync) expired = _sessions.Values.Where(s => s.ExpiresAt <= UtcNow).ToArray();
        foreach (var session in expired)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Do not interrupt a download, crop, or database commit already holding a lease.
            if (!await session.Gate.WaitAsync(0, cancellationToken)) continue;
            try { RemoveSession(session); }
            finally { session.Gate.Release(); }
        }
        lock (_sync)
        {
            if (!Directory.Exists(_root)) return;
            foreach (var directory in Directory.EnumerateDirectories(_root))
            {
                var name = Path.GetFileName(directory);
                if (!Guid.TryParseExact(name, "N", out var id) || _sessions.ContainsKey(id) || IsLink(directory)) continue;
                if (Directory.GetLastWriteTimeUtc(directory) <= UtcNow.AddMinutes(-_options.SessionLifetimeMinutes))
                    TryDelete(directory);
            }
        }
    }

    private async Task VerifyDependenciesCoreAsync()
    {
        var encoders = await RunAsync(_options.FfmpegPath, ["-hide_banner", "-encoders"], CancellationToken.None);
        if (!encoders.Contains("libwebp_anim", StringComparison.Ordinal) ||
            !encoders.Contains("libwebp ", StringComparison.Ordinal) || !encoders.Contains("libx264 ", StringComparison.Ordinal))
            throw new InvalidOperationException("Required media encoders are missing.");
        await RunAsync(_options.FfprobePath, ["-version"], CancellationToken.None);
    }

    private async Task ProbeAsync(Session session, CancellationToken cancellationToken)
    {
        string json;
        try
        {
            json = await RunAsync(_options.FfprobePath,
                ["-v", "error", "-max_alloc", "134217728", "-protocol_whitelist", "file,pipe", "-format_whitelist", Formats,
                    "-select_streams", "v:0", "-read_intervals", "%+#1", "-show_frames", "-show_streams", "-show_format", "-of", "json", "-i", session.SourcePath], cancellationToken);
        }
        catch (ValidationException ex) when (ex.Errors.All(error => error.ErrorCode == "media_processing_failed"))
        { throw Invalid("media_invalid_source", "The file cannot be decoded as an image, GIF, or video."); }
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { throw Invalid("media_invalid_source", "The source metadata could not be decoded."); }
        using var decoded = document;
        if (!document.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            throw Invalid("media_invalid_source", "The source contains no supported image or video track.");
        var videos = streams.EnumerateArray().Where(s => s.TryGetProperty("codec_type", out var t) && t.GetString() == "video").ToArray();
        if (videos.Length != 1) throw Invalid("media_invalid_source", "Choose a source containing one image or video track.");
        var stream = videos[0];
        var codec = stream.TryGetProperty("codec_name", out var codecName) ? codecName.GetString() : null;
        var allowed = session.Kind switch
        {
            "Image" => codec is "mjpeg" or "png" or "webp",
            "Gif" => codec == "gif",
            _ => codec is "h264" or "hevc" or "vp8" or "vp9" or "av1" or "mpeg4" or "prores" or "mjpeg" or "qtrle"
        };
        if (!allowed) throw Invalid("media_invalid_source", "The media codec is unsupported.");
        if (!stream.TryGetProperty("width", out var sourceWidth) || !stream.TryGetProperty("height", out var sourceHeight))
            throw Invalid("media_invalid_source", "The source dimensions could not be decoded.");
        int width = sourceWidth.GetInt32(), height = sourceHeight.GetInt32();
        if (width is < 1 or > 4096 || height is < 1 or > 4096 || (long)width * height > 16_000_000)
            throw Invalid("media_source_dimensions", "Source dimensions must be at most 4096 pixels and 16 megapixels.");
        var rotation = 0d;
        if (stream.TryGetProperty("side_data_list", out var sideData))
            foreach (var item in sideData.EnumerateArray())
                if (item.TryGetProperty("rotation", out var angle)) rotation = angle.GetDouble();
        if (stream.TryGetProperty("tags", out var tags) && tags.TryGetProperty("rotate", out var rotate))
            double.TryParse(rotate.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out rotation);
        // JPEG EXIF orientation is attached to the decoded frame, rather than the stream.
        if (document.RootElement.TryGetProperty("frames", out var frames))
            foreach (var frame in frames.EnumerateArray())
                if (frame.TryGetProperty("side_data_list", out var frameData))
                    foreach (var item in frameData.EnumerateArray())
                        if (item.TryGetProperty("rotation", out var angle)) rotation = angle.GetDouble();
        if (Math.Abs(rotation % 180) > 45) (width, height) = (height, width);
        session.Width = width;
        session.Height = height;
        session.DurationSeconds = ParseDuration(stream);
        if (stream.TryGetProperty("avg_frame_rate", out var rate))
        {
            var parts = rate.GetString()?.Split('/');
            if (parts is { Length: 2 } && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) && numerator > 0 && denominator > 0)
                session.FramePeriod = denominator / numerator;
        }
        if (document.RootElement.TryGetProperty("format", out var format))
            session.DurationSeconds = Math.Max(session.DurationSeconds, ParseDuration(format));
        if (session.Kind == "Image") session.DurationSeconds = 0;
        else if (!double.IsFinite(session.DurationSeconds) || session.DurationSeconds is <= 0 or > 300)
            throw Invalid("media_source_duration", "Animated sources must be no longer than five minutes.");
    }

    private async Task CreatePreviewAsync(Session session, CancellationToken cancellationToken)
    {
        var scale = Math.Min(1d, 512d / Math.Max(session.Width, session.Height));
        var width = Math.Max(2, ((int)(session.Width * scale) / 2) * 2);
        var height = Math.Max(2, ((int)(session.Height * scale) / 2) * 2);
        var bitrate = Math.Min(900_000, (int)(MaxPreviewBytes * 6d / session.DurationSeconds));
        var filter = $"color=c=0x182033:s={width}x{height}:r=24,format=rgb24," +
                     "geq=r='if(mod(floor(X/16)+floor(Y/16),2),38,24)':g='if(mod(floor(X/16)+floor(Y/16),2),51,32)':b='if(mod(floor(X/16)+floor(Y/16),2),72,51)'[bg];" +
                     $"[0:v:0]fps=24,scale={width}:{height},format=rgba[fg];[bg][fg]overlay=shortest=1,format=yuv420p[v]";
        session.PreviewPath = Path.Combine(session.Directory, "preview.mp4");
        var args = InputArgs(session.SourcePath);
        args.AddRange(["-filter_complex", filter, "-map", "[v]", "-an", "-t", F(session.DurationSeconds),
            "-c:v", "libx264", "-threads", "2", "-preset", "veryfast", "-b:v", bitrate.ToString(CultureInfo.InvariantCulture),
            "-maxrate", bitrate.ToString(CultureInfo.InvariantCulture), "-bufsize", (bitrate * 2).ToString(CultureInfo.InvariantCulture),
            "-movflags", "+faststart", "-map_metadata", "-1", session.PreviewPath]);
        await RunAsync(_options.FfmpegPath, args, cancellationToken, session.PreviewPath, MaxPreviewBytes + 1024 * 1024);
        if (new FileInfo(session.PreviewPath).Length > MaxPreviewBytes)
            throw Invalid("media_preview_too_large", "The source is too complex for the editor preview. Choose a shorter source.");
    }

    private async Task<ProcessedProfileMedia> RenderCoreAsync(Session session, MediaRenderRequest request,
        Crop crop, int size, CancellationToken cancellationToken)
    {
        var animated = request.Mode == "Animated";
        var mp4 = animated && session.Kind == "Video";
        var output = Path.Combine(session.Directory, mp4 ? "result.mp4" : "result.webp");
        var duration = animated ? request.EndSeconds - request.StartSeconds : 0;
        var attempts = animated ? new[] { (Quality: 72, Fps: 24), (Quality: 52, Fps: 20), (Quality: 32, Fps: 12) }
            : new[] { (Quality: 85, Fps: 1), (Quality: 65, Fps: 1), (Quality: 45, Fps: 1) };
        var finished = false;
        foreach (var attempt in attempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(output)) File.Delete(output);
            var args = InputArgs(session.SourcePath, animated ? request.StartSeconds : SafeFrameTime(session, request.FrameSeconds));
            var filter = $"crop={crop.Side}:{crop.Side}:{crop.X}:{crop.Y}:exact=1,scale={size}:{size}:flags=lanczos,setsar=1";
            if (animated) filter += $",fps={attempt.Fps}";
            filter += mp4 ? ",format=yuv420p" : ",format=bgra";
            args.AddRange(["-map", "0:v:0", "-vf", filter, "-an", "-map_metadata", "-1"]);
            if (animated) args.AddRange(["-t", F(duration)]);
            else args.AddRange(["-frames:v", "1"]);
            if (mp4)
            {
                var crf = attempt.Quality == 72 ? 24 : attempt.Quality == 52 ? 30 : 36;
                args.AddRange(["-c:v", "libx264", "-threads", "2", "-preset", "veryfast", "-crf", crf.ToString(),
                    "-maxrate", "1400k", "-bufsize", "2800k", "-movflags", "+faststart"]);
            }
            else args.AddRange(["-c:v", animated ? "libwebp_anim" : "libwebp", "-lossless", "0",
                "-quality", attempt.Quality.ToString(), "-compression_level", "6", "-loop", "0"]);
            args.Add(output);
            try { await RunAsync(_options.FfmpegPath, args, cancellationToken, output, 8L * 1024 * 1024); }
            catch (OutputLimitException) { continue; }
            if (new FileInfo(output).Length is > 0 and <= MaxResultBytes) { finished = true; break; }
        }
        if (!finished) throw Invalid("media_result_too_large", "The selected fragment cannot fit in 2 MiB. Choose a simpler or shorter fragment.");
        var data = await File.ReadAllBytesAsync(output, cancellationToken);
        byte[] poster;
        if (animated)
        {
            var posterPath = Path.Combine(session.Directory, "result-poster.webp");
            // Decode the original instead of animated WebP, whose decoder support varies between FFmpeg builds.
            await CreatePosterAsync(session.SourcePath, posterPath,
                $"crop={crop.Side}:{crop.Side}:{crop.X}:{crop.Y}:exact=1,scale={size}:{size}:flags=lanczos", request.StartSeconds,
                cancellationToken);
            poster = await File.ReadAllBytesAsync(posterPath, cancellationToken);
        }
        else poster = data;
        return new ProcessedProfileMedia(data, mp4 ? "video/mp4" : "image/webp", poster, "image/webp",
            size, size, duration, animated);
    }

    private async Task CreatePosterAsync(string source, string output, string filter, double time,
        CancellationToken cancellationToken)
    {
        if (File.Exists(output)) File.Delete(output);
        var args = InputArgs(source, time);
        args.AddRange(["-map", "0:v:0", "-vf", filter + ",format=bgra", "-an", "-frames:v", "1", "-c:v", "libwebp",
            "-quality", "80", "-compression_level", "6", "-map_metadata", "-1", output]);
        await RunAsync(_options.FfmpegPath, args, cancellationToken, output, MaxResultBytes);
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
            throw Invalid("media_invalid_selection", "The selected frame could not be decoded.");
    }

    private static List<string> InputArgs(string source, double time = 0)
    {
        var args = new List<string> { "-hide_banner", "-v", "error", "-nostdin", "-y", "-max_alloc", "134217728",
            "-threads", "2", "-filter_threads", "1", "-filter_complex_threads", "1", "-protocol_whitelist", "file,pipe",
            "-format_whitelist", Formats, "-i", source };
        // Output seeking is accurate for both GIF and video, including long GOP sources.
        if (time > 0) args.AddRange(["-ss", F(time)]);
        return args;
    }

    private async Task<string> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken,
        string? outputPath = null, long? outputLimit = null)
    {
        await _workers.WaitAsync(cancellationToken);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(_options.ProcessingTimeoutSeconds));
            using var process = new Process { StartInfo = new ProcessStartInfo(executable)
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
            foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stdout = ReadBoundedAsync(process.StandardOutput, 2 * 1024 * 1024);
            var stderr = ReadBoundedAsync(process.StandardError, 16 * 1024);
            var exceeded = false;
            var memoryExceeded = false;
            try
            {
                while (!process.HasExited)
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    try
                    {
                        process.Refresh();
                        if (!process.HasExited && process.WorkingSet64 > _options.ProcessMemoryLimitBytes)
                        {
                            memoryExceeded = true;
                            StopProcess(process);
                            break;
                        }
                    }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    if (outputPath is not null && outputLimit is not null && File.Exists(outputPath) &&
                        new FileInfo(outputPath).Length > outputLimit)
                    {
                        exceeded = true;
                        StopProcess(process);
                        break;
                    }
                    await Task.Delay(75, deadline.Token);
                }
                await process.WaitForExitAsync(deadline.Token);
                var captured = await stdout;
                await stderr;
                if (memoryExceeded)
                    throw Invalid("media_processing_memory_limit", "The source requires too much memory. Choose a smaller or simpler source.");
                if (exceeded) throw new OutputLimitException();
                if (process.ExitCode != 0)
                    throw Invalid("media_processing_failed", "The selected media could not be processed.");
                return captured;
            }
            catch (OperationCanceledException)
            {
                StopProcess(process);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stdout, stderr);
                if (cancellationToken.IsCancellationRequested) throw;
                throw Invalid("media_processing_timeout", "Media processing exceeded its time limit. Choose a smaller source.");
            }
        }
        finally { _workers.Release(); }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer)) != 0)
            if (builder.Length < maximum) builder.Append(buffer, 0, Math.Min(read, maximum - builder.Length));
        return builder.ToString();
    }

    private static void StopProcess(Process process)
    {
        // The encoder may finish between the cancellation/size check and Kill.
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }

    private static async Task<(string Extension, string Kind)> DetectAsync(string path, string mime, CancellationToken cancellationToken)
    {
        var header = new byte[32];
        await using var source = File.OpenRead(path);
        var read = await source.ReadAsync(header, cancellationToken);
        var bytes = header.AsSpan(0, read);
        string extension, kind;
        if (bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) (extension, kind) = (".jpg", "Image");
        else if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) (extension, kind) = (".png", "Image");
        else if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) (extension, kind) = (".gif", "Gif");
        else if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
            (extension, kind) = (".webp", "Image");
        else if (bytes.Length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8)) (extension, kind) = (".mp4", "Video");
        else if (bytes.Length >= 8 && (bytes[4..8].SequenceEqual("moov"u8) || bytes[4..8].SequenceEqual("mdat"u8) ||
            bytes[4..8].SequenceEqual("wide"u8) || bytes[4..8].SequenceEqual("free"u8)))
            (extension, kind) = (".mov", "Video");
        else if (bytes.StartsWith(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 })) (extension, kind) = (".webm", "Video");
        else throw Invalid("media_invalid_source", "Choose a JPEG, PNG, WebP, GIF, MP4, MOV, or WebM file.");
        var claimed = mime.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (claimed is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif" or "video/mp4" or
            "video/quicktime" or "video/webm" or "application/octet-stream"))
            throw Invalid("media_invalid_source", "The source media type is unsupported.");
        var matches = extension switch
        {
            ".jpg" => claimed == "image/jpeg",
            ".png" => claimed == "image/png",
            ".webp" => claimed == "image/webp",
            ".gif" => claimed == "image/gif",
            ".webm" => claimed == "video/webm",
            _ => claimed is "video/mp4" or "video/quicktime"
        };
        if (!matches && claimed != "application/octet-stream")
            throw Invalid("media_invalid_source", "The declared media type does not match the file.");
        return (extension, kind);
    }

    private static Crop ValidateSelection(Session session, MediaRenderRequest request, int size)
    {
        if (size is not (256 or 512) || request.Mode is not ("Still" or "Animated") ||
            !new[] { request.X, request.Y, request.Width, request.Height, request.StartSeconds, request.EndSeconds, request.FrameSeconds }
                .All(double.IsFinite) || request.X < 0 || request.Y < 0 || request.Width <= 0 || request.Height <= 0 ||
            request.X + request.Width > 1.000001 || request.Y + request.Height > 1.000001)
            throw Invalid("media_invalid_selection", "Choose a square crop within the source.");
        var width = request.Width * session.Width;
        var height = request.Height * session.Height;
        var side = (int)Math.Round(Math.Min(width, height));
        var x = (int)Math.Round(request.X * session.Width);
        var y = (int)Math.Round(request.Y * session.Height);
        if (side < 1 || Math.Abs(width - height) > 2 || x + side > session.Width || y + side > session.Height)
            throw Invalid("media_invalid_selection", "Choose a square crop within the source.");
        if (request.Mode == "Animated" && (!session.CanAnimate || request.StartSeconds < 0 ||
                request.EndSeconds > session.DurationSeconds + 0.000001 || request.EndSeconds <= request.StartSeconds ||
                request.EndSeconds - request.StartSeconds > 10.000001))
            throw Invalid("media_invalid_selection", "Choose an animation fragment no longer than ten seconds.");
        if (request.FrameSeconds < 0 || request.FrameSeconds > session.DurationSeconds)
            throw Invalid("media_invalid_selection", "The selected frame is outside the source.");
        return new Crop(x, y, side);
    }

    private Session Find(Guid actor, Guid id)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(id, out var session) || session.OwnerId != actor)
                throw new NotFoundException("The media editor session was not found.", "media.session_not_found");
            return session;
        }
    }

    private void EnsureUsable(Session session)
    {
        lock (_sync)
            if (!_sessions.ContainsKey(session.Id) || !session.Ready || session.ExpiresAt <= UtcNow)
                throw new NotFoundException("The media editor session has expired. Upload the source again.", "media.session_not_found");
    }

    private void RemoveSession(Session session)
    {
        lock (_sync) _sessions.Remove(session.Id);
        TryDelete(session.Directory);
    }

    private void EnsureRoot()
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(_root);
        else Directory.CreateDirectory(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (IsLink(_root)) throw new InvalidOperationException("The media temporary directory must not be a symbolic link.");
    }

    private long OrphanBytes() => Directory.EnumerateDirectories(_root)
        .Where(directory => !IsLink(directory) && !_sessions.Values.Any(s => s.Directory == directory))
        .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        .Where(file => !IsLink(file)).Sum(file => new FileInfo(file).Length);

    private void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !IsLink(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException ex) { _logger.LogWarning(ex, "Could not clean a temporary profile media session."); }
        catch (UnauthorizedAccessException ex) { _logger.LogWarning(ex, "Could not clean a temporary profile media session."); }
    }

    private DateTime UtcNow => _clock.GetUtcNow().UtcDateTime;
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static double ParseDuration(JsonElement value) => value.TryGetProperty("duration", out var duration) &&
        double.TryParse(duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    private static double SafeFrameTime(Session session, double time) => session.CanAnimate
        ? Math.Min(time, Math.Max(0, session.DurationSeconds - session.FramePeriod)) : 0;
    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    private static ValidationException Invalid(string code, string message) => new([new ValidationFailure("Media", message) { ErrorCode = code }]);
    private sealed class OutputLimitException : Exception;
    private sealed record Crop(int X, int Y, int Side);

    private sealed class Session(Guid id, Guid ownerId, DateTime expiresAt, string root)
    {
        public Guid Id { get; } = id;
        public Guid OwnerId { get; } = ownerId;
        public DateTime ExpiresAt { get; } = expiresAt;
        public string Directory { get; } = Path.Combine(root, id.ToString("N"));
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public bool Ready { get; set; }
        public string SourcePath { get; set; } = "";
        public string PreviewPath { get; set; } = "";
        public string PosterPath { get; set; } = "";
        public string Kind { get; set; } = "Image";
        public int Width { get; set; }
        public int Height { get; set; }
        public double DurationSeconds { get; set; }
        public double FramePeriod { get; set; } = 1d / 24;
        public bool CanAnimate => Kind is "Gif" or "Video";
        public MediaEditorSessionDto Dto => new(Id, Width, Height, DurationSeconds, Kind, CanAnimate, ExpiresAt);
    }

    private sealed class RenderLease(FfmpegProfileMediaProcessor owner, Session session, ProcessedProfileMedia media) : IProfileMediaRenderLease
    {
        private int _released;
        public ProcessedProfileMedia Media { get; } = media;
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            // Persistence already succeeded: caller cancellation must not leave a committed session reusable.
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                try { owner.RemoveSession(session); }
                finally { session.Gate.Release(); }
            }
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) session.Gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
