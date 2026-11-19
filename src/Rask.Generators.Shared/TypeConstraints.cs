using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>A generic declaration's constraints, re-declared on emitted code that takes the same type parameters.</summary>
internal static class TypeConstraints
{
    /// <summary>
    ///     Every <c>where</c> clause, in the order C# requires: the primary constraint (<c>class</c>, <c>class?</c>,
    ///     <c>struct</c>, <c>unmanaged</c>, <c>notnull</c>), the types with their nullability — which may name other
    ///     type parameters of the same declaration — then <c>new()</c>, then <c>allows ref struct</c>. Empty when none.
    /// </summary>
    public static string Clauses(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        if (typeParameters.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var clauses = new StringBuilder();
        foreach (var parameter in typeParameters)
        {
            var parts = new List<string>();
            if (parameter.HasReferenceTypeConstraint)
            {
                parts.Add(parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class");
            }
            else if (parameter.HasUnmanagedTypeConstraint)
            {
                parts.Add("unmanaged");
            }
            else if (parameter.HasValueTypeConstraint)
            {
                parts.Add("struct");
            }
            else if (parameter.HasNotNullConstraint)
            {
                parts.Add("notnull");
            }

            parts.AddRange(parameter.ConstraintTypes.Select(static t => t.ToDisplayString(GeneratedModelShape.TypeFormat)));

            if (parameter.HasConstructorConstraint)
            {
                parts.Add("new()");
            }

            if (parameter.AllowsRefLikeType)
            {
                parts.Add("allows ref struct");
            }

            if (parts.Count > 0)
            {
                clauses.Append(" where ").Append(parameter.Name).Append(" : ").Append(string.Join(", ", parts));
            }
        }

        return clauses.ToString();
    }
}
