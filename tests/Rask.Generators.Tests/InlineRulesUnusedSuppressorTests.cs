using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Validation;

namespace Rask.Generators.Tests;

// RASKSUP002 — a private static Rules(InlineValidator<T>) is called by the generated registry, which no analyzer sees,
// so "unused private member" is suppressed on it and on nothing else. Sonar and the IDE are stood in for by an analyzer
// that reports both ids on every private method: the suppressor keys on the id and the place, not on who reported it.
public class InlineRulesUnusedSuppressorTests
{
    private const string Stubs = """
        namespace FluentValidation { public class InlineValidator<T> { } }
        """;

    [Theory]
    [InlineData("S1144")]
    [InlineData("IDE0051")]
    public async Task A_private_Rules_of_the_conventions_shape_is_not_called_unused(string id)
    {
        var source = """
            namespace Shop;
            public sealed class Supplier
            {
                static void Rules(FluentValidation.InlineValidator<Supplier> rules) { }
            }
            """;

        var (reported, survives) = await RunAsync(source, id);

        Assert.True(reported);
        Assert.False(survives);
    }

    [Theory]
    [InlineData("static void Rules(FluentValidation.InlineValidator<Other> rules) { }")]
    [InlineData("void Rules(FluentValidation.InlineValidator<Supplier> rules) { }")]
    [InlineData("static void Helper() { }")]
    public async Task Any_other_unused_private_method_still_warns(string member)
    {
        var source = $$"""
            namespace Shop;
            public sealed class Other { }
            public sealed class Supplier
            {
                {{member}}
            }
            """;

        var (reported, survives) = await RunAsync(source, "S1144");

        Assert.True(reported);
        Assert.True(survives);
    }

    private static async Task<(bool Reported, bool Survives)> RunAsync(string source, string id)
    {
        var compilation = CSharpCompilation.Create(
            "Test",
            [
                CSharpSyntaxTree.ParseText(Stubs, new CSharpParseOptions(LanguageVersion.Latest)),
                CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)),
            ],
            GeneratorDriverFixture.BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        // Two-sided, as HiddenBuilderEntrySuppressorTests: the rule alone reports it, and the suppressor's run drops it.
        var raw = await compilation
            .WithAnalyzers(ImmutableArray.Create(Rule(id)))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create(Rule(id), new InlineRulesUnusedSuppressor()))
            .GetAllDiagnosticsAsync(TestContext.Current.CancellationToken);

        return (raw.Any(d => d.Id == id), all.Any(d => d.Id == id && !d.IsSuppressed));
    }

    private static DiagnosticAnalyzer Rule(string id) => id switch
    {
        "S1144" => new SonarUnused(),
        _ => new IdeUnused(),
    };

#pragma warning disable RS1001, RS1036, RS1038, RS1041 // test doubles standing in for Sonar's and the IDE's unused-member rules
    private sealed class SonarUnused : UnusedPrivateMethods
    {
        protected override DiagnosticDescriptor Descriptor { get; } =
            new("S1144", "Unused", "Unused private member", "Test", DiagnosticSeverity.Warning, true);
    }

    private sealed class IdeUnused : UnusedPrivateMethods
    {
        protected override DiagnosticDescriptor Descriptor { get; } =
            new("IDE0051", "Unused", "Unused private member", "Test", DiagnosticSeverity.Warning, true);
    }

    private abstract class UnusedPrivateMethods : DiagnosticAnalyzer
    {
        protected abstract DiagnosticDescriptor Descriptor { get; }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(
                c =>
                {
                    if (c.Symbol is IMethodSymbol { DeclaredAccessibility: Accessibility.Private, MethodKind: MethodKind.Ordinary } method)
                    {
                        c.ReportDiagnostic(Diagnostic.Create(Descriptor, method.Locations[0]));
                    }
                },
                SymbolKind.Method);
        }
    }
#pragma warning restore RS1001, RS1036, RS1038, RS1041
}
