using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;
using Xunit;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK045 — a component a chain produced is assigned to afterwards.
/// </summary>
/// <remarks>
///     <para>
///         The rule was documented in <c>docs/diagnostics.md</c> as an active warning, with an example
///         and suppression instructions, and had no descriptor anywhere in <c>src/</c> — so it never
///         fired. A documented diagnostic that cannot fire is worse than an undocumented gap: a reader
///         cannot tell it apart from a codebase with no violations, which is precisely how RASK034 spent
///         its whole life.
///     </para>
///     <para>
///         So the cases that matter here are the ones asserting it DOES fire, and <c>AnalyzeAsync</c>
///         fails the test if its source stops compiling — a file with a compile error reports no
///         analyzer diagnostics at all and would pass every "is not reported" case for the wrong reason.
///     </para>
/// </remarks>
public class ChainAssignedAfterwardsAnalyzerTests
{
    private const string Components = """
        namespace Demo
        {
            public sealed partial class Card : Rask.Core.Component
            {
                public string? Note { get; set; }
                public string? Title { get; set; }
            }

            public sealed partial class Panel : Rask.Core.Component
            {
                public string? Note { get; set; }
            }
        }
        """;

    // ---------- it fires ----------

    [Fact]
    public async Task AWriteAfterAChainIsReported()
    {
        var d = Assert.Single(await AnalyzeAsync("""
            var c = Card.Note("a");
            c.Note = "b";
            """));

        Assert.Equal("RASK045", d.Id);
        Assert.Contains("Note", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWriteToADifferentPropertyIsReportedToo() =>
        // The disagreement is the point, but so is the invisibility: a property the chain never named
        // is still one the reader of the call site cannot see being set.
        Assert.Single(await AnalyzeAsync("""
            var c = Card.Note("a");
            c.Title = "t";
            """));

    [Fact]
    public async Task AnUnqualifiedEntryWithNoStepsIsNotReported() =>
        // A deliberate miss, recorded so it is not mistaken for an oversight. `Card` unqualified binds
        // to a per-host forwarder, which carries nothing at the symbol level to tell it from a
        // hand-written property returning a component. Guessing would put a warning on correct code,
        // which is worse than the miss -- and a chain that named no steps is not where the two answers
        // disagree anyway.
        Assert.Empty(await AnalyzeAsync("""
            var c = Card;
            c.Note = "b";
            """));

    [Fact]
    public async Task AChainClosedByTheChildrenIndexerCounts() =>
        Assert.Single(await AnalyzeAsync("""
            var c = Card.Note("a")["x"];
            ((Card)c).Note = "b";
            """));

    // ---------- it stays silent ----------

    [Fact]
    public async Task AComponentFromAnOrdinaryMethodIsNotReported() =>
        // The surface it came through is what decides. A helper returning a component promises nothing
        // about being the whole story, so completing it afterwards is ordinary code.
        Assert.Empty(await AnalyzeAsync("""
            var c = Make();
            c.Note = "b";
            """));

    [Fact]
    public async Task WritingToThisOwnPropertyIsNotReported() =>
        // A component managing its own state is not this rule's business.
        Assert.Empty(await AnalyzeAsync("Note = \"b\";"));

    [Fact]
    public async Task ReadingAPropertyOffAChainIsNotAWrite() =>
        Assert.Empty(await AnalyzeAsync("""
            var c = Card.Note("a");
            var read = c.Note;
            """));

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string body)
    {
        var page = $$"""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    public string? Note { get; set; }

                    private static Card Make() => Card;

                    protected override Rask.Core.Component? Render()
                    {
                        {{body}}
                        return null;
                    }
                }
            }
            """;

        var source = Components + "\n" + page;

        // The compilation AFTER the factory generator has run: the chain syntax binds to generated
        // entries, so analyzing the bare source would leave every chain expression an error type and
        // this rule would report nothing and pass for the wrong reason.
        var compilation = BuilderGeneratorHarness.Compile(source);

        // A body that does not compile produces no diagnostics, which would pass every "is not reported"
        // case above for entirely the wrong reason.
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
        {
            throw new Xunit.Sdk.XunitException(
                "The test source does not compile, so no analyzer result means anything:\n  "
                + string.Join("\n  ", errors.Select(e => e.ToString())));
        }

        var withAnalyzer = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new ChainAssignedAfterwardsAnalyzer()));

        return await withAnalyzer.GetAnalyzerDiagnosticsAsync();
    }
}
