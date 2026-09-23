using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

public class AuthBeforeRaskAnalyzerTests
{
    // A minimal Program.cs-style top-level program. The real Rask.Server MapRask and ASP.NET Core
    // UseAuthentication symbols are referenced via BuildReferences(), so the analyzer resolves the
    // genuine method symbols (assembly / namespace checks).
    private static string Program(string body) => $$"""
                                                   using Microsoft.AspNetCore.Builder;
                                                   using Rask.Core;
                                                   using Rask.Server;

                                                   public sealed partial class App : Component
                                                   {
                                                       protected override Component? Render() => new Doctype();
                                                   }

                                                   public static class Program
                                                   {
                                                       public static void Main()
                                                       {
                                                           var builder = WebApplication.CreateBuilder();
                                                           var app = builder.Build();
                                                           {{body}}
                                                       }
                                                   }
                                                   """;

    [Fact]
    public async Task MapRask_placed_before_UseAuthentication_is_reported_as_RASK024()
    {
        var d = Assert.Single(await Diagnostics(Program(
            "app.MapRask<App>(); app.UseAuthentication();")));

        Assert.Equal("RASK024", d.Id);
        Assert.Contains("UseAuthentication", d.GetMessage());
        Assert.Contains("App", d.GetMessage());
    }

    [Fact]
    public async Task UseAuthentication_before_UseRask_reports_nothing() =>
        Assert.Empty(await Diagnostics(Program(
            "app.UseAuthentication(); app.UseAuthorization(); app.MapRask<App>();")));

    [Fact]
    public async Task An_app_without_UseAuthentication_reports_nothing() =>
        // An app that doesn't use authentication middleware is left alone.
        Assert.Empty(await Diagnostics(Program("app.MapRask<App>();")));

    [Fact]
    public async Task An_app_without_UseRask_reports_nothing() =>
        Assert.Empty(await Diagnostics(Program("app.UseAuthentication();")));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestProgram",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new AuthBeforeRaskAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK024").ToImmutableArray();
    }
}
