using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Core.Tests.Forms;

// The reflection-only target evaluator (no Expression.Compile on the render hot path) covers every
// documented Bind/For shape directly; these tests lock the Compile() fallback for the rarer,
// undocumented shapes that used to work — so the rewrite is behaviour-preserving, not just faster.
public sealed class ExpressionAccessorFallbackTests
{
    [Fact]
    public void Parse_MethodCallMidChain_ResolvesViaFallback()
    {
        var p = new Node { Child = new Node { Name = "leaf" } };

        // `p.Self().Child.Name` — a method call sits mid-chain, which the reflection walker does not
        // special-case, so the target evaluation falls back to compiling the sub-expression.
        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => p.Self().Child!.Name));

        Assert.Same(p.Child, acc.Target);
        Assert.Equal("Name", acc.PropertyName);
        Assert.Equal("leaf", acc.Getter());
    }

    [Fact]
    public void Parse_ArithmeticIndex_ResolvesViaFallback()
    {
        var items = new List<Node> { new() { Name = "zero" }, new() { Name = "one" }, new() { Name = "two" } };
        var i = 0;

        // `items[i + 1]` — an arithmetic expression as the index argument is not one of the
        // reflection-walked shapes, so it routes through the compile fallback.
        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => items[i + 1].Name));

        Assert.Same(items[1], acc.Target);
        Assert.Equal("one", acc.Getter());
    }

    [Fact]
    public void Parse_MethodIndexedThenProperty_ResolvesViaFallback()
    {
        // A user-defined method returning the item, then a property — also mid-chain method call.
        var box = new Box();
        box.Add(new Node { Name = "boxed" });

        var acc = ExpressionAccessor.Parse((Expression<Func<string>>)(() => box.At(0).Name));

        Assert.Same(box.At(0), acc.Target);
        Assert.Equal("boxed", acc.Getter());
    }

    [Fact]
    public void Parse_MissingDictionaryKey_SurfacesOriginalException()
    {
        // A target-chain indexer that throws must surface its own exception (as the old
        // Expression.Compile path did), not a reflection TargetInvocationException wrapper.
        var settings = new Dictionary<string, Node>();

        Assert.Throws<KeyNotFoundException>(() =>
            ExpressionAccessor.Parse((Expression<Func<string>>)(() => settings["absent"].Name)));
    }

    [Fact]
    public void Parse_ThrowingPropertyGetter_SurfacesOriginalException()
    {
        var box = new ThrowingBox();

        Assert.Throws<InvalidOperationException>(() =>
            ExpressionAccessor.Parse((Expression<Func<string>>)(() => box.Inner.Name)));
    }

    private sealed class Node
    {
        public string Name { get; set; } = "";
        public Node? Child { get; set; }
        public Node Self() => this;
    }

    private sealed class ThrowingBox
    {
        public Node Inner => throw new InvalidOperationException("boom");
    }

    private sealed class Box
    {
        private readonly List<Node> _nodes = [];
        public void Add(Node n) => _nodes.Add(n);
        public Node At(int i) => _nodes[i];
    }
}
