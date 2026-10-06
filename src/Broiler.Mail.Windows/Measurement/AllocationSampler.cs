// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// Broiler Code Assurance
// ----------------------
// Relevant units:   8
// Annotated:        0/8
// Exempt:           3
// Human-reviewed:   0/8
// IP risk:          not assessed
// Security risk:    not assessed
// Criteria:         0/0
// Resource impact:  not assessed
// Unverified:       8
//
// GENERATED - DO NOT EDIT MANUALLY

using System.Diagnostics.Tracing;

namespace Broiler.Mail.Windows.Measurement;

/// <summary>
/// Samples managed allocations by type from the runtime's GC allocation ticks (roughly one per
/// 100 KB allocated). Enabled by BROILER_MAIL_SAMPLE_ALLOCATIONS=1 for diagnosis on a JIT build; the
/// sampled amounts show which types dominate, not exact totals.
/// </summary>
internal sealed class AllocationSampler : EventListener
{
    private const EventKeywords GcKeyword = (EventKeywords)0x1;
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Bytes, int Samples)> _byType = new(StringComparer.Ordinal);
    private volatile bool _recording;

    public static bool Requested => Environment.GetEnvironmentVariable("BROILER_MAIL_SAMPLE_ALLOCATIONS") == "1";

    public void Start()
    {
        lock (_gate) _byType.Clear();
        _recording = true;
    }

    public void Stop() => _recording = false;

    public IReadOnlyList<(string Type, double SampledMb, int Samples)> Top(int count)
    {
        lock (_gate)
            return _byType.OrderByDescending(entry => entry.Value.Bytes).Take(count)
                .Select(entry => (entry.Key, entry.Value.Bytes / 1048576.0, entry.Value.Samples)).ToArray();
    }

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, EventLevel.Verbose, GcKeyword);
    }

    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        if (!_recording || !data.EventName!.StartsWith("GCAllocationTick", StringComparison.Ordinal) || data.PayloadNames is null) return;
        int typeIndex = data.PayloadNames.IndexOf("TypeName");
        int amountIndex = data.PayloadNames.IndexOf("AllocationAmount64");
        if (typeIndex < 0 || amountIndex < 0) return;
        string type = data.Payload![typeIndex] as string ?? "?";
        long amount = Convert.ToInt64(data.Payload[amountIndex]);
        lock (_gate)
        {
            _byType.TryGetValue(type, out var total);
            _byType[type] = (total.Bytes + amount, total.Samples + 1);
        }
    }
}
