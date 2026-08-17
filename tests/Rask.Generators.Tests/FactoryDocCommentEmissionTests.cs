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

    // `<inheritdoc/>` does NOT arrive resolved from Roslyn: GetDocumentationCommentXml hands back the
    // literal element, because resolving it belongs to the IDE/DocFX layer. Every async twin in the
    // framework is written that way — OnValidSubmitAsync as `<inheritdoc cref="OnValidSubmit"/>` — so
    // before this each of them emitted a chain setter and a factory param with NO documentation, beside a
    // fully documented sibling. Nothing about the source looked wrong, which is what made it survive.
    [Fact]
    public void InheritDoc_ResolvesToTheMemberItPointsAt()
    {
        var src = """
                  using Rask.Core;
                  using System;
                  using System.Threading.Tasks;
                  namespace Demo;

                  public sealed class Twin : Component
                  {
                      /// <summary>Runs when the thing happens.</summary>
                      public Action? OnThing { get; set; }

                      /// <inheritdoc cref="OnThing" />
                      public Func<Task>? OnThingAsync { get; set; }

                      public override Component? Render() => this;
                  }
                  """;

        var output = GeneratorDriverFixture.Run(src).GeneratedSource("Demo.Generated.g.cs");

        Assert.Contains(
            "/// <param name=\"OnThingAsync\">Runs when the thing happens.</param>",
            output,
            StringComparison.Ordinal);
    }

    // The commoner half of the same problem: an implementing member usually carries no doc comment AT
    // ALL rather than an explicit `<inheritdoc/>`, and an IDE still shows the interface's documentation
    // for it. Input/Select/Textarea implement IFormControl<T>.Validate/OnChange/AfterBind exactly that
    // way, so without this the interface could be documented exhaustively and every control's chain
    // would still show nothing.
    [Fact]
    public void ImplementedInterfaceMember_InheritsItsDocumentation()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;

                  public interface IHasLabel
                  {
                      /// <summary>The text shown beside the control.</summary>
                      string? Label { get; set; }
                  }

                  public sealed class Field : Component, IHasLabel
                  {
                      public string? Label { get; set; }

                      public override Component? Render() => this;
                  }
                  """;

        var output = GeneratorDriverFixture.Run(src).GeneratedSource("Demo.Generated.g.cs");

        Assert.Contains(
            "/// <param name=\"Label\">The text shown beside the control.</param>",
            output,
            StringComparison.Ordinal);
    }

    // A member whose name merely COLLIDES with an interface member it does not implement must not borrow
    // that member's docs — a confidently wrong tooltip is worse than a blank one.
    //
    // The collision needs an EXPLICIT implementation to exist at all: implement IHasLabel explicitly and
    // the interface's Label is satisfied by `IHasLabel.Label`, leaving the public `Label` a separate
    // member that merely shares the name. Matching on the name alone hands it the interface's summary;
    // matching on FindImplementationForInterfaceMember does not. (Written this way after the obvious
    // version — a class that simply does not implement the interface — turned out to prove nothing: with
    // no interface in the list there is nothing to borrow either way, so it passed with the check gone.)
    [Fact]
    public void UnrelatedMemberSharingANameDoesNotBorrowDocumentation()
    {
        var src = """
                  using Rask.Core;
                  namespace Demo;

                  public interface IHasLabel
                  {
                      /// <summary>Belongs to the interface, not to the public property.</summary>
                      string? Label { get; set; }
                  }

                  public sealed class Field : Component, IHasLabel
                  {
                      private string? _explicitLabel;

                      string? IHasLabel.Label
                      {
                          get => _explicitLabel;
                          set => _explicitLabel = value;
                      }

                      public string? Label { get; set; }

                      public override Component? Render() => this;
                  }
                  """;

        var output = GeneratorDriverFixture.Run(src).GeneratedSource("Demo.Generated.g.cs");

        Assert.DoesNotContain("Belongs to the interface", output, StringComparison.Ordinal);
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
