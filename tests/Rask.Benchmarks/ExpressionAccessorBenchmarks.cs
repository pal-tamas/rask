using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using Rask.Core.Forms;

namespace Rask.Benchmarks;

// ExpressionAccessor.Parse runs inside WriteAttributes for every bound Input/Select/Textarea/Bs*
// control, on EVERY render. It used to compile a throwaway lambda (Expression.Compile) just to read
// the target once; the reflection-only evaluator walks the tree instead. This measures the per-render
// cost across the common Bind/For shapes — Allocated is the headline metric.
[MemoryDiagnoser]
public class ExpressionAccessorBenchmarks
{
    private readonly Model _model = new();
    private readonly int _index = 1;

    private Expression<Func<string>> _simple = null!;
    private Expression<Func<string>> _nested = null!;
    private Expression<Func<string>> _listIndexer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _simple = () => _model.Name;
        _nested = () => _model.Address.Street;
        _listIndexer = () => _model.Items[_index].Street;
    }

    [Benchmark(Baseline = true)]
    public ExpressionAccessor.Accessor Parse_Simple() => ExpressionAccessor.Parse(_simple);

    [Benchmark]
    public ExpressionAccessor.Accessor Parse_NestedChain() => ExpressionAccessor.Parse(_nested);

    [Benchmark]
    public ExpressionAccessor.Accessor Parse_ListIndexer() => ExpressionAccessor.Parse(_listIndexer);

    private sealed class Model
    {
        public string Name { get; set; } = "name";
        public Address Address { get; set; } = new();
        public List<Address> Items { get; set; } = [new(), new()];
    }

    private sealed class Address
    {
        public string Street { get; set; } = "street";
    }
}
