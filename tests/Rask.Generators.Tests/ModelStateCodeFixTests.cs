using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Data.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>The RASK084 and RASK085 quick-fixes, applied and compared.</summary>
public class ModelStateCodeFixTests
{
    private static string Entity(string members) => $$"""
        using System;
        using System.Collections.Generic;
        using Rask.Data;
        namespace Shop;
        public sealed class OrderLine : Model<Guid> { }
        public sealed class Order : Model<Guid>
        {
            {{members}}
        }
        """;

    private static Task<string> Fix080(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new ModelStateMutationAnalyzer(), new ModelStateMutationCodeFixProvider(), "RASK084", source, "Rask.Data", "Rask.Cqrs");

    private static Task<bool> Offered080(string source) =>
        CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new ModelStateMutationAnalyzer(), new ModelStateMutationCodeFixProvider(), "RASK084", source, "Rask.Data", "Rask.Cqrs");

    // EF Core too, so a `b.HasMany(o => o.Lines)` in a test source binds — an unbound one is never rewritten,
    // and the test that it is left alone would pass for the wrong reason.
    private static Task<string> Fix081(string source) =>
        CodeFixHarness.ApplyAnalyzerFixAsync(
            new EntityCollectionExposureAnalyzer(), new EntityCollectionExposureCodeFixProvider(), "RASK085", source,
            "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    private static Task<bool> Offered081(string source) =>
        CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new EntityCollectionExposureAnalyzer(), new EntityCollectionExposureCodeFixProvider(), "RASK085", source,
            "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore");

    // ---- RASK084 ----

    [Fact]
    public async Task Rask084_MakesASetterPrivate()
    {
        var fixed_ = await Fix080(Entity("public string Name { get; set; } = \"\";"));
        Assert.Contains("public string Name { get; private set; } = \"\";", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rask084_MakesAnInitPrivate()
    {
        var fixed_ = await Fix080("""
            using Rask.Data;
            namespace Shop;
            public sealed record Address : IValueObject
            {
                public string City { get; init; } = "";
            }
            """);
        Assert.Contains("public string City { get; private init; } = \"\";", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rask084_KeepsAMultiLineAccessorListInShape()
    {
        var fixed_ = await Fix080(Entity("""
            public string Name
                {
                    get;
                    set;
                }
            """));
        Assert.Contains("        private set;", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rask084_MakesAPublicFieldPrivate()
    {
        var fixed_ = await Fix080(Entity("public int Stock;"));
        Assert.Contains("private int Stock;", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("public int Stock", fixed_, StringComparison.Ordinal);
    }

    // `{ private set => … }` is CS0276: an accessor modifier needs a second accessor to differ from.
    [Fact]
    public async Task Rask084_IsWithheldForASetOnlyProperty() =>
        Assert.False(await Offered080(Entity("""
            private string _hash = "";
                public string Hash => _hash;
                public string Password { set => _hash = value; }
            """)));

    [Fact]
    public async Task Rask084_IsOfferedForAGetSetProperty() =>
        Assert.True(await Offered080(Entity("public string Name { get; set; } = \"\";")));

    [Fact]
    public async Task Rask084_IsWithheldForARequiredProperty() =>
        Assert.False(await Offered080(Entity("public required string Name { get; set; }")));

    [Fact]
    public async Task Rask084_IsWithheldForAPositionalRecordStructParameter() =>
        Assert.False(await Offered080("""
            using Rask.Data;
            namespace Shop;
            public record struct Weight(decimal Grams) : IValueObject;
            """));

    // ---- RASK085 ----

    [Theory]
    [InlineData("public List<OrderLine> Lines { get; private set; } = new();")]
    [InlineData("public List<OrderLine> Lines { get; private set; } = [];")]
    [InlineData("public List<OrderLine> Lines { get; private set; } = new List<OrderLine>();")]
    [InlineData("public List<OrderLine> Lines { get; private set; }")]
    [InlineData("public ICollection<OrderLine> Lines { get; } = [];")]
    public async Task Rask085_RewritesTheAutoPropertyToAReadOnlyViewOverAField(string member)
    {
        var fixed_ = await Fix081(Entity(member + "\n    public void Add(OrderLine line) => Lines.Add(line);"));

        Assert.Contains("private readonly List<OrderLine> _lines = [];", fixed_, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyCollection<OrderLine> Lines => _lines;", fixed_, StringComparison.Ordinal);
        Assert.Contains("public void Add(OrderLine line) => _lines.Add(line);", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("global::", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rask085_KeepsAHashSet()
    {
        var fixed_ = await Fix081(Entity("public HashSet<OrderLine> Lines { get; } = [];"));

        Assert.Contains("private readonly HashSet<OrderLine> _lines = [];", fixed_, StringComparison.Ordinal);
        Assert.Contains("public IReadOnlyCollection<OrderLine> Lines => _lines;", fixed_, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rask085_PutsTheFieldBeforeTheDocCommentAndLeavesTheCommentOnTheProperty()
    {
        var fixed_ = await Fix081(Entity("""
            public string Code { get; private set; } = "";

                /// <summary>The lines.</summary>
                public List<OrderLine> Lines { get; private set; } = new();
            """));

        Assert.Contains(
            "\n\n    private readonly List<OrderLine> _lines = [];\n    /// <summary>The lines.</summary>\n    public IReadOnlyCollection<OrderLine> Lines => _lines;",
            fixed_.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    // Only THIS instance's collection is the field's to stand in for. A lambda parameter's `o.Lines` in EF's
    // `HasMany` would become a second, explicit `_lines` navigation beside the convention-found one; `nameof`
    // would silently become "_lines". The source is compiled before AND after, so every reference left alone
    // demonstrably bound to the property — a miss cannot pass as a decision.
    [Fact]
    public async Task Rask085_RewritesOnlyReferencesThroughThisInstance()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using Rask.Data;
            namespace Shop;
            public sealed class OrderLine : Model<Guid> { }
            public sealed class Order : Model<Guid>
            {
                public List<OrderLine> Lines { get; private set; } = new();
                public void Add(OrderLine line) => Lines.Add(line);
                public void AddExplicitly(OrderLine line) => this.Lines.Add(line);
                public int Count => Lines.Count;
                public bool Same(Order other) => other.Lines.Count == Lines.Count;
                public string Label => nameof(Lines);
                public static int CountOf(Order order) => order.Lines.Count;
                public static void Configure(EntityTypeBuilder<Order> b) => b.HasMany(o => o.Lines).WithOne();
            }
            public static class Outside
            {
                public static int Count(Order order) => order.Lines.Count();
            }
            """;
        AssertCompiles(source);

        var fixed_ = await Fix081(source);
        AssertCompiles(fixed_);

        Assert.Contains("public void Add(OrderLine line) => _lines.Add(line);", fixed_, StringComparison.Ordinal);
        Assert.Contains("public void AddExplicitly(OrderLine line) => this._lines.Add(line);", fixed_, StringComparison.Ordinal);
        Assert.Contains("public int Count => _lines.Count;", fixed_, StringComparison.Ordinal);
        Assert.Contains("other.Lines.Count == _lines.Count", fixed_, StringComparison.Ordinal);
        Assert.Contains("public string Label => nameof(Lines);", fixed_, StringComparison.Ordinal);
        Assert.Contains("public static int CountOf(Order order) => order.Lines.Count;", fixed_, StringComparison.Ordinal);
        Assert.Contains(
            "public static void Configure(EntityTypeBuilder<Order> b) => b.HasMany(o => o.Lines).WithOne();",
            fixed_,
            StringComparison.Ordinal);
        Assert.Contains("order.Lines.Count()", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("o._lines", fixed_, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(_lines)", fixed_, StringComparison.Ordinal);
    }

    private static void AssertCompiles(string source)
    {
        var references = GeneratorDriverFixture.BuildReferences().AddRange(
            new[] { "Rask.Data", "Rask.Cqrs", "Microsoft.EntityFrameworkCore" }.Select(static name =>
                (MetadataReference)MetadataReference.CreateFromFile(Assembly.Load(name).Location)));
        var compilation = CSharpCompilation.Create(
            "Check",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var errors = compilation.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "Does not compile:\n  " + string.Join("\n  ", errors) + "\n\n" + source);
    }

    [Fact]
    public async Task Rask085_QualifiesTheCollectionTypesWhenTheirNamespaceIsNotImported()
    {
        var fixed_ = await Fix081("""
            using System;
            using Rask.Data;
            namespace Shop;
            public sealed class OrderLine : Model<Guid> { }
            public sealed class Order : Model<Guid>
            {
                public System.Collections.Generic.List<OrderLine> Lines { get; } = [];
            }
            """);

        Assert.Contains("System.Collections.Generic.List<OrderLine> _lines = [];", fixed_, StringComparison.Ordinal);
        Assert.Contains("System.Collections.Generic.IReadOnlyCollection<OrderLine> Lines => _lines;", fixed_, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public List<OrderLine> Lines { get; private set; } = [new OrderLine()];")]
    [InlineData("public List<OrderLine> Lines { get; private set; } = new(4);")]
    [InlineData("public List<OrderLine> Lines { get => field; private set => field = value; } = [];")]
    [InlineData("public required List<OrderLine> Lines { get; set; }")]
    [InlineData("public List<OrderLine> Lines { get; private set; } = [];\n    public void Reset() => Lines = [];")]
    [InlineData("public List<OrderLine> Lines { get; private set; } = [];\n    private int _lines;")]
    public async Task Rask085_IsWithheldWhereTheRewriteCouldNotKeepTheMeaning(string members) =>
        Assert.False(await Offered081(Entity(members)));
}
