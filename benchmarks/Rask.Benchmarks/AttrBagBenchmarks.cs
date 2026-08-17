#pragma warning disable RASK022 // keyless rows on purpose — see the note below

using BenchmarkDotNet.Attributes;
using Rask.Core;

namespace Rask.Benchmarks;

// RASK022 is suppressed for this file. Its rows ARE keyless, and correctly reported — but this benchmark
// renders the bare minimum around the one attribute it measures, and giving every row a Key would add a
// per-element write to both arms and move the absolute figures already recorded in the CHANGELOG
// (80.7 KB -> 63.52 KB). The A/B is between two spellings of Data, not a reconciliation claim, so the
// key would buy nothing here and cost the record its meaning.

// What `.Data("test-id", "primary")` costs against `.Data(new Dictionary<string, string?> { … })`,
// which is what a call site had to write before it existed.
//
// The claim being measured is narrow and worth stating precisely: a Dictionary for ONE attribute is
// three allocations — the dictionary, its bucket array and its entry array — and the bag is one
// object with two fields. Both are rebuilt on every render, because a chain step assigns its property
// every time the parent renders; this is per-render cost, not per-call-site cost.
//
// The tree is built inside the benchmark rather than in [GlobalSetup] on purpose. Setting up once and
// re-rendering would measure only the serializer, which is the same for both arms — the difference
// lives in constructing the bag, so constructing it is the thing to time.
[MemoryDiagnoser]
public partial class AttrBagBenchmarks : global::Rask.Core.RaskMarkup
{
    private const int Rows = 100;

    [Benchmark(Baseline = true)]
    public string Dictionary()
    {
        var rows = new List<Component>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            rows.Add(Div.Data(new Dictionary<string, string?> { ["test-id"] = "row" })[Span["x"]]);
        }

        return Div[rows].ToHtml();
    }

    [Benchmark]
    public string Pair()
    {
        var rows = new List<Component>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            rows.Add(Div.Data("test-id", "row")[Span["x"]]);
        }

        return Div[rows].ToHtml();
    }

    // Three attributes: the point where a bag's linear scan and an array of pairs stop being obviously
    // cheaper than hashing. Included so the trade-off is measured rather than assumed.
    [Benchmark]
    public string DictionaryThree()
    {
        var rows = new List<Component>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            rows.Add(Div.Data(new Dictionary<string, string?>
            {
                ["test-id"] = "row",
                ["state"] = "idle",
                ["index"] = "0",
            })[Span["x"]]);
        }

        return Div[rows].ToHtml();
    }

    [Benchmark]
    public string PairsThree()
    {
        var rows = new List<Component>(Rows);
        for (var i = 0; i < Rows; i++)
        {
            rows.Add(Div.Data(("test-id", "row"), ("state", "idle"), ("index", "0"))[Span["x"]]);
        }

        return Div[rows].ToHtml();
    }
}
