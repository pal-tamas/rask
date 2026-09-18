using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Cqrs.Generators;
using Rask.Generators.Shared;

namespace Rask.Data.Generators;

/// <summary>
/// Generates the read face of every mapped entity — <c>OrderRead</c> for <c>Order</c> — and the
/// <c>Order.Read</c> that queries it.
/// </summary>
/// <remarks>
/// <para>
/// The write model and the read model come from one declaration. An aggregate references another by id
/// only, so a write cannot cross a boundary by accident; the read face carries the navigations the write
/// model is not allowed to have, inferred from those same ids, and may join across as many aggregates as it
/// likes. Every mapped entity gets one, children included: reads have no borders, so a part is queryable on
/// its own even though it is only writable through its root.
/// </para>
/// <para>
/// This emits the CLR <em>shape</em>. The <em>mapping</em> is mirrored from the built write model at
/// runtime by <c>Rask.Data.ReadModelRegistry</c>, because an entity's own static <c>Configure</c> can ignore
/// a property, rename a column or add a conversion, and none of that is visible here.
/// </para>
/// </remarks>
[Generator]
public sealed class ReadModelGenerator : IIncrementalGenerator
{
    private const string Query = "global::Rask.Data.ModelQuery";
    private const string Registry = "global::Rask.Data.ReadModelRegistry";
    private const string Mapping = "global::Rask.Data.ReadEntityMapping";

    private static readonly DiagnosticDescriptor Rask089 = new(
        "RASK089",
        "Id looks like a reference but no navigation was inferred",
        "'{0}.{1}' names the aggregate '{2}', but {3}, so no navigation appears on '{0}Read'; "
        + "rename the property, or give it the aggregate's own key type",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A read face's navigations are inferred from the aggregate's ids: a CustomerId whose "
                     + "type is Customer's key becomes a Customer navigation. An id that names an aggregate "
                     + "but cannot be matched to it produces no navigation at all, and without this warning "
                     + "the only symptom is a join the author expected and never got.",
        helpLinkUri: DiagnosticHelp.Link("RASK089"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var shapes = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, token) => Describe(ctx, token))
            .Where(static shape => shape is not null)
            .Select(static (shape, _) => shape!);

        context.RegisterSourceOutput(shapes.Collect(), static (spc, all) => Emit(spc, all));
    }

    private static ReadShape? Describe(GeneratorSyntaxContext ctx, CancellationToken token) =>
        ctx.Node is ClassDeclarationSyntax declaration &&
        ctx.SemanticModel.GetDeclaredSymbol(declaration, token) is INamedTypeSymbol symbol &&
        ReadModelShape.IsCandidate(symbol)
            ? ReadModelShape.Describe(symbol, token)
            : null;

    private static void Emit(SourceProductionContext context, ImmutableArray<ReadShape> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        // Distinct because a partial class contributes one shape per declaration, and ordered so the
        // generated files are stable across builds.
        var shapes = candidates
            .Distinct()
            .OrderBy(static s => s.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();

        // Only an aggregate is a reference target: a child is part of somebody, and an id naming one would
        // be a reference into the middle of an aggregate, which is the thing the border exists to stop.
        var aggregates = new Dictionary<string, ReadModelShape.ReadTarget>(StringComparer.Ordinal);

        foreach (var shape in shapes.Where(static s => s.IsAggregate))
        {
            aggregates[shape.SourceName] =
                new ReadModelShape.ReadTarget(shape.FullyQualifiedName, shape.IdTypeName, shape.SourceTypeName);
        }

        // A child's navigation back to its root is declared on the ROOT's shape, so the child's own face has
        // to be told about it before it can be emitted.
        var inverses = new Dictionary<string, (string RootReadType, string Name)>(StringComparer.Ordinal);

        foreach (var shape in shapes)
        {
            foreach (var child in shape.Children)
            {
                inverses[child.ChildReadType] = (shape.FullyQualifiedName, child.Inverse);
            }
        }

        var mappings = new List<(ReadShape Shape, List<ReadNavigation> Navigations)>();

        foreach (var shape in shapes)
        {
            mappings.Add((shape, Resolve(context, shape, aggregates, inverses)));
        }

        foreach (var (shape, navigations) in mappings)
        {
            context.AddSource(
                HintName(shape),
                SourceText.From(EmitFace(shape, navigations, inverses), Encoding.UTF8));
        }

        context.AddSource("__RaskReadModelRegistry.g.cs", SourceText.From(EmitRegistry(mappings), Encoding.UTF8));
    }

    // Every {X}Id that resolves, with the ones that cannot reported rather than silently dropped.
    private static List<ReadNavigation> Resolve(
        SourceProductionContext context,
        ReadShape shape,
        IReadOnlyDictionary<string, ReadModelShape.ReadTarget> aggregates,
        IReadOnlyDictionary<string, (string RootReadType, string Name)> inverses)
    {
        var navigations = new List<ReadNavigation>();

        // A name the face already uses: a column, or the navigation back to the root. A child holding an
        // explicit OrderId is exactly this case — the relationship is already there, under that name.
        var taken = new HashSet<string>(shape.Members.Select(static m => m.Name), StringComparer.Ordinal);

        if (inverses.TryGetValue(shape.FullyQualifiedName, out var inverse))
        {
            taken.Add(inverse.Name);
        }

        foreach (var child in shape.Children)
        {
            taken.Add(child.Name);
        }

        foreach (var collection in shape.ValueCollections)
        {
            taken.Add(collection.Name);
        }

        foreach (var reference in shape.References)
        {
            var navigation = ReadModelShape.Resolve(reference, shape.SourceTypeName, aggregates, out var problem);

            if (problem is not ReadModelShape.ReadReferenceProblem.None)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask089,
                    shape.Location?.ToLocation(),
                    shape.SourceName,
                    reference.Property,
                    reference.Target,
                    problem is ReadModelShape.ReadReferenceProblem.KeyTypeMismatch
                        ? $"'{reference.IdTypeName}' is not that aggregate's key type"
                        : "more than one aggregate's name ends there"));

                continue;
            }

            if (navigation is null || !taken.Add(navigation.Name))
            {
                continue;
            }

            navigations.Add(navigation);
        }

        return navigations;
    }

    private static string EmitFace(
        ReadShape shape,
        List<ReadNavigation> navigations,
        IReadOnlyDictionary<string, (string RootReadType, string Name)> inverses)
    {
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        s.AppendLine();

        if (shape.Namespace.Length > 0)
        {
            s.Append("namespace ").Append(shape.Namespace).AppendLine(";");
            s.AppendLine();
        }

        s.Append("/// <summary>The read face of <see cref=\"").Append(shape.SourceTypeName)
            .AppendLine("\" />: its columns, and the navigations its ids imply.</summary>");
        s.AppendLine("/// <remarks>");
        s.AppendLine("/// Generated, and read-only by construction — no behaviour, and not in the write context, so");
        s.AppendLine("/// there is nothing here to change or save. Query it with <c>" + shape.SourceName + ".Read</c>.");
        s.AppendLine("/// </remarks>");
        s.Append(shape.Accessibility).Append(" sealed class ").Append(shape.Name)
            .AppendLine(" : global::Rask.Data.IReadModel");
        s.AppendLine("{");

        foreach (var member in shape.Members)
        {
            s.Append("    /// <summary>The <c>").Append(member.ColumnName).AppendLine("</c> column.</summary>");
            s.Append("    public ").Append(member.TypeName).Append(member.Nullable ? "? " : " ")
                .Append(member.Name).Append(" { get; init; }")
                .AppendLine(member is { Nullable: false, IsReferenceType: true } ? " = null!;" : string.Empty);
            s.AppendLine();
        }

        foreach (var navigation in navigations)
        {
            s.Append("    /// <summary>Inferred from <see cref=\"").Append(navigation.ForeignKey)
                .AppendLine("\" />.</summary>");
            s.Append("    public ").Append(navigation.TargetReadType).Append(navigation.Nullable ? "? " : " ")
                .Append(navigation.Name).Append(" { get; init; }")
                .AppendLine(navigation.Nullable ? string.Empty : " = null!;");
            s.AppendLine();
        }

        if (inverses.TryGetValue(shape.FullyQualifiedName, out var inverse))
        {
            s.AppendLine("    /// <summary>The aggregate this is part of.</summary>");
            s.Append("    public ").Append(inverse.RootReadType).Append(' ').Append(inverse.Name)
                .AppendLine(" { get; init; } = null!;");
            s.AppendLine();
        }

        foreach (var child in shape.Children)
        {
            s.Append("    /// <summary>The <c>").Append(child.Name).AppendLine("</c> of this aggregate.</summary>");
            s.Append("    public global::System.Collections.Generic.List<").Append(child.ChildReadType)
                .Append("> ").Append(child.Name).AppendLine(" { get; init; } = [];");
            s.AppendLine();
        }

        foreach (var collection in shape.ValueCollections)
        {
            s.Append("    /// <summary>The <c>").Append(collection.Name).Append("</c> column")
                .AppendLine(collection.IsValueObject ? ", held as JSON.</summary>" : ".</summary>");
            s.Append("    public global::System.Collections.Generic.List<").Append(collection.ElementTypeName)
                .Append("> ").Append(collection.Name).AppendLine(" { get; init; } = [];");
            s.AppendLine();
        }

        s.AppendLine("}");
        s.AppendLine();

        s.Append("/// <summary>Puts <c>Read</c> on <see cref=\"").Append(shape.SourceTypeName)
            .AppendLine("\" />.</summary>");
        s.Append(shape.Accessibility).Append(" static class ").Append(shape.SourceName).AppendLine("ReadAccess");
        s.AppendLine("{");
        s.Append("    extension(").Append(shape.SourceTypeName).AppendLine(")");
        s.AppendLine("    {");
        s.Append("        /// <summary>Queries <see cref=\"").Append(shape.FullyQualifiedName)
            .AppendLine("\" /> — the read side, where there are no aggregate borders.</summary>");
        s.AppendLine("        /// <example>");
        s.Append("        ///     <code>await ").Append(shape.SourceName)
            .AppendLine(".Read.Where(x =&gt; x.CreatedAt &gt; since).ToListAsync();</code>");
        s.AppendLine("        /// </example>");
        s.Append("        public static ").Append(Query).Append('<').Append(shape.FullyQualifiedName)
            .Append("> Read => global::Rask.Data.GeneratedReadQuery.Of<").Append(shape.FullyQualifiedName)
            .AppendLine(">();");
        s.AppendLine("    }");
        s.AppendLine("}");

        return s.ToString();
    }

    private static string EmitRegistry(List<(ReadShape Shape, List<ReadNavigation> Navigations)> mappings)
    {
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/>");
        s.AppendLine("#nullable enable");
        s.AppendLine();
        s.AppendLine("namespace Rask.Data.Generated;");
        s.AppendLine();
        s.AppendLine("/// <summary>This assembly's contribution to the Rask read model.</summary>");
        s.AppendLine("file static class __RaskReadModelRegistry");
        s.AppendLine("{");
        s.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        s.AppendLine("    internal static void Initialize() =>");
        s.Append("        ").Append(Registry).AppendLine(".Replace(typeof(__RaskReadModelRegistry), Entities());");
        s.AppendLine();
        s.Append("    private static ").Append(Mapping).AppendLine("[] Entities() =>");
        s.AppendLine("    [");

        foreach (var (shape, navigations) in mappings)
        {
            s.Append("        new ").Append(Mapping).AppendLine("(");
            s.Append("            typeof(").Append(shape.FullyQualifiedName).AppendLine("),");
            s.Append("            typeof(").Append(shape.SourceTypeName).AppendLine("),");
            s.Append("            ").Append(shape.IsAggregate ? "true" : "false").AppendLine(",");
            s.Append("            ").Append(Literal(shape.TableName)).AppendLine(",");

            s.AppendLine("            [");
            foreach (var member in shape.Members)
            {
                s.Append("                new global::Rask.Data.ReadColumnMapping(")
                    .Append(Literal(member.Name)).Append(", ")
                    .Append(Literal(member.Path)).Append(", ")
                    .Append(Literal(member.ColumnName)).AppendLine("),");
            }

            s.AppendLine("            ],");

            s.AppendLine("            [");
            foreach (var navigation in navigations)
            {
                s.Append("                new global::Rask.Data.ReadReferenceMapping(")
                    .Append(Literal(navigation.Name)).Append(", typeof(")
                    .Append(navigation.TargetReadType).Append("), ")
                    .Append(Literal(navigation.ForeignKey)).AppendLine("),");
            }

            s.AppendLine("            ],");

            s.AppendLine("            [");
            foreach (var child in shape.Children)
            {
                s.Append("                new global::Rask.Data.ReadChildMapping(")
                    .Append(Literal(child.Name)).Append(", typeof(")
                    .Append(child.ChildReadType).Append("), typeof(")
                    .Append(child.ChildWriteType).Append("), ")
                    .Append(Literal(child.Inverse)).AppendLine("),");
            }

            s.AppendLine("            ],");

            s.AppendLine("            [");
            foreach (var collection in shape.ValueCollections)
            {
                s.Append("                new global::Rask.Data.ReadValueCollectionMapping(")
                    .Append(Literal(collection.Name))
                    .Append(collection.IsValueObject ? ", typeof(" + collection.ElementTypeName + ")" : ", null")
                    .AppendLine("),");
            }

            s.AppendLine("            ]),");
        }

        s.AppendLine("    ];");
        s.AppendLine("}");

        return s.ToString();
    }

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // One file per face, named after the type it reads so a stack trace or a "go to generated" lands
    // somewhere recognisable. The global:: prefix and the dots go, because a hint name is a file name.
    private static string HintName(ReadShape shape) =>
        shape.FullyQualifiedName.Substring("global::".Length).Replace('.', '_') + ".g.cs";
}
