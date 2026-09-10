using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using Rask.Core;

namespace Rask.Benchmarks;

// A <select> rendered end to end, which the suite had no view of.
//
// BoundControlRenderBenchmarks looks like it covers this and does not: every arm of it is an Input, so
// Select's own WriteAttributes — the option marking, the selection set, and the handler choice at the
// bottom of it — is off its path entirely. That gap is why adding a branch to that method (the
// OnSelect/OnSelectAsync hook) had nothing to measure it.
//
// Four arms, and the pairing is the point rather than the absolute numbers:
//
//   ControlledPlain   Select.Value with no callback — the arm the new handler branch runs through and
//                     falls out of, so it is where a cost would land if the branch had one
//   ControlledChange  Select.Value + OnChange — the same path, reaching the single-value handler
//   ControlledSelect  Select.Value + OnSelect  — the new values-shaped handler
//   Bound             Select.Bind — untouched by the hook, and the arm most apps actually write
//
// Bound is deliberately NOT the baseline: it carries the accessor, the auto-created EditContext and the
// validator registration, so reading the others against it would attribute all of that to the option
// list. ControlledPlain is the floor.
[MemoryDiagnoser]
public partial class SelectRenderBenchmarks : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Values =
        ["hu", "gb", "ie", "de", "fr", "es", "it", "nl", "pl", "pt"];

    private readonly CountryModel _model = new();

    private Expression<Func<string>> _bound = null!;

    [GlobalSetup]
    public void Setup() => _bound = () => _model.Country;

    [Benchmark(Baseline = true)]
    public string ControlledPlain() =>
        Select.Value(_model.Country)[Options()].ToHtml();

    [Benchmark]
    public string ControlledChange() =>
        Select.Value(_model.Country).OnChange(NoteOne)[Options()].ToHtml();

    [Benchmark]
    public string ControlledSelect() =>
        Select.Value(_model.Country).Multiple(true).OnSelect(NoteMany)[Options()].ToHtml();

    [Benchmark]
    public string Bound() =>
        Select.Bind(_bound)[Options()].ToHtml();

    private static IEnumerable<Component?> Options()
    {
        foreach (var value in Values)
        {
            yield return Option.Key(value).Value(value)[value];
        }
    }

    private void NoteOne(string value) => _model.Country = value;

    private void NoteMany(IReadOnlyList<string> values) =>
        _model.Country = values.Count == 0 ? "" : values[0];

    private sealed class CountryModel
    {
        public string Country { get; set; } = "gb";
    }
}
