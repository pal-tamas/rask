using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
///     The read navigations an entity's configured relationships give it: the target is the <c>HasOne&lt;T&gt;()</c>
///     the author wrote, never one guessed from the id's name.
/// </summary>
/// <remarks>
///     The name: <c>X</c> for an id <c>XId</c> when <c>X</c> ends in the target's last word —
///     <c>ForeignTaxProductCategoryId</c> to a <c>ForeignTaxCategory</c> is <c>ForeignTaxProductCategory</c> — and the
///     target's own name otherwise — <c>TransactionTypeId</c> to a <c>Provider</c> is <c>Provider</c>. When that name is
///     taken, or two relationships want it, the other one; when both are, RASK112.
/// </remarks>
internal sealed class ConfiguredReferences
{
    private static readonly DiagnosticDescriptor Rask112 = new(
        "RASK112",
        "A configured relationship's read navigation has no free name",
        "The relationship configured on '{0}.{1}' gets no navigation on '{2}': {3} already taken by a member or another relationship. Rename the id, or what holds the name.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A relationship configured in Configure gives the read face a navigation to the configured target, "
                     + "named after the id or after the target. When every name it could take is already a column, "
                     + "a collection or another relationship's navigation, it gets none rather than one whose name "
                     + "would say something else.",
        helpLinkUri: DiagnosticHelp.Link("RASK112"));

    private readonly Dictionary<string, List<ReadRelationship>> _byDependent = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReadShape> _bySource = new(StringComparer.Ordinal);

    /// <summary>Gathers every relationship the shapes declare, by the type that holds its foreign key.</summary>
    /// <remarks>A <c>HasMany&lt;D&gt;().WithOne()</c> on the principal belongs to <c>D</c>, so it is filed there.</remarks>
    public ConfiguredReferences(IEnumerable<ReadShape> shapes)
    {
        foreach (var shape in shapes)
        {
            _bySource[shape.SourceTypeName] = shape;

            foreach (var relationship in shape.Relationships)
            {
                if (!_byDependent.TryGetValue(relationship.DependentTypeName, out var list))
                {
                    _byDependent[relationship.DependentTypeName] = list = [];
                }

                if (!list.Contains(relationship))
                {
                    list.Add(relationship);
                }
            }
        }
    }

    /// <summary>Every id <paramref name="shape" /> has a relationship configured on, which no name rule may guess at.</summary>
    public HashSet<string> ForeignKeysOf(ReadShape shape) =>
        new(Of(shape).SelectMany(static r => r.ForeignKeys), StringComparer.Ordinal);

    /// <summary>The navigations <paramref name="shape" />'s configured relationships give it, each name added to <paramref name="taken" />.</summary>
    /// <remarks>
    ///     An id that names itself is settled first — its name is its own, no other id has it. Then each of the rest takes
    ///     its target's name if nothing else holds or wants it, and its own otherwise.
    /// </remarks>
    public List<ReadNavigation> Navigations(SourceProductionContext context, ReadShape shape, HashSet<string> taken)
    {
        var candidates = Of(shape).Select(r => Candidate(shape, r)).OfType<Navigable>().ToList();
        var names = new Dictionary<Navigable, string>();

        foreach (var candidate in candidates.Where(c => c.NamedById && taken.Add(c.IdName!)))
        {
            names[candidate] = candidate.IdName!;
        }

        var rest = candidates.Where(c => !names.ContainsKey(c)).ToList();

        foreach (var candidate in rest)
        {
            var target = candidate.Target.SourceName;
            var wanted = rest.Count(c => string.Equals(c.Target.SourceName, target, StringComparison.Ordinal));

            if (wanted == 1 && taken.Add(target))
            {
                names[candidate] = target;
            }
            else if (!candidate.NamedById && candidate.IdName is { } own && taken.Add(own))
            {
                names[candidate] = own;
            }
        }

        var navigations = new List<ReadNavigation>();

        foreach (var candidate in candidates)
        {
            if (!names.TryGetValue(candidate, out var name))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask112, shape.Location?.ToLocation(), shape.SourceName, candidate.ForeignKey.Name, shape.Name, Tried(candidate)));
                continue;
            }

            navigations.Add(new ReadNavigation(name, candidate.Target.FullyQualifiedName, candidate.ForeignKey.Name, candidate.ForeignKey.Nullable));
        }

        return navigations;
    }

    private List<ReadRelationship> Of(ReadShape shape) =>
        _byDependent.TryGetValue(shape.SourceTypeName, out var list) ? list : [];

    // The id's column and target, when the relationship is one a navigation can carry: one column, onto a face this
    // compilation reads, joined on the target's key and of its key's type.
    private Navigable? Candidate(ReadShape shape, ReadRelationship relationship)
    {
        if (relationship is not { OnAlternateKey: false, ForeignKeys.Count: 1 } ||
            !_bySource.TryGetValue(relationship.PrincipalTypeName, out var target) ||
            shape.Members.FirstOrDefault(m => string.Equals(m.Name, relationship.ForeignKeys[0], StringComparison.Ordinal)) is not { } foreignKey ||
            !string.Equals(foreignKey.TypeName, target.IdTypeName, StringComparison.Ordinal))
        {
            return null;
        }

        var idName = foreignKey.Name.Length > 2 && foreignKey.Name.EndsWith("Id", StringComparison.Ordinal)
            ? foreignKey.Name.Substring(0, foreignKey.Name.Length - 2)
            : null;

        return new Navigable(foreignKey, target, idName, idName?.EndsWith(LastWord(target.SourceName), StringComparison.Ordinal) == true);
    }

    private static string Tried(Navigable candidate) =>
        candidate.IdName is null || string.Equals(candidate.IdName, candidate.Target.SourceName, StringComparison.Ordinal)
            ? $"'{candidate.Target.SourceName}' is"
            : $"'{candidate.Target.SourceName}' and '{candidate.IdName}' are";

    // ForeignTaxCategory -> Category: the noun an id has to end in to be named after itself.
    internal static string LastWord(string name)
    {
        for (var i = name.Length - 1; i > 0; i--)
        {
            if (char.IsUpper(name[i]))
            {
                return name.Substring(i);
            }
        }

        return name;
    }

    // A relationship a navigation can carry: its id's column, the face it points at, the name the id gives it, and
    // whether the id names itself — ends in the target's last word.
    private sealed record Navigable(ReadMember ForeignKey, ReadShape Target, string? IdName, bool NamedById);
}
