using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Analyzers;

namespace Rask.Generators.Tests;

/// <summary>
///     RASK060 — a second <c>AddRask</c> on the same service collection discards its own options.
/// </summary>
/// <remarks>
///     The real <c>Rask.Server.AddRask</c> symbol is referenced via <c>BuildReferences()</c>, so the
///     analyzer resolves a genuine method symbol and its containing-assembly check actually runs. A
///     harness that could not bind the call would report nothing and every negative case below would pass
///     for the wrong reason — which is why <see cref="TwoCallsOnTheSameCollection_ReportsRask056"/> comes
///     first: it is the proof that this fixture binds what the analyzer looks for.
/// </remarks>
public class DuplicateAddRaskAnalyzerTests
{
    private static string Program(string body) => $$"""
                                                   using Microsoft.AspNetCore.Builder;
                                                   using Microsoft.Extensions.DependencyInjection;
                                                   using Rask.Core;
                                                   using Rask.Server;

                                                   public static class Program
                                                   {
                                                       public static void Main()
                                                       {
                                                           var builder = WebApplication.CreateBuilder();
                                                           {{body}}
                                                       }
                                                   }
                                                   """;

    [Fact]
    public async Task TwoCallsOnTheSameCollection_ReportsRask056()
    {
        // The shape that ships an app with no languages: the second call's configureCulture runs, builds
        // its options, and then loses to the TryAddSingleton the first call already made.
        var d = Assert.Single(await Diagnostics(Program("""
            builder.Services.AddRask();
            builder.Services.AddRask(configureCulture: c => c.SupportedCultures.Add("hu"));
            """)));

        Assert.Equal("RASK060", d.Id);
        Assert.Contains("builder.Services", d.GetMessage(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThreeCalls_ReportsEveryCallAfterTheFirst() =>
        // Each surplus call is a separate edit to make, so each gets its own report.
        Assert.Equal(2, (await Diagnostics(Program("""
            builder.Services.AddRask();
            builder.Services.AddRask();
            builder.Services.AddRask();
            """))).Length);

    [Fact]
    public async Task OneCall_NoDiagnostic() =>
        Assert.Empty(await Diagnostics(Program(
            "builder.Services.AddRask(configureCulture: c => c.SupportedCultures.Add(\"en\"));")));

    [Fact]
    public async Task TwoDifferentCollections_NoDiagnostic() =>
        // Two collections configured side by side are two apps' worth of registrations, not a double
        // registration. Without this the rule would fire across a test file that builds one collection per
        // case, which is how an analyzer earns being turned off.
        Assert.Empty(await Diagnostics(Program("""
            var first = new ServiceCollection();
            var second = new ServiceCollection();
            first.AddRask();
            second.AddRask();
            """)));

    [Fact]
    public async Task CallsInSeparateMethods_NoDiagnostic() =>
        Assert.Empty(await Diagnostics("""
            using Microsoft.Extensions.DependencyInjection;
            using Rask.Core;
            using Rask.Server;

            public static class Program
            {
                public static void Main() { }

                private static void One() => new ServiceCollection().AddRask();

                private static void Two()
                {
                    var services = new ServiceCollection();
                    services.AddRask();
                }
            }
            """));

    [Fact]
    public async Task AnUnrelatedAddRask_NoDiagnostic() =>
        // Name-matching alone is not enough: the rule is about Rask's host registration, so a method that
        // merely shares the name must not trip it.
        Assert.Empty(await Diagnostics("""
            public static class Other
            {
                public static void AddRask(this object o) { }
            }

            public static class Program
            {
                public static void Main()
                {
                    var thing = new object();
                    thing.AddRask();
                    thing.AddRask();
                }
            }
            """));

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(string source)
    {
        var compilation = CSharpCompilation.Create(
            "TestProgram",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                nullableContextOptions: NullableContextOptions.Enable));

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new DuplicateAddRaskAnalyzer());
        var all = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return all.Where(d => d.Id == "RASK060").ToImmutableArray();
    }
}
