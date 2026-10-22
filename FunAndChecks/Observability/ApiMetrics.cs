using System.Diagnostics;
using System.Globalization;
using FunAndChecks.Application.Auditing;
using Prometheus;

namespace FunAndChecks.Observability;

/// <summary>Per-host registry: bounded API labels and process/GC samples.</summary>
public sealed class ApiMetrics
{
    public CollectorRegistry Registry { get; } = Metrics.NewCustomRegistry();
    public Counter Requests { get; }
    public Histogram Duration { get; }
    public Gauge InFlight { get; }
    public Counter AuditDropped { get; }
    public Counter AuditWritten { get; }
    private readonly Counter _cpu;
    private readonly Gauge _workingSet, _managedMemory;
    private readonly Counter _collections;
    private readonly Counter _captureFailures;
    private readonly object _sampleLock = new();
    private double _lastCpu;
    private long _lastCaptureFailures;
    private readonly long[] _lastCollections = new long[3];

    public ApiMetrics()
    {
        var factory = Metrics.WithCustomRegistry(Registry);
        var labels = new[] { "route", "method", "status" };
        Requests = factory.CreateCounter("funandchecks_http_requests_total", "Completed business API requests.", new CounterConfiguration { LabelNames = labels });
        Duration = factory.CreateHistogram("funandchecks_http_request_duration_seconds", "Business API request duration in seconds.", new HistogramConfiguration
        {
            LabelNames = labels,
            Buckets = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10]
        });
        InFlight = factory.CreateGauge("funandchecks_http_requests_in_flight", "Business API requests currently executing.");
        AuditDropped = factory.CreateCounter("funandchecks_audit_events_dropped_total", "Audit events not written to the local buffer.", new CounterConfiguration { LabelNames = ["reason"] });
        AuditWritten = factory.CreateCounter("funandchecks_audit_events_written_total", "Audit events written to the local buffer.");
        _captureFailures = factory.CreateCounter("funandchecks_audit_capture_failures_total", "Audit snapshots or actor enrichment that could not be captured.");
        _cpu = factory.CreateCounter("process_cpu_seconds_total", "Total CPU time used by the API process in seconds.");
        _workingSet = factory.CreateGauge("process_working_set_bytes", "API process working set in bytes.");
        _managedMemory = factory.CreateGauge("dotnet_total_memory_bytes", "Managed memory in bytes.");
        _collections = factory.CreateCounter("dotnet_collection_count_total", "Garbage collections per generation.", new CounterConfiguration { LabelNames = ["generation"] });
        Registry.AddBeforeCollectCallback(SampleRuntime);
    }

    private void SampleRuntime()
    {
        lock (_sampleLock)
        {
            using var process = Process.GetCurrentProcess();
            var cpu = process.TotalProcessorTime.TotalSeconds;
            _cpu.Inc(Math.Max(0, cpu - _lastCpu));
            _lastCpu = cpu;
            _workingSet.Set(process.WorkingSet64);
            _managedMemory.Set(GC.GetTotalMemory(false));
            var captureFailures = AdminAuditDiagnostics.CaptureFailures;
            _captureFailures.Inc(Math.Max(0, captureFailures - _lastCaptureFailures));
            _lastCaptureFailures = captureFailures;
            for (var generation = 0; generation < _lastCollections.Length; generation++)
            {
                var count = GC.CollectionCount(generation);
                _collections.WithLabels(generation.ToString(CultureInfo.InvariantCulture)).Inc(Math.Max(0, count - _lastCollections[generation]));
                _lastCollections[generation] = count;
            }
        }
    }
}
