using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rask.Generators.Tests;

/// <summary>
///     A factory call is what a user WRITES, so the documentation has to arrive there: hovering
///     <c>Video(</c> must say what <c>&lt;video&gt;</c> is and link its MDN page, without anyone
///     navigating to the component type first. These tests pin that the component's own
///     <c>&lt;summary&gt;</c> becomes the factory's and each documented property's becomes a
///     <c>&lt;param&gt;</c> — and that documenting only SOME properties stays warning-clean, which is
///     the shape every real component has.
/// </summary>
public class FactoryDocCommentEmissionTests
{
    private const string Src = """
                               using Rask.Core;
                               namespace Demo;

                               /// <summary>
                               /// Embeds a media player. <see href="https://developer.mozilla.org/en-US/docs/Web/HTML/Element/video">MDN</see>
                               /// </summary>
                               public sealed class Clip : Component
                               {
                                   /// <summary>A still shown while the video downloads.</summary>
                                   public string? Poster { get; set; }

                                   public int? Width { get; set; }

                                   public override Component? Render() => this;
                               }
                               """;

    [Fact]
    public void ComponentSummary_BecomesTheFactorySummary()
    {
        var output = GeneratorDriverFixture.Run(Src).GeneratedSource("Demo.Generated.g.cs");

        Assert.Contains("/// <summary>Embeds a media player.", output, StringComparison.Ordinal);
        // The MDN reference rides along — it is the reason the summary is worth forwarding at all.
        Assert.Contains(
            "<see href=\"https://developer.mozilla.org/en-US/docs/Web/HTML/Element/video\">MDN</see>",
            output,
            StringComparison.Ordinal);
        // The type breadcrumb survives beside the summary rather than being replaced by it.
        Assert.Contains("/// <seealso cref=\"global::Demo.Clip\"/>", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Factory for the", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UndocumentedComponent_KeepsTheBreadcrumbFallback()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;
                  public sealed class Bare : Component
                  {
                      public override Component? Render() => this;
                  }
                  """;

        var output = GeneratorDriverFixture.Run(src).GeneratedSource("Demo.Generated.g.cs");

        // No summary to forward ⇒ today's `<see cref>` breadcrumb, not an empty doc comment (which
        // would suppress the fallback tooltip the IDE shows for an undocumented member).
        Assert.Contains(
            "/// <summary>Factory for the <see cref=\"global::Demo.Bare\"/> component.</summary>",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentedProperty_BecomesAParamTag_AndUndocumentedOnesStayBare()
    {
        var output = GeneratorDriverFixture.Run(Src).GeneratedSource("Demo.Generated.g.cs");

        Assert.Contains(
            "/// <param name=\"Poster\">A still shown while the video downloads.</param>",
            output,
            StringComparison.Ordinal);
        // Width carries no summary: an empty <param> is worse than none.
        Assert.DoesNotContain("<param name=\"Width\">", output, StringComparison.Ordinal);
        Assert.DoesNotContain("<param name=\"Key\">", output, StringComparison.Ordinal);
    }

    [Fact]
    public void PartiallyDocumentedFactory_IsWarningCleanUnderADocumentationBuild()
    {
        // The trap this guards: CS1573 fires per UNDOCUMENTED parameter as soon as one param tag
        // exists, and every packable project here emits a doc file with warnings-as-errors. An
        // element factory carries ~50 universal event props, so partial documentation is the
        // permanent state — if it warned, documenting one property would break every consumer.
        var docDiagnostics = DocCommentDiagnostics(Src);

        Assert.True(
            docDiagnostics.Count == 0,
            "Generated factories must not emit XML-doc diagnostics: "
            + string.Join(", ", docDiagnostics.Select(d => $"{d.Id} {d.GetMessage()}")));
    }

    [Fact]
    public void OptingOutOfNavigation_KeepsTheDocsAndDropsOnlyTheBreadcrumb()
    {
        var output = FactoryNavigationEmissionTests.RunWith(
            Src,
            new Dictionary<string, string> { ["build_property.RaskFactoryNavigation"] = "false" });

        // RaskFactoryNavigation is about the cref link, not about documentation: turning it off must
        // not blank the tooltip of every factory in the project.
        Assert.Contains("/// <summary>Embeds a media player.", output, StringComparison.Ordinal);
        Assert.Contains("/// <param name=\"Poster\">", output, StringComparison.Ordinal);
        Assert.DoesNotContain("<seealso cref", output, StringComparison.Ordinal);
    }

    // Re-compiles the user source plus every generated source in DocumentationMode.Diagnose — the
    // mode that reports XML-doc diagnostics (CS1570 malformed, CS1572 param tag for a parameter that
    // isn't there, CS1573 parameter with no param tag). The default Parse mode reports none of them,
    // so GeneratedCompileErrors() cannot see this class of break.
    private static IReadOnlyList<Diagnostic> DocCommentDiagnostics(string source)
    {
        var parse = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Diagnose);
        var run = GeneratorDriverFixture.Run(source);
        var trees = run.RunResult.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText, parse))
            .Prepend(CSharpSyntaxTree.ParseText(source, parse))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "DocTestAssembly",
            trees,
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                // CS1591 (missing comment on a public member) is suppressed repo-wide — see
                // Directory.Build.props. Everything else about the doc comments must be clean.
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic>
                {
                    ["CS1591"] = ReportDiagnostic.Suppress,
                }));

        return compilation.GetDiagnostics()
            .Where(d => d.Id.StartsWith("CS157", StringComparison.Ordinal)
                        || d.Id is "CS1570" or "CS1580" or "CS1584")
            .ToList();
    }
}
