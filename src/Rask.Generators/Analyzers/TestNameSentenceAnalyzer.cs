using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK116 — a test whose name is not a sentence. A test's name is what the runner prints and the first thing the
// reader of a failure sees, so it says what the test proves: `Remember_loads_once_then_serves_from_the_cache`, not
// `GetOrCreate_RunsFactoryOnce`: three words or more, most of them lower-case prose, and none of the scaffolding words a
// template leaves behind. Rask's own suites are held to it too (Directory.Build.targets, tests/Rask.TestNames.Analyzer).
//
// Off by default — it is a convention, not a bug. An app's <RaskStrict> switches it on in its test projects
// (build/rask-strict-tests.globalconfig); `dotnet_diagnostic.RASK116.severity = warning` does it by hand.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestNameSentenceAnalyzer : DiagnosticAnalyzer
{
    // The attribute a test method carries in each framework; a derived one (xunit's Theory, a custom Fact) counts.
    private static readonly ImmutableArray<string> TestAttributes = ImmutableArray.Create(
        "Xunit.FactAttribute",
        "NUnit.Framework.TestAttribute",
        "NUnit.Framework.TestCaseAttribute",
        "NUnit.Framework.TestCaseSourceAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute");

    private static readonly char[] WordBreak = { '_' };

    private static readonly ImmutableHashSet<string> Scaffolding = ImmutableHashSet.Create(
        StringComparer.Ordinal, "Should", "Given", "When", "Then", "Test", "Returns", "Throws");

    private static readonly DiagnosticDescriptor Rask116 = new(
        "RASK116",
        "Test is not named as a sentence",
        "'{0}' does not read as a sentence: {1} — name it after what it proves, like "
        + "Remember_loads_once_then_serves_from_the_cache",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        false,
        "A test's name is what the runner prints when it fails, so it says what the test proves, in words joined "
        + "by underscores: at least three, mostly lower-case prose, without the scaffolding words Should, Given, "
        + "When, Then, Test, Returns or Throws. Names the test is about keep their casing and stay the minority.",
        DiagnosticHelp.Link("RASK116"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask116);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            var attributes = TestAttributes
                .Select(start.Compilation.GetTypeByMetadataName)
                .OfType<INamedTypeSymbol>()
                .ToImmutableArray();
            if (attributes.IsEmpty)
            {
                return;
            }

            start.RegisterSymbolAction(ctx => Analyze(ctx, attributes), SymbolKind.Method);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, ImmutableArray<INamedTypeSymbol> attributes)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (!method.GetAttributes().Any(a => IsTestAttribute(a.AttributeClass, attributes))
            || WhyNotASentence(method.Name) is not { } why)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rask116, method.Locations[0], method.Name, why));
    }

    private static bool IsTestAttribute(INamedTypeSymbol? attribute, ImmutableArray<INamedTypeSymbol> attributes)
    {
        for (var type = attribute; type is not null; type = type.BaseType)
        {
            if (attributes.Contains(type, SymbolEqualityComparer.Default))
            {
                return true;
            }
        }

        return false;
    }

    internal static string? WhyNotASentence(string name)
    {
        var words = name.Split(WordBreak, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3)
        {
            return "fewer than three words";
        }

        if (words.Count(IsProse) * 2 <= words.Length)
        {
            return "mostly names, not prose";
        }

        return words.FirstOrDefault(Scaffolding.Contains) is { } scaffold
            ? $"the scaffolding word \"{scaffold}\""
            : null;
    }

    private static bool IsProse(string word) => word.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9'));
}
