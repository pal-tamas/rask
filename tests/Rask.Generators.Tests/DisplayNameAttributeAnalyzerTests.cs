using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK131: a property named by <c>[DisplayName]</c>, which Rask never reads, and its fix to <c>[Display(Name = …)]</c>.
/// </summary>
public class DisplayNameAttributeAnalyzerTests
{
    [Fact]
    public async Task A_DisplayName_on_a_property_is_a_RASK131_warning()
    {
        const string source = """
                              using System.ComponentModel;

                              public sealed class Signup
                              {
                                  [DisplayName("Given name")]
                                  public string? GivenName { get; set; }
                              }
                              """;

        var diagnostics = await GetDiagnosticsAsync(source);

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
        Assert.Contains("GivenName", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(" — ", d.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("write [Display(Name = …)]", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_subclass_of_DisplayName_is_reported_too()
    {
        const string source = """
                              public sealed class LocalizedNameAttribute(string key) : System.ComponentModel.DisplayNameAttribute(key);

                              public sealed class Signup
                              {
                                  [LocalizedName("given")]
                                  public string? GivenName { get; set; }
                              }
                              """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task A_name_given_by_Display_is_left_alone()
    {
        const string source = """
                              using System.ComponentModel.DataAnnotations;

                              public sealed class Signup
                              {
                                  [Display(Name = "Given name")]
                                  public string? GivenName { get; set; }
                              }
                              """;

        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task The_RASK131_fix_writes_the_name_into_Display_and_adds_its_using()
    {
        const string source = """
                              using System.ComponentModel;

                              public sealed class Signup
                              {
                                  [DisplayName("Given name")]
                                  public string? GivenName { get; set; }
                              }
                              """;

        var fixedSource = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new DisplayNameAttributeAnalyzer(), new DisplayNameAttributeCodeFixProvider(), "RASK131", source);

        Assert.Contains("[Display(Name = \"Given name\")]", fixedSource, StringComparison.Ordinal);
        Assert.Contains("using System.ComponentModel.DataAnnotations;", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.ComponentModel;", fixedSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayName", fixedSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_RASK131_fix_keeps_System_ComponentModel_while_something_else_uses_it()
    {
        const string source = """
                              using System.ComponentModel;
                              using System.ComponentModel.DataAnnotations;

                              public sealed class Signup
                              {
                                  [DisplayName("Given name")]
                                  [Required]
                                  public string? GivenName { get; set; }

                                  [Description("Shown under the field")]
                                  public string? Nickname { get; set; }
                              }
                              """;

        var fixedSource = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new DisplayNameAttributeAnalyzer(), new DisplayNameAttributeCodeFixProvider(), "RASK131", source);

        Assert.Contains("[Display(Name = \"Given name\")]", fixedSource, StringComparison.Ordinal);
        Assert.Contains("using System.ComponentModel;", fixedSource, StringComparison.Ordinal);
        Assert.Equal(2, fixedSource.Split("using System.ComponentModel.DataAnnotations;").Length);
    }

    [Fact]
    public async Task The_RASK131_fix_is_withheld_for_a_subclass_of_DisplayName()
    {
        const string source = """
                              public sealed class LocalizedNameAttribute(string key) : System.ComponentModel.DisplayNameAttribute(key);

                              public sealed class Signup
                              {
                                  [LocalizedName("given")]
                                  public string? GivenName { get; set; }
                              }
                              """;

        var offered = await CodeFixHarness.IsAnalyzerFixOfferedAsync(
            new DisplayNameAttributeAnalyzer(), new DisplayNameAttributeCodeFixProvider(), "RASK131", source);

        Assert.False(offered);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.DoesNotContain(compilation.GetDiagnostics(), static d => d.Severity == DiagnosticSeverity.Error);

        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new DisplayNameAttributeAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        return [.. all.Where(static d => d.Id == "RASK131")];
    }
}
