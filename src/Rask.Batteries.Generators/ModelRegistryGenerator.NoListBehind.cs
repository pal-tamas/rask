using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// RASK111: a collection of value objects with no list behind it — `IReadOnlyList<Stop> Stops { get; private set; }`.
// EF Core writes a JSON collection of value objects through an IList<T>, and this property has none (its backing field
// is the same read-only view), so it is mapped the old way, as an owned collection, whose column is NULL whatever
// the model says.
public sealed partial class ModelRegistryGenerator
{
    private static readonly DiagnosticDescriptor Rask111 = new(
        "RASK111",
        "A collection of value objects with no list behind it is a NULL column",
        "'{0}' on '{1}' has no list behind it, so its JSON column is NULL whatever its nullability; hold it in a "
        + "List<{2}> field behind the read-only view, or declare it List<{2}>",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A collection of value objects is one JSON column, NOT NULL where the property is non-nullable. "
                     + "EF Core reads and writes that column through an IList<T>: the List<T> field behind a "
                     + "read-only view, or a List<T> property. A get-only IReadOnlyList<T> auto-property has none, "
                     + "so it keeps the owned mapping, whose column is NULL.",
        helpLinkUri: DiagnosticHelp.Link("RASK111"));

    // The list EF Core writes through: the field behind the view, or the property itself — as Rask.Data's
    // JsonCollection.ListBehind decides at runtime.
    private static bool HasListBehind(ModelValueCollection collection)
    {
        var declared = collection.Field?.Type ?? collection.Property.Type;

        return new[] { declared }.Concat(declared.AllInterfaces).OfType<INamedTypeSymbol>().Any(t =>
            t.IsGenericType &&
            t.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IList_T &&
            SymbolEqualityComparer.Default.Equals(t.TypeArguments[0], collection.Element));
    }

    private static void ReportJsonCollectionsWithNoList(SourceProductionContext context, List<Candidate> entities)
    {
        foreach (var entity in entities)
        {
            foreach (var collection in entity.Collections.Where(static c => c.NoListAt is not null))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask111,
                    collection.NoListAt!.ToLocation(),
                    collection.Property,
                    entity.SimpleName,
                    LastSegment(collection.ElementTypeName!)));
            }
        }
    }
}
