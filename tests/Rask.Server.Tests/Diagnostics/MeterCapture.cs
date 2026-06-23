using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Rask.Server.Tests.Diagnostics;

// Captures measurements from a single Meter instance via a MeterListener, scoped by reference so
// measurements from any other RaskMetrics constructed concurrently are ignored. Disposable — replaces
// the per-test listener scaffolding that was copy-pasted (and, in one place, leaked) across the suite.
internal sealed class MeterCapture : IDisposable
{
    private readonly ConcurrentBag<(string Name, KeyValuePair<string, object?>[] Tags)> _measurements = new();
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, int> _gauges = new();
    private readonly ConcurrentDictionary<string, int> _histogramSamples = new();
    private readonly MeterListener _listener;

    private MeterCapture(Meter meter)
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (ReferenceEquals(inst.Meter, meter))
                {
                    l.EnableMeasurementEvents(inst);
                }
            }
        };
        _listener.SetMeasurementEventCallback<long>((inst, measurement, tags, _) =>
        {
            _counters.AddOrUpdate(inst.Name, measurement, (_, v) => v + measurement);
            _measurements.Add((inst.Name, tags.ToArray()));
        });
        _listener.SetMeasurementEventCallback<int>((inst, measurement, _, _) => _gauges[inst.Name] = measurement);
        _listener.SetMeasurementEventCallback<double>((inst, _, _, _) =>
            _histogramSamples.AddOrUpdate(inst.Name, 1, (_, v) => v + 1));
        _listener.Start();
    }

    public static MeterCapture For(Meter meter) => new(meter);

    public void RecordObservable() => _listener.RecordObservableInstruments();

    public long Counter(string name) => _counters.GetValueOrDefault(name);

    public int Gauge(string name) => _gauges.GetValueOrDefault(name);

    public int HistogramSampleCount(string name) => _histogramSamples.GetValueOrDefault(name);

    public IEnumerable<string> TagValues(string instrument, string tagKey) =>
        _measurements
            .Where(m => m.Name == instrument)
            .SelectMany(m => m.Tags)
            .Where(t => t.Key == tagKey)
            .Select(t => t.Value as string)
            .Where(v => v is not null)!;

    public void Dispose() => _listener.Dispose();
}
