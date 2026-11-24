using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// Every Secret<T> a model holds — on an entity, inside a value object, inside a JSON collection's elements — is stored as
// its ciphertext. One pre-convention registration per closed type reaches all of those places at once, JSON included,
// which a per-property mapping could not: EF Core configures a JSON column's members itself.
public sealed partial class ModelRegistryGenerator
{
    // The T of each Secret<T> the entity holds, however deep, fully qualified.
    private static EquatableArray<string> SecretsHeldBy(INamedTypeSymbol entity)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var property in GeneratedModelShape.Properties(entity).Where(static p => !p.IsStatic && !p.IsIndexer))
        {
            Visit(property.Type, 0);
        }

        return new EquatableArray<string>([.. found]);

        void Visit(ITypeSymbol type, int depth)
        {
            if (depth > AggregateShape.MaxValueObjectDepth || type is not INamedTypeSymbol named || !seen.Add(named))
            {
                return;
            }

            if (AggregateShape.SecretValueOf(named) is { } held)
            {
                found.Add(held.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                return;
            }

            if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                Visit(named.TypeArguments[0], depth);
                return;
            }

            if (named.SpecialType == SpecialType.System_String || AggregateShape.IsEntity(named))
            {
                return;
            }

            if (named.AllInterfaces.Append(named).FirstOrDefault(static i =>
                    i.IsGenericType && i.ConstructedFrom.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T) is { } sequence)
            {
                Visit(sequence.TypeArguments[0], depth + 1);
                return;
            }

            if (AggregateShape.IsValueObjectType(named))
            {
                foreach (var member in AggregateShape.StoredProperties(named))
                {
                    Visit(member.Type, depth + 1);
                }
            }
        }
    }

    private static void EmitSecretConversions(StringBuilder source, List<Candidate> entities)
    {
        foreach (var held in entities.SelectMany(static e => e.Secrets).Distinct(StringComparer.Ordinal).OrderBy(static s => s, StringComparer.Ordinal))
        {
            source.Append("        configurationBuilder.Properties<global::Rask.Values.Secret<").Append(held).AppendLine(">>()");
            source.Append("            .HaveConversion<global::Rask.Data.SecretConverter<").Append(held).AppendLine(">>();");
        }
    }
}
