using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Rask.Generators.Analyzers;

// RASK092 — a unit literal that reads wrong for its count: `2.Hour`, `1.Hours`. Rask.Core.Units gives
// every whole-number unit a singular and a plural with the same value, so both compile and both are
// right; only one of them is English. The check looks at a LITERAL count only — `count.Hours` has no
// count to read — and only at the int units, the ones that have a singular to swap to.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnitCountAnalyzer : DiagnosticAnalyzer
{
    public const string ReplacementKey = "Replacement";

    private const string UnitsFullName = "Rask.Core.Units";

    private static readonly DiagnosticDescriptor Rask092 = new(
        "RASK092",
        "Unit reads wrong for its count",
        "'{0}' reads wrong — write '{1}'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "A count of one takes the singular (1.Hour) and any other count the plural (2.Hours). Both "
        + "spellings are the same value; the one that matches its count is the one that reads.",
        DiagnosticHelp.Link("RASK092"));

    // Plural → singular for every unit that has both. Kept in step with Rask.Core.Units.
    private static readonly ImmutableDictionary<string, string> Singulars =
        ImmutableDictionary.CreateRange(StringComparer.Ordinal, new[]
        {
            Pair("Milliseconds", "Millisecond"), Pair("Seconds", "Second"), Pair("Minutes", "Minute"),
            Pair("Hours", "Hour"), Pair("Days", "Day"), Pair("Weeks", "Week"), Pair("Bytes", "Byte"),
            Pair("Kilobytes", "Kilobyte"), Pair("Megabytes", "Megabyte"), Pair("Gigabytes", "Gigabyte"),
        });

    private static readonly ImmutableDictionary<string, string> Plurals =
        ImmutableDictionary.CreateRange(StringComparer.Ordinal, new[]
        {
            Pair("Millisecond", "Milliseconds"), Pair("Second", "Seconds"), Pair("Minute", "Minutes"),
            Pair("Hour", "Hours"), Pair("Day", "Days"), Pair("Week", "Weeks"), Pair("Byte", "Bytes"),
            Pair("Kilobyte", "Kilobytes"), Pair("Megabyte", "Megabytes"), Pair("Gigabyte", "Gigabytes"),
        });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rask092);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(UnitsFullName) is not { } units)
            {
                return;
            }

            start.RegisterSyntaxNodeAction(ctx => Analyze(ctx, units), SyntaxKind.SimpleMemberAccessExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, INamedTypeSymbol units)
    {
        var access = (MemberAccessExpressionSyntax)context.Node;
        if (access.Expression is not LiteralExpressionSyntax { Token.Value: int count })
        {
            return;
        }

        var name = access.Name.Identifier.ValueText;
        var replacement = count == 1
            ? Singulars.TryGetValue(name, out var singular) ? singular : null
            : Plurals.TryGetValue(name, out var plural) ? plural : null;
        if (replacement is null || !DeclaredOnUnits(context, access, units))
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty.Add(ReplacementKey, replacement);
        context.ReportDiagnostic(Diagnostic.Create(
            Rask092,
            access.Name.GetLocation(),
            properties,
            access.ToString(),
            $"{access.Expression}.{replacement}"));
    }

    // The name alone is not enough — an app is free to have its own `Hours` on an int.
    private static bool DeclaredOnUnits(
        SyntaxNodeAnalysisContext context, MemberAccessExpressionSyntax access, INamedTypeSymbol units)
    {
        for (var type = context.SemanticModel.GetSymbolInfo(access, context.CancellationToken).Symbol?.ContainingType;
             type is not null;
             type = type.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(type, units))
            {
                return true;
            }
        }

        return false;
    }

    private static System.Collections.Generic.KeyValuePair<string, string> Pair(string key, string value) =>
        new(key, value);
}
