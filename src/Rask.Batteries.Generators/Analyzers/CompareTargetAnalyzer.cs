using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators.Analyzers;

/// <summary>
///     RASK144 — <c>[Compare("X")]</c> names a property the model it is written on does not have.
/// </summary>
/// <remarks>
///     <c>CompareAttribute</c> looks the other property up by name when it validates, and a name it does not find is a
///     refusal ("Could not find a property named X") on every submit — a form nobody can send. Properties only, as the
///     attribute reads them: its own type and its bases, matched by exact name. A suggestion by default; a warning
///     under <c>&lt;RaskStrict&gt;</c>.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CompareTargetAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rask144 = new(
        "RASK144",
        "[Compare] names a property the model does not have",
        "[Compare(\"{0}\")] on '{1}.{2}' names no property of '{1}', so every submit is refused — name one with nameof(…)",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "CompareAttribute finds the property it compares with by name when it validates. A name the "
                     + "model has no property for refuses every value. A suggestion by default; a warning under "
                     + "<RaskStrict>.",
        helpLinkUri: DiagnosticHelp.Link("RASK144"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Rask144);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName("System.ComponentModel.DataAnnotations.CompareAttribute") is not { } compare)
            {
                return;
            }

            start.RegisterSymbolAction(context => Analyze(context, compare), SymbolKind.Property);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, INamedTypeSymbol compare)
    {
        var property = (IPropertySymbol)context.Symbol;
        foreach (var attribute in property.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, compare) ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not string other ||
                HasProperty(property.ContainingType, other))
            {
                continue;
            }

            var at = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? property.Locations.FirstOrDefault();
            context.ReportDiagnostic(Diagnostic.Create(Rask144, at, other, property.ContainingType.Name, property.Name));
        }
    }

    private static bool HasProperty(INamedTypeSymbol type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name).OfType<IPropertySymbol>().Any())
            {
                return true;
            }
        }

        return false;
    }
}
