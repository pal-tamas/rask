using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>
///     What a read face is called: <c>OrderRead</c> beside <c>Order</c>, or <c>OuterOrderRead</c> for a nested one.
/// </summary>
/// <remarks>
///     Its own file so every generator that has to NAME a face — not describe it — links only this: another
///     generator sees the face as an error type and rebuilds its full name from here.
/// </remarks>
internal static class ReadFaceName
{
    /// <summary>The suffix a read face takes after its entity's name.</summary>
    public const string Suffix = "Read";

    /// <summary>The read face's simple name.</summary>
    /// <remarks>
    ///     A read face is always emitted at the top level — nesting it would need the containing type to be
    ///     <c>partial</c>, which is not something declaring an entity should require. The containing names
    ///     are carried into the face's own name so two entities called <c>Post</c> under different parents
    ///     do not collide, and nobody types the result: <c>Post.Where(…)</c> is the door.
    /// </remarks>
    public static string Of(INamedTypeSymbol entity)
    {
        var name = new StringBuilder(entity.Name).Append(Suffix);

        for (var parent = entity.ContainingType; parent is not null; parent = parent.ContainingType)
        {
            name.Insert(0, parent.Name);
        }

        return name.ToString();
    }

    /// <summary>The read face's fully-qualified name, ready to emit.</summary>
    public static string Fqn(INamedTypeSymbol entity) =>
        (entity.ContainingNamespace is null || entity.ContainingNamespace.IsGlobalNamespace
            ? "global::"
            : "global::" + entity.ContainingNamespace.ToDisplayString() + ".") + Of(entity);
}
