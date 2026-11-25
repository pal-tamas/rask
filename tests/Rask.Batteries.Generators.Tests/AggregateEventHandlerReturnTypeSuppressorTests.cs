using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Batteries.Generators.Analyzers;

namespace Rask.Batteries.Generators.Tests;

// RASKSUP003 — an aggregate's static Task On(E e) answers Task because RASK179 says so, so CA1859's "answer the concrete
// Task<int> its body returns" is suppressed on it and on nothing else. CA1859 is stood in for by an analyzer reporting it
// where the real one does (the method's name) on every private method that answers Task: the suppressor keys on the id
// and the place, not on who reported it.
public class AggregateEventHandlerReturnTypeSuppressorTests
{
    private const string Fuelings = """
        using System.Threading.Tasks;
        using Rask.Cqrs;
        using Rask.Data;
        namespace Fuel;

        public sealed record DriverChipsChanged(string Code) : IEvent;

        public sealed partial class Fueling : Aggregate<int>
        {
            MEMBER
        }

        public static class Elsewhere
        {
            private static Task On(DriverChipsChanged e) => Task.FromResult(1);
        }
        """;

    [Fact]
    public async Task An_aggregates_event_handler_answering_a_write_is_not_asked_for_a_concrete_return_type()
    {
        var source = Fuelings.Replace("MEMBER", "private static Task On(DriverChipsChanged e) => Task.FromResult(1);", StringComparison.Ordinal);

        var (reported, surviving) = await RunAsync(source);

        Assert.Equal(2, reported);
        Assert.Equal(["Elsewhere"], surviving);
    }

    [Theory]
    [InlineData("private static Task Recount(DriverChipsChanged e) => Task.FromResult(1);")]
    [InlineData("private static Task On(string code) => Task.FromResult(1);")]
    [InlineData("private Task On(DriverChipsChanged e) => Task.FromResult(1);")]
    public async Task Any_other_private_method_answering_Task_still_hears_CA1859(string member)
    {
        var source = Fuelings.Replace("MEMBER", member, StringComparison.Ordinal);

        var (reported, surviving) = await RunAsync(source);

        Assert.Equal(2, reported);
        Assert.Equal(["Elsewhere", "Fueling"], surviving);
    }

    private static async Task<(int Reported, IReadOnlyList<string> Surviving)> RunAsync(string source)
    {
        var compilation = ModelStateAnalyzerHarness.Compile(source);

        // Two-sided, as InlineRulesUnusedSuppressorTests: the rule alone reports it, and the suppressor's run drops it.
        var raw = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ConcreteReturnType()))
            .GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
        var all = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new ConcreteReturnType(), new AggregateEventHandlerReturnTypeSuppressor()))
            .GetAllDiagnosticsAsync(TestContext.Current.CancellationToken);

        var surviving = all
            .Where(static d => d.Id == "CA1859" && !d.IsSuppressed)
            .Select(static d => d.GetMessage(System.Globalization.CultureInfo.InvariantCulture))
            .Order(StringComparer.Ordinal)
            .ToList();
        return (raw.Count(static d => d.Id == "CA1859"), surviving);
    }

#pragma warning disable RS1001, RS1029, RS1036, RS1038, RS1041, RS2008 // a test double standing in for the .NET analyzers' CA1859, under its id
    private sealed class ConcreteReturnType : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor Descriptor =
            new("CA1859", "Use concrete types", "{0}", "Test", DiagnosticSeverity.Warning, true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Descriptor];

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolAction(
                static c =>
                {
                    if (c.Symbol is IMethodSymbol { DeclaredAccessibility: Accessibility.Private, ReturnType.Name: "Task" } method)
                    {
                        c.ReportDiagnostic(Diagnostic.Create(Descriptor, method.Locations[0], method.ContainingType.Name));
                    }
                },
                SymbolKind.Method);
        }
    }
#pragma warning restore RS1001, RS1029, RS1036, RS1038, RS1041, RS2008
}
