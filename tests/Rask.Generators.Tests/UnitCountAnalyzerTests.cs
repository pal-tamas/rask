using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;
using Rask.Generators.CodeFixes;

namespace Rask.Generators.Tests;

public class UnitCountAnalyzerTests
{
    private static string Code(string expression) => $$"""
                                                       using System;
                                                       using Rask;
                                                       namespace Demo;
                                                       public static class Timing
                                                       {
                                                           public static object Value => {{expression}};
                                                       }
                                                       """;

    [Theory]
    [InlineData("2.Hour", "2.Hours")]
    [InlineData("0.Minute", "0.Minutes")]
    [InlineData("10.Megabyte", "10.Megabytes")]
    [InlineData("1.Hours", "1.Hour")]
    [InlineData("1.Seconds", "1.Second")]
    [InlineData("1.Gigabytes", "1.Gigabyte")]
    public async Task A_unit_that_reads_wrong_for_its_count_says_what_to_write(string written, string meant)
    {
        var d = Assert.Single(await Diagnostics(Code(written)));

        Assert.Equal($"'{written}' reads wrong — write '{meant}'", d.GetMessage());
    }

    [Theory]
    [InlineData("1.Hour")]
    [InlineData("2.Hours")]
    [InlineData("1.Megabyte")]
    [InlineData("50.Megabytes")]
    [InlineData("1.5.Hours")]        // a fraction has no singular to match
    [InlineData("-1.Hour")]          // the literal is 1; the minus applies to the TimeSpan
    public async Task A_unit_that_reads_right_is_left_alone(string written) =>
        Assert.Empty(await Diagnostics(Code(written)));

    [Fact]
    public async Task A_count_that_is_not_a_literal_is_left_alone() =>
        Assert.Empty(await Diagnostics(Code("Count.Hour; static int Count => 2")));

    [Fact]
    public async Task An_apps_own_Hours_on_an_int_is_not_ours_to_check()
    {
        var source = """
                     namespace Demo;
                     public static class Mine { extension(int n) { public int Hours => n; } }
                     public static class Use { public static int X => 1.Hours; }
                     """;

        Assert.Empty(await Diagnostics(source));
    }

    [Theory]
    [InlineData("2.Hour", "2.Hours")]
    [InlineData("1.Megabytes", "1.Megabyte")]
    public async Task The_fix_writes_the_form_that_reads(string written, string meant)
    {
        var fixedSource = await CodeFixHarness.ApplyAnalyzerFixAsync(
            new UnitCountAnalyzer(), new UnitCountCodeFixProvider(), "RASK092", Code(written));

        Assert.Contains($"=> {meant};", fixedSource);
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new UnitCountAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK092").ToImmutableArray();
    }
}
