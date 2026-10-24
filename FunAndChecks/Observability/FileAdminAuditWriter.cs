using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FunAndChecks.Application.Auditing;
using Microsoft.Extensions.Options;

namespace FunAndChecks.Observability;

/// <summary>A bounded non-blocking local audit buffer. Remote delivery is Alloy's responsibility.</summary>
public sealed class FileAdminAuditWriter : BackgroundService, IAdminAuditWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    private readonly AdminAuditOptions _options;
    private readonly ApiMetrics _metrics;
    private readonly Channel<AdminAuditEvent> _pending;
    private StreamWriter? _file;
    private string? _date;
    private long _fileBytes;
    private int _sequence;

    public FileAdminAuditWriter(IOptions<AdminAuditOptions> options, ApiMetrics metrics)
    {
        _options = options.Value;
        _metrics = metrics;
        _pending = Channel.CreateBounded<AdminAuditEvent>(new BoundedChannelOptions(Math.Clamp(_options.QueueCapacity, 1, 100_000))
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
        });
    }

    public bool TryWrite(AdminAuditEvent auditEvent)
    {
        if (!_options.Enabled) return false;
        try
        {
            if (_pending.Writer.TryWrite(auditEvent)) return true;
            _metrics.AuditDropped.WithLabels("queue_full_or_stopping").Inc();
        }
        catch { _metrics.AuditDropped.WithLabels("enqueue_error").Inc(); }
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var auditEvent in _pending.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    var safe = auditEvent with
                    {
                        Before = AuditValueSanitizer.Sanitize(auditEvent.Before),
                        After = AuditValueSanitizer.Sanitize(auditEvent.After),
                        Summary = AuditValueSanitizer.Redact(auditEvent.Summary)
                    };
                    var line = JsonSerializer.Serialize(safe, Json);
                    var bytes = Encoding.UTF8.GetByteCount(line) + 1;
                    await EnsureFileAsync(bytes);
                    await _file!.WriteLineAsync(line.AsMemory(), stoppingToken);
                    await _file.FlushAsync(stoppingToken);
                    _fileBytes += bytes;
                    _metrics.AuditWritten.Inc();
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _metrics.AuditDropped.WithLabels("shutdown").Inc();
                    break;
                }
                catch
                {
                    _metrics.AuditDropped.WithLabels("write_error").Inc();
                    try { if (_file is not null) await _file.DisposeAsync(); } catch { }
                    _file = null;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            while (_pending.Reader.TryRead(out _)) _metrics.AuditDropped.WithLabels("shutdown").Inc();
            try { if (_file is not null) await _file.DisposeAsync(); } catch { }
            _file = null;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pending.Writer.TryComplete();
        try { if (ExecuteTask is not null) await ExecuteTask.WaitAsync(cancellationToken); }
        finally { await base.StopAsync(cancellationToken); }
    }

    private async Task EnsureFileAsync(int nextBytes)
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var limit = Math.Max(1024, _options.FileSizeLimitBytes);
        if (_file is not null && _date == date && _fileBytes + nextBytes <= limit) return;
        if (_file is not null) { await _file.DisposeAsync(); _file = null; }
        if (_date != date) { _date = date; _sequence = 0; }
        Directory.CreateDirectory(_options.Directory);
        string path;
        do
        {
            var suffix = _sequence == 0 ? "" : "_" + _sequence.ToString("D3", CultureInfo.InvariantCulture);
            path = Path.Combine(_options.Directory, $"admin-audit-{date}{suffix}.jsonl");
            _sequence++;
        } while (File.Exists(path) && new FileInfo(path).Length + nextBytes > limit);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete, 4096, useAsync: true);
        _fileBytes = stream.Length;
        _file = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n" };
        Prune(path);
    }

    private void Prune(string activePath)
    {
        // Only our own local buffer files; original Serilog files/imports are untouched.
        var files = new DirectoryInfo(_options.Directory).GetFiles("admin-audit-*.jsonl")
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        var cutoff = DateTime.UtcNow.AddDays(-Math.Clamp(_options.RetainedDays, 1, 365));
        for (var index = 0; index < files.Length; index++)
        {
            var file = files[index];
            if (file.FullName == Path.GetFullPath(activePath)) continue;
            if (index >= Math.Clamp(_options.RetainedFileCount, 1, 10_000) || file.LastWriteTimeUtc < cutoff)
                try { file.Delete(); } catch { }
        }
    }
}
