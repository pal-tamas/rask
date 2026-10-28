using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>
///     What the form of <see cref="Holder" /> carries for a child keyed by several columns, and why it cannot carry it
///     when it cannot.
/// </summary>
/// <remarks>
///     A link — <c>Entity&lt;(Company, Teaor)&gt;</c> and nothing else — is a list of the other part's ids, saved as a
///     set. Anything more is a list of rows, matched to the stored ones by the parts of the key the holder does not
///     give. The holder's part is never on the form: the save fills it.
/// </remarks>
internal sealed class CompositeChildForm(
    CompositeChildKind kind,
    INamedTypeSymbol holder,
    IReadOnlyList<KeyPart> ownParts,
    string? problem)
{
    /// <summary>A list of ids, or a list of rows.</summary>
    public CompositeChildKind Kind { get; } = kind;

    /// <summary>The aggregate, or the child, whose collection holds the rows.</summary>
    public INamedTypeSymbol Holder { get; } = holder;

    /// <summary>The parts of the key the holder does not give, in key order: what a posted row is matched by.</summary>
    public IReadOnlyList<KeyPart> OwnParts { get; } = ownParts;

    /// <summary>Why a form cannot carry the rows, or null when it can.</summary>
    public string? Problem { get; } = problem;

    /// <summary>The form's list of ids for the collection <paramref name="collection" />: <c>Activities</c> → <c>ActivityIds</c>.</summary>
    public static string IdListName(string collection) => FormListName.Ids(collection);

    /// <summary>
    ///     How <paramref name="holder" />'s form carries <paramref name="child" />, or null when the child's key cannot
    ///     be built at all — which the key's own RASK106 reports.
    /// </summary>
    public static CompositeChildForm? For(INamedTypeSymbol holder, ModelChild child)
    {
        var type = child.ChildType;
        var parts = CompositeKeyShape.ValidPartsOf(type);

        if (parts.Count == 0 || !CompositeKeyShape.HasCompositeKey(type))
        {
            return null;
        }

        var own = parts.Where(p => !SymbolEqualityComparer.Default.Equals(p.Aggregate, holder)).ToList();
        var shape = GeneratedModelShape.Describe(type);
        var fields = shape.Members.Count(m => m.Role == ModelMemberRole.Value && !own.Exists(p => string.Equals(p.Name, m.Property.Name, StringComparison.Ordinal)));
        var kind = own.Count == 1 && fields == 0 && shape.ValueCollections.Count == 0 && shape.Children.Count == 0
                   && shape.CompositeChildren.Count == 0
            ? CompositeChildKind.Ids
            : CompositeChildKind.Rows;

        return new CompositeChildForm(kind, holder, own, ProblemOf(holder, child, kind, parts.Count - own.Count, shape));
    }

    /// <summary>
    ///     How the one holder whose key part names <paramref name="child" />'s collection carries it, or null when no
    ///     such holder is found.
    /// </summary>
    public static CompositeChildForm? Of(INamedTypeSymbol child)
    {
        var held = CompositeKeyShape.ValidPartsOf(child)
            .Select(static p => p.Aggregate)
            .Where(static a => a is not null)
            .Distinct<INamedTypeSymbol?>(SymbolEqualityComparer.Default)
            .SelectMany(holder => GeneratedModelShape.Properties(holder!)
                .Select(p => GeneratedModelShape.DescribeChild(holder!, p))
                .Where(c => c is not null && SymbolEqualityComparer.Default.Equals(c.ChildType, child))
                .Select(c => (Holder: holder!, Child: c!)))
            .ToList();

        return held.Count == 1 ? For(held[0].Holder, held[0].Child) : null;
    }

    private static string? ProblemOf(
        INamedTypeSymbol holder, ModelChild child, CompositeChildKind kind, int holderParts, ModelShape shape)
    {
        var type = child.ChildType;

        if (holderParts == 0)
        {
            return $"no part of its key names '{holder.Name}', which holds it";
        }

        if (shape.Children.Concat(shape.CompositeChildren.Select(static c => c.Child)).FirstOrDefault() is { } rows)
        {
            return $"its rows hold rows of their own ('{rows.Name}'), which a form does not carry";
        }

        if (!type.InstanceConstructors.Any(static c => c.Parameters.Length == 0))
        {
            return "it has no constructor without parameters, so a form cannot add a row";
        }

        if (kind == CompositeChildKind.Ids &&
            GeneratedModelShape.Properties(holder).Any(p => string.Equals(p.Name, IdListName(child.Name), StringComparison.Ordinal)))
        {
            return $"its form list '{IdListName(child.Name)}' is a name '{holder.Name}' already declares";
        }

        var unwritable = CompositeKeyShape.ValidPartsOf(type).FirstOrDefault(p =>
            p.Aggregate is null &&
            type.GetMembers(p.Name).OfType<IPropertySymbol>().FirstOrDefault() is { } declared &&
            GeneratedModelShape.WriteKindOf(declared) is null);

        return unwritable is null
            ? null
            : $"its key part '{unwritable.Name}' has no setter or backing field a form can write";
    }
}
