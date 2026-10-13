using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// RASK103: a value object EF Core can only build through a constructor that takes another value object of
// several values. That one is a complex type, and a constructor parameter binds to a column, never to a complex
// type — so the model is refused when the app starts. Said at the build instead.
public sealed partial class ModelRegistryGenerator
{
    private static readonly DiagnosticDescriptor Rask103 = new(
        "RASK103",
        "A constructor-built value object holds a value object of several values",
        "'{0}' is built through its constructor and holds '{1}', a value object of several values; EF Core "
        + "cannot bind it. Give '{0}' a private parameterless constructor and private setters.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A value object of several values is stored as an EF Core complex type, and EF Core binds a "
                     + "constructor parameter to a column only — never to a complex type. A value object whose "
                     + "every constructor takes one therefore cannot be built when a row is read, and EF Core "
                     + "refuses the whole model when the app starts. A value object that holds ONE value is a "
                     + "column and binds; so does any value object EF Core can build without that parameter.",
        helpLinkUri: DiagnosticHelp.Link("RASK103"));

    /// <summary>
    ///     The first constructor parameter that makes <paramref name="holder" /> impossible for EF Core to
    ///     build, or null when it has a constructor EF Core can use.
    /// </summary>
    /// <remarks>
    ///     A record's copy constructor is not one EF Core builds with. A struct's implicit parameterless
    ///     constructor counts only when the struct declares no other: once it declares one, EF Core binds that.
    /// </remarks>
    private static UnbuildableHolder? UnbuildableHolderOf(INamedTypeSymbol holder)
    {
        var constructors = holder.InstanceConstructors
            .Where(c => !IsCopyConstructor(c, holder))
            .ToList();

        if (holder.IsValueType && constructors.Exists(static c => !c.IsImplicitlyDeclared))
        {
            constructors.RemoveAll(static c => c.IsImplicitlyDeclared && c.Parameters.Length == 0);
        }

        IParameterSymbol? first = null;
        foreach (var constructor in constructors)
        {
            if (constructor.Parameters.FirstOrDefault(static p => IsSeveralValues(p.Type)) is not { } unbound)
            {
                return null;
            }

            first ??= unbound;
        }

        return first is null
            ? null
            : new UnbuildableHolder(
                holder.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                holder.Name,
                AggregateShape.ValueObjectTypeOf(first.Type)!.Name,
                SymbolLocation.From(first));
    }

    private static bool IsSeveralValues(ITypeSymbol type) =>
        AggregateShape.ValueObjectTypeOf(type) is { } valueObject && !AggregateShape.HoldsOneValue(valueObject);

    private static bool IsCopyConstructor(IMethodSymbol constructor, INamedTypeSymbol holder) =>
        constructor.Parameters.Length == 1 &&
        SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, holder);

    // Once per value object, however many entities and paths reach it; at the parameter, or — for a type from
    // another assembly — at the entity that holds it.
    private static void ReportUnbuildableHolders(SourceProductionContext context, List<Candidate> entities)
    {
        var reported = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            foreach (var holder in entity.UnbuildableHolders.Where(h => reported.Add(h.TypeName)))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask103, (holder.Location ?? entity.Location)?.ToLocation(), holder.Name, holder.Held));
            }
        }
    }

    /// <summary>A value object EF Core cannot build: its name, the several-values one it takes, and where.</summary>
    private sealed record UnbuildableHolder(string TypeName, string Name, string Held, SymbolLocation? Location);
}
