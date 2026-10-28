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
        "The relationship configured on '{0}.{1}' gets no navigation on '{2}': {3}. Rename the id, or what holds the name.",
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
    public List<ReadNavigation> Navigations(SourceProductionContext context, ReadShape shape, HashSet<string> taken)
    {
        var candidates = Of(shape)
            .Select(r => Candidate(shape, r))
            .Where(static c => c is not null)
            .Select(static c => c!.Value)
            .ToList();

        var navigations = new List<ReadNavigation>();

        foreach (var (foreignKey, preferred, other, target) in candidates)
        {
            var name = Pick(candidates, preferred, other, taken);

            if (name is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask112, shape.Location?.ToLocation(), shape.SourceName, foreignKey.Name, shape.Name, Why(preferred, other)));
                continue;
            }

            taken.Add(name);
            navigations.Add(new ReadNavigation(name, target.FullyQualifiedName, foreignKey.Name, foreignKey.Nullable));
        }

        return navigations;
    }

    // The preferred name, unless another relationship prefers it too; else the other, unless any relationship prefers it.
    private static string? Pick(
        List<(ReadMember ForeignKey, string Preferred, string? Other, ReadShape Target)> candidates,
        string preferred,
        string? other,
        HashSet<string> taken)
    {
        if (!taken.Contains(preferred) && candidates.Count(c => string.Equals(c.Preferred, preferred, StringComparison.Ordinal)) == 1)
        {
            return preferred;
        }

        return other is not null && !taken.Contains(other) &&
               !candidates.Exists(c => string.Equals(c.Preferred, other, StringComparison.Ordinal))
            ? other
            : null;
    }

    private List<ReadRelationship> Of(ReadShape shape) =>
        _byDependent.TryGetValue(shape.SourceTypeName, out var list) ? list : [];

    // The id's column and target, when the relationship is one a navigation can carry: one column, onto a face this
    // compilation reads, joined on the target's key and of its key's type.
    private (ReadMember ForeignKey, string Preferred, string? Other, ReadShape Target)? Candidate(ReadShape shape, ReadRelationship relationship)
    {
        if (relationship is not { OnAlternateKey: false, ForeignKeys.Count: 1 } ||
            !_bySource.TryGetValue(relationship.PrincipalTypeName, out var target) ||
            shape.Members.FirstOrDefault(m => string.Equals(m.Name, relationship.ForeignKeys[0], StringComparison.Ordinal)) is not { } foreignKey ||
            !string.Equals(foreignKey.TypeName, target.IdTypeName, StringComparison.Ordinal))
        {
            return null;
        }

        var stripped = foreignKey.Name.Length > 2 && foreignKey.Name.EndsWith("Id", StringComparison.Ordinal)
            ? foreignKey.Name.Substring(0, foreignKey.Name.Length - 2)
            : null;

        return stripped is not null && stripped.EndsWith(LastWord(target.SourceName), StringComparison.Ordinal)
            ? (foreignKey, stripped, target.SourceName, target)
            : (foreignKey, target.SourceName, stripped, target);
    }

    // ForeignTaxCategory -> Category: the noun an id has to end in to be named after itself.
    private static string LastWord(string name)
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

    private static string Why(string preferred, string? other) =>
        other is null || string.Equals(other, preferred, StringComparison.Ordinal)
            ? $"'{preferred}' is already taken"
            : $"'{preferred}' and '{other}' are both already taken";
}
