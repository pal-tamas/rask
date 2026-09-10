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
///     RASK075 — an option template on a select that was also told to use the platform's own control.
///     An <c>&lt;option&gt;</c> holds text, so the template is never called: the build is green, the
///     control works, and the markup somebody wrote is simply absent.
/// </summary>
/// <remarks>
///     <para>
///         Every case goes through a REAL chain rather than a constructed component, because the chain is
///         where this kind of analyzer dies quietly — a form control's entry is its mode-opening seed and
///         not a <c>Build&lt;T&gt;</c>, so the obvious way of reading a chain matches nothing here and
///         the diagnostic never fires.
///     </para>
///     <para>
///         The stand-ins are form controls for exactly that reason, and not merely to look like the
///         originals: a generic component that is NOT one gets no entry at all, so a plainer stand-in
///         would not compile — and a test whose source does not compile reports no diagnostics and passes
///         every "is not reported" case for the wrong reason. That is what <c>AnalyzeAsync</c> throws on.
///     </para>
/// </remarks>
public class OptionTemplateNativeAnalyzerTests
{
    private const string Components = """
        namespace Rask.Ui
        {
            public sealed partial class UiSelect<T> : Rask.Core.Component, Rask.Core.Forms.IFormControl<T>
            {
                public required string Label { get; set; }
                public bool? Native { get; set; }
                public string? Class { get; set; }
                public System.Func<T, Rask.Core.Component>? OptionTemplate { get; set; }
                public T? Value { get; set; }
                public System.Action<T>? OnChange { get; set; }
                public System.Func<T, System.Threading.Tasks.Task>? OnChangeAsync { get; set; }
                public System.Linq.Expressions.Expression<System.Func<T>>? Bind { get; set; }
                public Rask.Core.Forms.Validate<T>? Validate { get; set; }
                public Rask.Core.Forms.ValidateAsync<T>? ValidateAsync { get; set; }
                public System.Action<T>? AfterBind { get; set; }
                public System.Func<T, System.Threading.Tasks.Task>? AfterBindAsync { get; set; }
            }

            public sealed partial class UiMultiSelect<T> : Rask.Core.Component, Rask.Core.Forms.IFormControl<T>
            {
                public required string Label { get; set; }
                public bool? Native { get; set; }
                public System.Func<T, Rask.Core.Component>? OptionTemplate { get; set; }
                public System.Func<T, Rask.Core.Component>? ChipTemplate { get; set; }
                public T? Value { get; set; }
                public System.Action<T>? OnChange { get; set; }
                public System.Func<T, System.Threading.Tasks.Task>? OnChangeAsync { get; set; }
                public System.Linq.Expressions.Expression<System.Func<T>>? Bind { get; set; }
                public Rask.Core.Forms.Validate<T>? Validate { get; set; }
                public Rask.Core.Forms.ValidateAsync<T>? ValidateAsync { get; set; }
                public System.Action<T>? AfterBind { get; set; }
                public System.Func<T, System.Threading.Tasks.Task>? AfterBindAsync { get; set; }
            }
        }

        namespace Demo
        {
            public sealed partial class Card : Rask.Core.Component
            {
                public bool? Native { get; set; }
                public System.Func<string, Rask.Core.Component>? OptionTemplate { get; set; }
            }
        }
        """;

    [Fact]
    public async Task A_template_beside_an_explicit_native_is_reported()
    {
        var d = Assert.Single(await AnalyzeAsync(
            """UiSelect.Value("a").Label("L").OptionTemplate(v => null!).Native(true)"""));

        Assert.Equal("RASK075", d.Id);
        Assert.Contains("OptionTemplate", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_order_the_two_were_written_in_does_not_matter() =>
        Assert.Single(await AnalyzeAsync(
            """UiSelect.Value("a").Label("L").Native(true).OptionTemplate(v => null!)"""));

    [Fact]
    public async Task It_reads_a_bound_chain_as_well_as_a_controlled_one() =>
        Assert.Single(await AnalyzeAsync(
            """UiSelect.Bind(() => Field).Label("L").OptionTemplate(v => null!).Native(true)"""));

    [Fact]
    public async Task A_chip_template_counts_too()
    {
        // Same failure for the same reason: the platform's control draws its own selection, so a chip
        // template has nowhere to render either.
        var d = Assert.Single(await AnalyzeAsync(
            """UiMultiSelect.Value("a").Label("L").ChipTemplate(v => null!).Native(true)"""));

        Assert.Contains("ChipTemplate", d.GetMessage(), StringComparison.Ordinal);
    }

    // The common case, and the one that must stay silent: a template with Native unset already chooses
    // the drawn list, so there is nothing to report and nothing to fix.
    [Fact]
    public async Task A_template_on_its_own_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync(
            """UiSelect.Value("a").Label("L").OptionTemplate(v => null!)"""));

    [Fact]
    public async Task A_template_beside_an_explicit_native_false_is_agreement_not_a_contradiction() =>
        Assert.Empty(await AnalyzeAsync(
            """UiSelect.Value("a").Label("L").OptionTemplate(v => null!).Native(false)"""));

    [Fact]
    public async Task A_native_select_with_no_template_is_not_reported() =>
        Assert.Empty(await AnalyzeAsync(
            """UiSelect.Value("a").Label("L").Native(true).Class("x")"""));

    // Not knowable at compile time, so reporting it would be a guess — and a guess that fires on a
    // control whose mode is a setting is a warning nobody can silence.
    [Fact]
    public async Task A_native_flag_that_is_not_a_literal_is_left_alone() =>
        Assert.Empty(await AnalyzeAsync(
            """UiSelect.Value("a").Label("L").OptionTemplate(v => null!).Native(Flag)"""));

    // The analyzer keys on the kit's own types. Somebody else's component with a property spelled the
    // same way is none of its business, and matching on the name alone would make it so.
    [Fact]
    public async Task Another_components_identically_named_properties_are_not_reported() =>
        Assert.Empty(await AnalyzeAsync(
            "Card.OptionTemplate(v => null!).Native(true)"));

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string chain)
    {
        var page = $$"""
            namespace Demo
            {
                public sealed partial class Page : Rask.Core.Component
                {
                    private static bool Flag => true;

                    private static string Field { get; set; } = "a";

                    protected override Rask.Core.Component? Render() => {{chain}};
                }
            }
            """;

        var source = Components + "\n" + page;
        var run = BuilderGeneratorHarness.Run(source);
        var trees = run.Sources
            .Select(s => s.SourceText.ToString())
            .Prepend(source)
            .Select(s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        // A chain that does not compile produces no diagnostics, which would pass every "is not
        // reported" case above for entirely the wrong reason. This is what makes those cases mean
        // something — and it is not hypothetical: the first draft of these stand-ins was not a form
        // control, got no chain entry at all, and every negative test passed green.
        var errors = compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
        {
            throw new Xunit.Sdk.XunitException(
                "The analyzed source does not compile:\n" + string.Join("\n", errors.Select(e => e.ToString())));
        }

        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new OptionTemplateNativeAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();

        return [.. all.Where(d => d.Id == "RASK075")];
    }
}
