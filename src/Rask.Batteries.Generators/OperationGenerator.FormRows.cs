using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

// A list of rows as a parameter — `Change(IReadOnlyList<SupplyPartSpec> parts)` for a `Children<SupplyPart, SupplyPartSpec>
// .Sync` — is an editable list on the operation's form: `f.Parts`, a row per child the aggregate holds, each row's
// fields the spec's members (`row.Quantity`), a row added with `f.Parts.Add(new())` and taken away with
// `f.Parts.Remove(row)`. Each row is made into its spec, a refusal under its place (`Parts[1].Quantity`), which is
// where the dry run puts what the child's own rules say too.
public sealed partial class OperationGenerator
{
    // The list types a row list is handed to the operation as; each takes a collection expression.
    private static readonly HashSet<string> RowLists = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
        "System.Collections.Generic.IEnumerable<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.List<T>",
    };

    /// <summary>The rows of a list parameter: a nested class of the form's model, one per row.</summary>
    /// <param name="Model">The nested class: <c>PartsRow</c>.</param>
    /// <param name="Spec">What one row is made into.</param>
    /// <param name="Members">The spec's members, a field each.</param>
    /// <param name="Arguments">
    ///     How the spec is built from the members' made values: named constructor arguments (<c>Id: __m_Id</c>), or an
    ///     object initializer's assignments when <paramref name="ByInitializer" />.
    /// </param>
    /// <param name="ByInitializer">Whether the spec is built by a parameterless constructor and an object initializer.</param>
    private sealed record FormRows(string Model, string Spec, EquatableArray<FormField> Members, EquatableArray<string> Arguments, bool ByInitializer);

    private static FormField? RowsFieldOf(IParameterSymbol parameter, INamedTypeSymbol aggregate, string input)
    {
        if (RowOf(parameter.Type) is not { } spec || SpecMembers(spec) is not { } members)
        {
            return null;
        }

        var property = Capitalised(parameter.Name);
        var child = ChildrenOf(aggregate, property);
        var held = "__" + property + "Child";
        var owner = child?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var fields = new List<FormField>();
        foreach (var member in members.Properties)
        {
            var kept = child is null ? null : PropertyNamed(child, member.Name);
            var optional = member.NullableAnnotation == NullableAnnotation.Annotated ||
                           member.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
            if (MemberField(member.Name, member.Type, optional, kept, held, lift: true) is { } field)
            {
                fields.Add(field with { Shape = StatedShape(field, member.Type, kept is null ? null : owner, kept ?? member) });
                continue;
            }

            // A value object of several values on a row: a group of its own (`f.Parts[i].Start.Date`), filled from the
            // child's member of that name and type.
            var stored = kept is not null && Same(kept.Type, member.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)) ? kept : null;
            if (GroupOf(member.Name, member.Type, optional, stored, held, input, property + member.Name + "Fields") is not { } group)
            {
                return null;
            }

            fields.Add(group);
        }

        var model = property + "Row";
        var fills = fields.Where(static f => f.Fill is not null).Select(static f => f.Property + " = " + f.Fill).ToList();
        var fill = child is null
            ? null
            : "[.. global::System.Linq.Enumerable.Select(aggregate." + property + ", static " + held + " => new " + input + "." + model +
              " { " + string.Join(", ", fills) + " })]";

        return new FormField(property, "global::System.Collections.Generic.List<" + model + ">", null, Optional: false, Required: false, Text: false, fill)
        {
            Rows = new FormRows(
                model,
                spec.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                new EquatableArray<FormField>(fields),
                new EquatableArray<string>(members.Arguments),
                members.ByInitializer),
        };
    }

    // The row type of a list: `IReadOnlyList<SupplyPartSpec>` holds `SupplyPartSpec` — a type of members of its own, not a
    // value, a value object or an entity.
    private static INamedTypeSymbol? RowOf(ITypeSymbol type)
    {
        var element = type switch
        {
            IArrayTypeSymbol { Rank: 1 } array => array.ElementType,
            INamedTypeSymbol { TypeArguments.Length: 1 } list when RowLists.Contains(list.OriginalDefinition.ToDisplayString()) => list.TypeArguments[0],
            _ => null,
        };

        return element is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct, SpecialType: SpecialType.None, IsAbstract: false, IsGenericType: false } row &&
               row.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T &&
               AggregateShape.DeclaredValueBaseOf(row) is null &&
               !AggregateShape.IsValueRecord(row) &&
               !AggregateShape.IsEntity(row) &&
               row.ContainingNamespace?.ToDisplayString().StartsWith("System", StringComparison.Ordinal) != true &&
               row.DeclaredAccessibility == Accessibility.Public
            ? row
            : null;
    }

    private sealed record SpecShape(List<IPropertySymbol> Properties, List<string> Arguments, bool ByInitializer);

    // How a spec is built: a public constructor naming every member (a positional record), else a public parameterless
    // one and a public setter on each. Null for a spec generated code cannot build.
    private static SpecShape? SpecMembers(INamedTypeSymbol spec)
    {
        var properties = spec.GetMembers().OfType<IPropertySymbol>()
            .Where(static p => !p.IsStatic && !p.IsIndexer && p.DeclaredAccessibility == Accessibility.Public &&
                               p.GetMethod is { DeclaredAccessibility: Accessibility.Public } &&
                               !string.Equals(p.Name, "EqualityContract", StringComparison.Ordinal))
            .ToList();
        if (properties.Count == 0)
        {
            return null;
        }

        var constructor = spec.InstanceConstructors
            .Where(static c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length > 0)
            .FirstOrDefault(c => c.Parameters.All(p => properties.Exists(m => string.Equals(m.Name, p.Name, StringComparison.OrdinalIgnoreCase) && Same(m.Type, p.Type))));
        if (constructor is not null)
        {
            var named = constructor.Parameters.Select(p => properties.First(m => string.Equals(m.Name, p.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            var arguments = constructor.Parameters.Select(p => Identifier(p.Name) + ": __m_" + properties.First(m => string.Equals(m.Name, p.Name, StringComparison.OrdinalIgnoreCase)).Name).ToList();
            return new SpecShape(named, arguments, ByInitializer: false);
        }

        if (spec.InstanceConstructors.Any(static c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public) &&
            properties.All(static p => p.SetMethod is { DeclaredAccessibility: Accessibility.Public }))
        {
            return new SpecShape(properties, properties.Select(static p => p.Name + " = __m_" + p.Name).ToList(), ByInitializer: true);
        }

        return null;
    }

    // The child a row stands for: the entity of the aggregate's list of that name — `Children<SupplyPart, …> Parts`.
    private static INamedTypeSymbol? ChildrenOf(INamedTypeSymbol aggregate, string property) =>
        PropertyNamed(aggregate, property)?.Type is INamedTypeSymbol held
            ? held.AllInterfaces.Concat([held])
                .Where(static i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                .Select(static i => i.TypeArguments[0])
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(AggregateShape.IsEntity)
            : null;

    // Every row made into its spec, in their order: a refusal under the row's place, `Parts[1].Quantity`, and a row any
    // member of which is refused left out (the read fails as a whole).
    private static void AppendRowsMade(StringBuilder s, string input, Operation operation, FormField field)
    {
        var rows = field.Rows!;
        var list = "global::System.Collections.Generic.List<";
        s.AppendLine();
        s.Append("    private static ").Append(list).Append(rows.Spec).Append("> ").Append(operation.Name).Append(field.Property).Append("Made(")
            .Append(list).Append(input).Append('.').Append(rows.Model).Append("> given, string at, ").Append(FailureListFqn).AppendLine(" failures)");
        s.AppendLine("    {");
        s.Append("        var __made = new ").Append(list).Append(rows.Spec).AppendLine(">(given.Count);");
        s.AppendLine("        for (var __i = 0; __i < given.Count; __i++)");
        s.AppendLine("        {");
        s.AppendLine("            var __row = given[__i];");
        s.AppendLine("            var __at = at + \"[\" + __i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\";");
        s.AppendLine("            var __before = failures.Count;");
        foreach (var member in rows.Members)
        {
            var at = "__at + " + SymbolDisplay.FormatLiteral("." + member.Property, quote: true);
            var made = member.Group is null
                ? MadeFrom(member, "__row." + member.Property, at)
                : operation.Name + field.Property + member.Property + "Made(__row." + member.Property + ", " + at + ", failures)";
            if (member.Group is not null && !member.Optional)
            {
                made += "!";
            }

            s.Append("            var __m_").Append(member.Property).Append(" = ").Append(made).AppendLine(";");
        }

        s.AppendLine("            if (failures.Count == __before)");
        s.AppendLine("            {");
        s.Append("                __made.Add(new ").Append(rows.Spec)
            .Append(rows.ByInitializer ? " { " + string.Join(", ", rows.Arguments) + " }" : "(" + string.Join(", ", rows.Arguments) + ")")
            .AppendLine(");");
        s.AppendLine("            }");
        s.AppendLine("        }");
        s.AppendLine();
        s.AppendLine("        return __made;");
        s.AppendLine("    }");

        foreach (var member in rows.Members.Where(static m => m.Group is not null))
        {
            AppendGroupMade(s, input, operation, member with { Property = field.Property + member.Property });
        }
    }

    // The groups a list's rows hold, each with the key it is registered under (`ChangePartsStart`).
    private static IEnumerable<(string Key, FormField Group)> RowGroups(string operation, FormField field) =>
        field.Rows is { } rows
            ? rows.Members.Where(static m => m.Group is not null).Select(m => (operation + field.Property + m.Property, m))
            : Enumerable.Empty<(string Key, FormField Group)>();
}
