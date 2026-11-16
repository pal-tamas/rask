using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>
///     The RASK118 / RASK043 quick-fix: the class that names a chain entry becomes a markup host —
///     <c>[Rask.Core.RaskMarkup]</c> and <c>partial</c>, on it and on every type it is nested in — and the
///     chain that did not compile then does.
/// </summary>
public class MarkupHostCodeFixTests
{
    private const string Receipt = """
        using Rask.Core;

        namespace Shop;

        public sealed class OrderReceipt : Component
        {
            public int? OrderId { get; set; }

            protected override Component? Render() => null;
        }

        """;

    [Fact]
    public async Task The_RASK118_fix_marks_the_class_a_partial_markup_host_and_the_chain_compiles()
    {
        const string mailer = Receipt + """
            /// <summary>Sends the receipt.</summary>
            public sealed class ReceiptMailer
            {
                public object Body() => OrderReceipt.OrderId(42);
            }
            """;

        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new FactoryNotImportedAnalyzer(), new MarkupHostCodeFixProvider(), "RASK118", mailer);

        Assert.Contains(
            "/// <summary>Sends the receipt.</summary>\n[Rask.Core.RaskMarkup]\npublic sealed partial class ReceiptMailer",
            fixhed.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.Empty(Errors(fixhed));
    }

    [Fact]
    public async Task The_fix_makes_every_enclosing_type_partial_too()
    {
        const string jobs = Receipt + """
            public static class Jobs
            {
                internal class SendReceipt
                {
                    public object Body() => OrderReceipt.OrderId(42);
                }
            }
            """;

        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new FactoryNotImportedAnalyzer(), new MarkupHostCodeFixProvider(), "RASK118", jobs);

        Assert.Contains("public static partial class Jobs", fixhed, StringComparison.Ordinal);
        Assert.Contains("[Rask.Core.RaskMarkup]", fixhed, StringComparison.Ordinal);
        Assert.Contains("internal partial class SendReceipt", fixhed, StringComparison.Ordinal);
        Assert.Empty(Errors(fixhed));
    }

    [Fact]
    public async Task The_RASK043_fix_turns_a_static_helper_into_a_host_for_the_element_chain()
    {
        const string parts = """
            namespace Shop;

            static class Parts
            {
                public static object Loading() => Div.Class("spinner");
            }
            """;

        var fixhed = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new FactoryNotImportedAnalyzer(), new MarkupHostCodeFixProvider(), "RASK043", parts);

        Assert.Contains("[Rask.Core.RaskMarkup]\nstatic partial class Parts", fixhed.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
        Assert.Empty(Errors(fixhed));
    }

    // The fixed file, with the builder surface generated over it as a build would: the errors left.
    private static List<Diagnostic> Errors(string source) =>
        GeneratorDriverFixture.WithBuilderSurface(CSharpCompilation.Create(
                "Fixed",
                [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
                GeneratorDriverFixture.BuildReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable)))
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
}
