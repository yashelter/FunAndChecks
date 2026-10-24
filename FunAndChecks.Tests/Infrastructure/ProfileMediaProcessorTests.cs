using System.Diagnostics;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;
using FunAndChecks.Application.Profiles;
using FunAndChecks.Infrastructure.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FunAndChecks.Tests.ProfileMedia;

public sealed class ProfileMediaProcessorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fc-media-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Guid _actor = Guid.NewGuid();
    private readonly ManualTimeProvider _clock = new();
    private readonly FfmpegProfileMediaProcessor _processor;

    public ProfileMediaProcessorTests()
    {
        Directory.CreateDirectory(_directory);
        _processor = new FfmpegProfileMediaProcessor(Options.Create(new ProfileMediaOptions
            { TemporaryDirectory = Path.Combine(_directory, "sessions") }),
            NullLogger<FfmpegProfileMediaProcessor>.Instance, _clock);
    }

    [FfmpegFact]
    public async Task Still_PreservesTransparency_CropsAndCanRetryUntilCommit()
    {
        var source = await Fixture("image.png", ["-f", "lavfi", "-i", "color=red@0.0:s=80x40,format=rgba", "-frames:v", "1"]);
        var session = await Prepare(source, "image/png");
        session.Width.Should().Be(80);
        session.Height.Should().Be(40);
        session.CanAnimate.Should().BeFalse();
        var request = new MediaRenderRequest(.25, 0, .5, 1, 0, 0, 0, "Still");
        await using (var failedSave = await _processor.RenderAsync(_actor, session.Id, request, 256))
        {
            failedSave.Media.ContentType.Should().Be("image/webp");
            failedSave.Media.Width.Should().Be(256);
            failedSave.Media.Height.Should().Be(256);
            failedSave.Media.Data.Length.Should().BeInRange(1, (int)FfmpegProfileMediaProcessor.MaxResultBytes);
            var output = Path.Combine(_directory, "result.webp");
            await File.WriteAllBytesAsync(output, failedSave.Media.Data);
            using var pixels = await Command("ffmpeg", ["-v", "error", "-i", output, "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"]);
            pixels.ToArray()[3].Should().Be(0);
        }
        // A failed database save disposes the lease, retaining its source for a retry.
        await using var retry = await _processor.RenderAsync(_actor, session.Id, request, 256);
        await retry.CommitAsync();
        Func<Task> read = async () => await _processor.GetPreviewAsync(_actor, session.Id);
        await read.Should().ThrowAsync<NotFoundException>();
        Directory.EnumerateDirectories(Path.Combine(_directory, "sessions")).Should().BeEmpty();
    }

    [FfmpegFact]
    public async Task JpegExifOrientation_IsAppliedBeforeCropDimensions()
    {
        var jpeg = await Fixture("image.jpg", ["-f", "lavfi", "-i", "color=red:s=80x40", "-frames:v", "1"]);
        // Little-endian EXIF IFD containing Orientation=6 (rotate 90 degrees clockwise).
        byte[] payload = [69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0, 1, 0,
            18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        var oriented = new byte[jpeg.Length + payload.Length + 4];
        jpeg.AsSpan(0, 2).CopyTo(oriented);
        oriented[2] = 0xff; oriented[3] = 0xe1;
        oriented[4] = 0; oriented[5] = (byte)(payload.Length + 2);
        payload.CopyTo(oriented, 6);
        jpeg.AsSpan(2).CopyTo(oriented.AsSpan(payload.Length + 6));
        var session = await Prepare(oriented, "image/jpeg");
        session.Width.Should().Be(40);
        session.Height.Should().Be(80);
        await using var render = await _processor.RenderAsync(_actor, session.Id,
            new MediaRenderRequest(0, .25, 1, .5, 0, 0, 0, "Still"), 256);
        render.Media.Data.Should().NotBeEmpty();
        await render.CommitAsync();
    }

    [FfmpegFact]
    public async Task Sessions_ArePrivate_BoundedAndRemovedOnCancel()
    {
        var source = await Fixture("image.png", ["-f", "lavfi", "-i", "color=red:s=32x32", "-frames:v", "1"]);
        var first = await Prepare(source, "image/png");
        await Prepare(source, "image/png");
        Func<Task> third = async () => await Prepare(source, "image/png");
        await third.Should().ThrowAsync<RateLimitException>();
        Func<Task> stranger = async () => await _processor.GetPreviewAsync(Guid.NewGuid(), first.Id);
        await stranger.Should().ThrowAsync<NotFoundException>();
        await _processor.CancelAsync(_actor, first.Id);
        Func<Task> removed = async () => await _processor.GetPreviewAsync(_actor, first.Id);
        await removed.Should().ThrowAsync<NotFoundException>();
        await Prepare(source, "image/png");
    }

    [FfmpegFact]
    public async Task Cancellation_WaitsForDatabaseLease_AndDoesNotDeleteTheSourceInUse()
    {
        var source = await Fixture("image.png", ["-f", "lavfi", "-i", "color=red:s=32x32", "-frames:v", "1"]);
        var session = await Prepare(source, "image/png");
        var lease = await _processor.RenderAsync(_actor, session.Id, SquareStill(), 256);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var cancel = _processor.CancelAsync(_actor, session.Id, deadline.Token);
        cancel.IsCompleted.Should().BeFalse();
        await lease.DisposeAsync();
        await cancel;
        Func<Task> read = async () => await _processor.GetPreviewAsync(_actor, session.Id);
        await read.Should().ThrowAsync<NotFoundException>();
    }

    [FfmpegFact]
    public async Task ExpiredSessions_AreCleaned_ButAnActiveRenderLeaseIsPreserved()
    {
        var source = await Fixture("image.png", ["-f", "lavfi", "-i", "color=red:s=32x32", "-frames:v", "1"]);
        var session = await Prepare(source, "image/png");
        var lease = await _processor.RenderAsync(_actor, session.Id, SquareStill(), 256);
        _clock.Advance(TimeSpan.FromMinutes(21));
        await _processor.CleanupExpiredAsync();
        Directory.EnumerateDirectories(Path.Combine(_directory, "sessions")).Should().ContainSingle();
        await lease.DisposeAsync();
        await _processor.CleanupExpiredAsync();
        Directory.EnumerateDirectories(Path.Combine(_directory, "sessions")).Should().BeEmpty();
    }

    [FfmpegFact]
    public async Task Gif_ProvidesSeekableVideoPreview_AndExportsAnimationOrStill()
    {
        var source = await Fixture("animation.gif", ["-f", "lavfi", "-i", "testsrc=s=64x64:r=10", "-t", "1"]);
        var session = await Prepare(source, "image/gif");
        session.Kind.Should().Be("Gif");
        session.DurationSeconds.Should().BeApproximately(1, .1);
        var preview = await _processor.GetPreviewAsync(_actor, session.Id);
        preview.ContentType.Should().Be("video/mp4");
        await using (preview.Data) preview.Data.Length.Should().BeLessThanOrEqualTo(FfmpegProfileMediaProcessor.MaxPreviewBytes);
        await using (var animation = await _processor.RenderAsync(_actor, session.Id,
                         new MediaRenderRequest(0, 0, 1, 1, .2, .8, .4, "Animated"), 512))
        {
            animation.Media.ContentType.Should().Be("image/webp");
            animation.Media.IsAnimated.Should().BeTrue();
            Encoding.ASCII.GetString(animation.Media.Data).Should().Contain("ANIM");
            animation.Media.DurationSeconds.Should().BeApproximately(.6, .001);
            animation.Media.PosterData.Should().NotBeEmpty();
        }
        var frame = await _processor.GetFrameAsync(_actor, session.Id, session.DurationSeconds);
        await using (frame.Data) frame.Data.Length.Should().BeGreaterThan(0);
        await using var still = await _processor.RenderAsync(_actor, session.Id,
            new MediaRenderRequest(0, 0, 1, 1, 0, 0, .5, "Still"), 256);
        still.Media.IsAnimated.Should().BeFalse();
        await still.CommitAsync();
    }

    [FfmpegFact]
    public async Task TransparentGif_ExportsAnimatedWebPWithAlpha_AndTransparentPoster()
    {
        const string block = "between(X,mod(N*4,40),mod(N*4,40)+16)*between(Y,16,32)";
        var source = await Fixture("transparent.gif", ["-t", "1", "-f", "lavfi", "-i",
            $"color=c=black@0.0:s=64x64:r=10,format=rgba,geq=r='if({block},255,0)':g=0:b=0:a='if({block},255,0)'",
            "-filter_complex_threads", "1", "-filter_complex",
            "[0:v]split[a][b];[a]palettegen=reserve_transparent=1:stats_mode=full[p];[b][p]paletteuse=alpha_threshold=128:dither=none[v]",
            "-map", "[v]", "-t", "1"]);
        var session = await Prepare(source, "image/gif");
        await using var render = await _processor.RenderAsync(_actor, session.Id,
            new MediaRenderRequest(0, 0, 1, 1, .1, .9, .1, "Animated"), 512);
        var data = render.Media.Data;
        var features = WebPChunk(data, "VP8X");
        features.Should().BeGreaterThanOrEqualTo(0);
        (data[features] & 0x10).Should().Be(0x10, "animated WebP must retain the GIF alpha channel");
        (data[features] & 0x02).Should().Be(0x02, "the selected fragment must remain animated");
        WebPChunk(data, "ANIM").Should().BeGreaterThanOrEqualTo(0);
        WebPChunk(data, "ANMF").Should().BeGreaterThanOrEqualTo(0);
        var poster = Path.Combine(_directory, "transparent-poster.webp");
        await File.WriteAllBytesAsync(poster, render.Media.PosterData);
        using var pixels = await Command("ffmpeg", ["-v", "error", "-i", poster, "-frames:v", "1", "-pix_fmt", "rgba", "-f", "rawvideo", "pipe:1"]);
        var rgba = pixels.ToArray();
        rgba[3].Should().Be(0, "the corner belongs to the transparent background");
        Enumerable.Range(0, rgba.Length / 4).Select(i => rgba[i * 4 + 3]).Should().Contain((byte)255,
            "the moving block must remain opaque");
        await render.CommitAsync();
    }

    [FfmpegFact]
    public async Task Video_ExportsMutedSquareH264_WithPosterAndUnchangedChosenDuration()
    {
        var source = await Fixture("video.mp4", ["-f", "lavfi", "-i", "testsrc2=s=80x40:r=24", "-f", "lavfi", "-i", "sine=frequency=440",
            "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac"]);
        var session = await Prepare(source, "video/mp4");
        await using var render = await _processor.RenderAsync(_actor, session.Id,
            new MediaRenderRequest(.25, 0, .5, 1, .1, .9, .5, "Animated"), 256);
        render.Media.ContentType.Should().Be("video/mp4");
        render.Media.DurationSeconds.Should().BeApproximately(.8, .001);
        var path = Path.Combine(_directory, "output.mp4");
        await File.WriteAllBytesAsync(path, render.Media.Data);
        using var probe = await Command("ffprobe", ["-v", "error", "-show_streams", "-show_format", "-of", "json", path]);
        using var json = JsonDocument.Parse(probe.ToArray());
        var tracks = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        tracks.Should().ContainSingle();
        tracks[0].GetProperty("codec_name").GetString().Should().Be("h264");
        tracks[0].GetProperty("width").GetInt32().Should().Be(256);
        tracks[0].GetProperty("height").GetInt32().Should().Be(256);
        render.Media.PosterData.Should().NotBeEmpty();
        await render.CommitAsync();
    }

    [FfmpegFact]
    public async Task LargeDecodedSource_RespectsConfiguredProcessMemoryBudget_AndCleansFailedSession()
    {
        var source = await Fixture("large.png", ["-f", "lavfi", "-i", "color=red@0.5:s=4096x3900,format=rgba", "-frames:v", "1"]);
        var root = Path.Combine(_directory, "memory-limited-sessions");
        var processor = new FfmpegProfileMediaProcessor(Options.Create(new ProfileMediaOptions
            { TemporaryDirectory = root, ProcessMemoryLimitBytes = 64L * 1024 * 1024 }),
            NullLogger<FfmpegProfileMediaProcessor>.Instance);
        using var stream = new MemoryStream(source);
        Func<Task> prepare = async () => await processor.PrepareAsync(_actor, stream, "large.png", "image/png");
        var failure = await prepare.Should().ThrowAsync<ValidationException>();
        failure.Which.Errors.Should().Contain(error => error.ErrorCode == "media_processing_memory_limit");
        Directory.EnumerateDirectories(root).Should().BeEmpty();
    }

    [FfmpegFact]
    public async Task MovAndWebM_AreDecodedAndCanBeSavedAsStillImages()
    {
        foreach (var (file, mime, codec) in new[] { ("clip.mov", "video/quicktime", "qtrle"), ("clip.webm", "video/webm", "libvpx-vp9") })
        {
            var source = await Fixture(file, ["-f", "lavfi", "-i", "testsrc=s=64x64:r=10", "-t", "0.5", "-c:v", codec]);
            var session = await Prepare(source, mime);
            session.Kind.Should().Be("Video");
            await using var render = await _processor.RenderAsync(_actor, session.Id,
                new MediaRenderRequest(0, 0, 1, 1, 0, 0, .2, "Still"), 256);
            render.Media.ContentType.Should().Be("image/webp");
            await render.CommitAsync();
        }
    }

    [FfmpegFact]
    public async Task InvalidSourcesAndSelections_AreRejectedWithoutLeakingTemporaryUploads()
    {
        Func<Task> svg = async () => await Prepare(Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"), "image/svg+xml");
        await svg.Should().ThrowAsync<ValidationException>();
        Func<Task> oversized = async () => await Prepare(new byte[FfmpegProfileMediaProcessor.MaxUploadBytes + 1], "image/png");
        await oversized.Should().ThrowAsync<ValidationException>();
        Directory.EnumerateDirectories(Path.Combine(_directory, "sessions")).Should().BeEmpty();
        var source = await Fixture("image.png", ["-f", "lavfi", "-i", "color=red:s=32x32", "-frames:v", "1"]);
        Func<Task> mismatch = async () => await Prepare(source, "image/jpeg");
        await mismatch.Should().ThrowAsync<ValidationException>();
        var session = await Prepare(source, "image/png");
        foreach (var bad in new[] { SquareStill() with { X = double.NaN }, SquareStill() with { X = .9 },
                     SquareStill() with { Width = .5 }, SquareStill() with { Mode = "Animated", EndSeconds = 1 } })
        {
            Func<Task> render = async () => await _processor.RenderAsync(_actor, session.Id, bad, 256);
            await render.Should().ThrowAsync<ValidationException>();
        }
        await _processor.CancelAsync(_actor, session.Id);
    }

    private async Task<MediaEditorSessionDto> Prepare(byte[] source, string contentType)
    {
        using var stream = new MemoryStream(source);
        return await _processor.PrepareAsync(_actor, stream, "../../ignored-name", contentType);
    }

    private async Task<byte[]> Fixture(string name, string[] arguments)
    {
        var path = Path.Combine(_directory, name);
        using var result = await Command("ffmpeg", ["-v", "error", "-nostdin", "-y", "-threads", "2", "-filter_threads", "1", .. arguments, path]);
        return await File.ReadAllBytesAsync(path);
    }

    private static async Task<MemoryStream> Command(string executable, string[] arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var output = new MemoryStream();
        var copying = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        await copying;
        var detail = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException(detail);
        output.Position = 0;
        return output;
    }

    private static MediaRenderRequest SquareStill() => new(0, 0, 1, 1, 0, 0, 0, "Still");
    private static int WebPChunk(byte[] data, string type)
    {
        for (var position = 12; position + 8 <= data.Length;)
        {
            var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4, 4)));
            if (Encoding.ASCII.GetString(data, position, 4) == type) return position + 8;
            position = checked(position + 8 + length + (length & 1));
        }
        return -1;
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }

    private sealed class FfmpegFactAttribute : FactAttribute
    {
        private static readonly Lazy<bool> Available = new(Check);
        public FfmpegFactAttribute()
        {
            if (!Available.Value) Skip = "FFmpeg is not installed.";
        }
        private static bool Check()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
                    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
                if (process is null) return false;
                var output = process.StandardOutput.ReadToEndAsync();
                var errors = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(5000)) { process.Kill(entireProcessTree: true); return false; }
                Task.WhenAll(output, errors).GetAwaiter().GetResult();
                return process.ExitCode == 0;
            }
            catch { return false; }
        }
    }
}
