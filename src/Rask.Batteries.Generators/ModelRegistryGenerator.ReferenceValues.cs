using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// RASK105: a value object that compares by reference. EF Core compares it by value either way, so the save is
// right — but the app's own code would find two equal amounts different. A record (or record struct) compares by
// value as written; a plain class says how by deriving from Rask.Data.ValueObject.
public sealed partial class ModelRegistryGenerator
{
    private static readonly DiagnosticDescriptor Rask105 = new(
        "RASK105",
        "A value object compares by reference",
        "'{0}' is held as a value object on '{1}' but compares by reference; make it a record (or a record struct), "
        + "or derive it from ValueObject and list its EqualityComponents",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "What an aggregate holds that is not an entity is a value object: it is its values, so two with "
                     + "the same values are the same. A record or a record struct already compares that way; a "
                     + "plain class compares by reference unless it derives from Rask.Data.ValueObject, which "
                     + "compares the values its EqualityComponents name.",
        helpLinkUri: DiagnosticHelp.Link("RASK105"));

    private const string ValueObjectBase = "ValueObject";

    // A record compares by value as written; a class does when it derives from Rask.Data.ValueObject.
    private static bool ComparesByValue(INamedTypeSymbol valueObject)
    {
        if (valueObject.IsRecord)
        {
            return true;
        }

        for (var current = valueObject.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: ValueObjectBase, TypeArguments.Length: 0 } &&
                string.Equals(current.ContainingNamespace?.ToDisplayString(), "Rask.Data", System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Once per value object, however many entities and paths reach it. A strongly-typed id another entity is keyed
    // by is a converted column, not a value object, so it is left alone.
    private static void ReportReferenceValues(SourceProductionContext context, List<Candidate> entities, HashSet<string> idTypes)
    {
        var reported = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            foreach (var value in entity.ReferenceValues.Where(v => !idTypes.Contains(v.TypeName) && reported.Add(v.TypeName)))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask105, (value.Location ?? entity.Location)?.ToLocation(), value.Name, entity.SimpleName));
            }
        }
    }

    /// <summary>A value object that compares by reference: its type, its name, and the property that holds it.</summary>
    private sealed record ReferenceValue(string TypeName, string Name, SymbolLocation? Location);
}
