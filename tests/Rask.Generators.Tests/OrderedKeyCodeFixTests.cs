using Rask.Batteries.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK148 quick-fix: an entity's key made by Rask's ordered <c>New()</c>.</summary>
public class OrderedKeyCodeFixTests
{
    private const string Others = """
        public sealed class Plain : Aggregate<PlainId>
        {
            private Plain() { }
            public static Plain Make() => new() { Id = new PlainId(Guid.NewGuid()) };
        }
        public sealed class Note : Aggregate<Guid>
        {
            private Note() { }
            public static Note Write(TimeProvider clock) => new() { Id = Guid.CreateVersion7(clock.GetUtcNow()) };
            public static Note Again() => new() { Id = Guid.NewGuid() };
        }
        """;

    // One flagged key unless `others` adds more, so a single fix is the one under test.
    private static string Source(string member, string usings = "using Rask;", string others = "") => $$"""
        using System;
        {{usings}}
        using Rask.Data;
        namespace Shop;
        public readonly partial record struct OrderId(Guid Value)
        {
            public static OrderId New() => new(global::Rask.OrderedGuid.New());
        }
        public readonly record struct PlainId(Guid Value);
        public sealed class Order : Aggregate<OrderId>
        {
            private Order() { }
            {{member}}
        }
        {{others}}
        """;

    private static Task<string> Fix(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new OrderedKeyAnalyzer(), new OrderedKeyCodeFixProvider(), "RASK148", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

    [Theory]
    [InlineData("public static Order Place() => new() { Id = new OrderId(Guid.CreateVersion7()) };", "new() { Id = OrderId.New() }")]
    [InlineData("public static Order Place() => new() { Id = new(Guid.NewGuid()) };", "new() { Id = OrderId.New() }")]
    [InlineData("public static Order Place(TimeProvider t) => new() { Id = new OrderId(Guid.CreateVersion7(t.GetUtcNow())) };", "new() { Id = new OrderId(OrderedGuid.New(t)) }")]
    public async Task The_RASK148_fix_makes_a_typed_key_with_its_New_and_keeps_a_time_source(string written, string expected)
    {
        var source = Source(written);

        var fixed_ = await Fix(source);

        Assert.Contains(expected, fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fix_all_makes_every_key_of_the_document_ordered()
    {
        var source = Source("public static Order Place() => new() { Id = new OrderId(Guid.CreateVersion7()) };", others: Others);

        var fixed_ = await CodeFixHarness.ApplyAnalyzerFixAllAsync(
            new OrderedKeyAnalyzer(), new OrderedKeyCodeFixProvider(), "RASK148", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

        Assert.Contains("new() { Id = OrderId.New() }", fixed_, StringComparison.Ordinal);
        Assert.Contains("new() { Id = new PlainId(OrderedGuid.New()) }", fixed_, StringComparison.Ordinal);
        Assert.Contains("new() { Id = OrderedGuid.New(clock) }", fixed_, StringComparison.Ordinal);
        Assert.Contains("new() { Id = OrderedGuid.New() }", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.NewGuid", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateVersion7", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK148_fix_qualifies_OrderedGuid_where_Rask_is_not_imported()
    {
        var source = Source("public static Order Place() => new() { Id = new OrderId(Guid.CreateVersion7(TimeProvider.System.GetUtcNow())) };", usings: "");

        var fixed_ = await Fix(source);

        Assert.Contains("new() { Id = new OrderId(global::Rask.OrderedGuid.New(TimeProvider.System)) }", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK148_fix_is_withheld_for_a_moment_that_is_no_time_source()
    {
        var source = Source("public static Order Place(DateTimeOffset at) => new() { Id = new OrderId(Guid.CreateVersion7(at)) };");

        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new OrderedKeyAnalyzer(), new OrderedKeyCodeFixProvider(), "RASK148", source, "Rask.Data", "Rask.Cqrs", "Rask.Wire");

        Assert.False(offered);
    }
}
