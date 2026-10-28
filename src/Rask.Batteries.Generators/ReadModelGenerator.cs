using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
/// Generates the read face of every mapped entity — <c>OrderRead</c> for <c>Order</c> — and the
/// <c>Order.Where(…)</c>, <c>Order.OrderBy(…)</c>, … that query it.
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
public sealed partial class ReadModelGenerator : IIncrementalGenerator
{
    private const string Query = "global::Rask.Data.ModelQuery";
    private const string Registry = "global::Rask.Data.ReadModelRegistry";
    private const string Mapping = "global::Rask.Data.ReadEntityMapping";

    private const string Token = "global::System.Threading.CancellationToken cancellationToken = default";
    private const string Predicate = "global::System.Linq.Expressions.Expression<global::System.Func<{R}, bool>> predicate";

    // Every opening operator of ModelQuery, reachable off the type itself: `Product.Where(…)`. ThenBy is not
    // here — it continues an order, so it only exists once a query does. A test holds this list to
    // ModelQuery's public surface, so an operator added there cannot be missing here.
    private static readonly (string Summary, string Signature, string Call)[] Forwarders =
    [
        ("Filters the rows.", "{Q} Where(" + Predicate + ")", "Where(predicate)"),
        ("The rows whose indexed text contains every word of <paramref name=\"text\" />, best match first.",
            "{Q} Search(string? text)", "Search(text)"),
        ("Orders the rows by <paramref name=\"keySelector\" />.",
            "{Q} OrderBy<TKey>(global::System.Linq.Expressions.Expression<global::System.Func<{R}, TKey>> keySelector)",
            "OrderBy(keySelector)"),
        ("Orders the rows by <paramref name=\"keySelector\" />, descending.",
            "{Q} OrderByDescending<TKey>(global::System.Linq.Expressions.Expression<global::System.Func<{R}, TKey>> keySelector)",
            "OrderByDescending(keySelector)"),
        ("Skips the first <paramref name=\"count\" /> rows.", "{Q} Skip(int count)", "Skip(count)"),
        ("Takes at most <paramref name=\"count\" /> rows.", "{Q} Take(int count)", "Take(count)"),
        ("Loads <paramref name=\"navigation\" /> with each row.",
            "{Q} Include<TProperty>(global::System.Linq.Expressions.Expression<global::System.Func<{R}, TProperty>> navigation)",
            "Include(navigation)"),
        ("Loads the navigation at <paramref name=\"navigationPropertyPath\" /> with each row.",
            "{Q} Include(string navigationPropertyPath)", "Include(navigationPropertyPath)"),
        ("Includes soft-deleted rows.", "{Q} IgnoreQueryFilters()", "IgnoreQueryFilters()"),
        ("Loads included collections in separate queries.", "{Q} AsSplitQuery()", "AsSplitQuery()"),
        ("Projects each row through <paramref name=\"selector\" />.",
            "global::Rask.Data.Projection<{R}, TResult> Select<TResult>(global::System.Linq.Expressions.Expression<global::System.Func<{R}, TResult>> selector)",
            "Select(selector)"),
        ("The rows as an <see cref=\"global::System.Linq.IQueryable{T}\" />, for a grid or LINQ of your own.",
            "global::System.Linq.IQueryable<{R}> AsQueryable()", "AsQueryable()"),
        ("The first row, or <c>null</c>.", "global::System.Threading.Tasks.Task<{R}?> First(" + Token + ")", "First(cancellationToken)"),
        ("The first matching row, or <c>null</c>.", "global::System.Threading.Tasks.Task<{R}?> First(" + Predicate + ", " + Token + ")",
            "First(predicate, cancellationToken)"),
        ("The only row, or <c>null</c>.", "global::System.Threading.Tasks.Task<{R}?> Single(" + Token + ")", "Single(cancellationToken)"),
        ("The only matching row, or <c>null</c>.", "global::System.Threading.Tasks.Task<{R}?> Single(" + Predicate + ", " + Token + ")",
            "Single(predicate, cancellationToken)"),
        ("How many rows there are.", "global::System.Threading.Tasks.Task<int> Count(" + Token + ")", "Count(cancellationToken)"),
        ("How many rows match.", "global::System.Threading.Tasks.Task<int> Count(" + Predicate + ", " + Token + ")", "Count(predicate, cancellationToken)"),
        ("How many rows there are.", "global::System.Threading.Tasks.Task<long> LongCount(" + Token + ")", "LongCount(cancellationToken)"),
        ("Whether there is any row.", "global::System.Threading.Tasks.Task<bool> Any(" + Token + ")", "Any(cancellationToken)"),
        ("Whether any row matches.", "global::System.Threading.Tasks.Task<bool> Any(" + Predicate + ", " + Token + ")", "Any(predicate, cancellationToken)"),
        ("Streams every row.", "global::System.Collections.Generic.IAsyncEnumerable<{R}> AsAsyncEnumerable(" + Token + ")",
            "AsAsyncEnumerable(cancellationToken)"),
        ("Runs <paramref name=\"query\" /> over the rows, in a context opened and disposed around it.",
            "global::System.Threading.Tasks.Task<TResult> Query<TResult>(global::System.Func<global::System.Linq.IQueryable<{R}>, global::System.Threading.CancellationToken, global::System.Threading.Tasks.Task<TResult>> query, " + Token + ")",
            "Query(query, cancellationToken)"),
    ];

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

    private static readonly DiagnosticDescriptor Rask108 = new(
        "RASK108",
        "A collection of the rows that point at a read face has a name already taken",
        "{0} does not get '{1}': the name is already {2}. Rename the id ({4}.{5}) or the {3}.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Every navigation a read face infers from an id gives its target the rows that point there, named "
                     + "after the referencing type and the id's prefix: Distribution.FromFuelingId gives "
                     + "FuelingRead.DistributionsFrom. When the target already uses that name, or two ids would both "
                     + "claim it, the collection is left out rather than renamed by a guess.",
        helpLinkUri: DiagnosticHelp.Link("RASK108"));

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
            ? ReadModelShape.Describe(symbol, ConfiguredRelationships.Of(symbol, ctx.SemanticModel.Compilation, token), token)
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

        var (aggregates, children) = ReferenceTargets(shapes);

        // A child's navigation back to its root is declared on the ROOT's shape, so the child's own face has
        // to be told about it before it can be emitted.
        // A child of TWO roots (a Chip under a Driver or a Vehicle) has a navigation back to each.
        var inverses = new Dictionary<string, List<(string RootReadType, string Name)>>(StringComparer.Ordinal);

        foreach (var shape in shapes)
        {
            foreach (var child in shape.Children)
            {
                if (!inverses.TryGetValue(child.ChildReadType, out var roots))
                {
                    inverses[child.ChildReadType] = roots = [];
                }

                if (!roots.Exists(r => string.Equals(r.Name, child.Inverse, StringComparison.Ordinal)))
                {
                    roots.Add((shape.FullyQualifiedName, child.Inverse));
                }
            }
        }

        var mappings = new List<(ReadShape Shape, List<ReadNavigation> Navigations)>();
        var configured = new ConfiguredReferences(shapes);

        foreach (var shape in shapes)
        {
            mappings.Add((shape, Resolve(context, shape, aggregates, children, inverses, configured)));
        }

        var reverses = Reverses(context, mappings, inverses);

        foreach (var (shape, navigations) in mappings)
        {
            context.AddSource(
                HintName(shape),
                SourceText.From(EmitFace(shape, navigations, inverses, reverses), Encoding.UTF8));
        }

        context.AddSource("__RaskReadModelRegistry.g.cs", SourceText.From(EmitRegistry(mappings), Encoding.UTF8));
    }

    // An aggregate is the reference target first. A child of another aggregate is one too, on the READ side only —
    // a fueling's PistolId reaches the pistol under its pump — but only for a name no aggregate takes, so a
    // navigation an app already has never changes. The write side still holds nothing but the id.
    private static (Dictionary<string, ReadModelShape.ReadTarget> Aggregates, Dictionary<string, ReadModelShape.ReadTarget> Children)
        ReferenceTargets(List<ReadShape> shapes)
    {
        var aggregates = new Dictionary<string, ReadModelShape.ReadTarget>(StringComparer.Ordinal);
        var children = new Dictionary<string, ReadModelShape.ReadTarget>(StringComparer.Ordinal);

        foreach (var shape in shapes)
        {
            (shape.IsAggregate ? aggregates : children)[shape.SourceName] =
                new ReadModelShape.ReadTarget(shape.FullyQualifiedName, shape.IdTypeName, shape.SourceTypeName);
        }

        return (aggregates, children);
    }

    // The relationships Configure declares first, then every other {X}Id that resolves by its name, with the ones that
    // cannot reported rather than silently dropped. An id a relationship is configured on is never guessed at.
    private static List<ReadNavigation> Resolve(
        SourceProductionContext context,
        ReadShape shape,
        IReadOnlyDictionary<string, ReadModelShape.ReadTarget> aggregates,
        IReadOnlyDictionary<string, ReadModelShape.ReadTarget> children,
        Dictionary<string, List<(string RootReadType, string Name)>> inverses,
        ConfiguredReferences configured)
    {
        // A child holding an explicit OrderId is exactly this case — the relationship is already there, under that name.
        var taken = Taken(shape, inverses);
        var navigations = configured.Navigations(context, shape, taken);
        var keys = configured.ForeignKeysOf(shape);

        foreach (var reference in shape.References.Where(r => !keys.Contains(r.Property)))
        {
            var (navigation, problem) = Infer(reference, shape.SourceTypeName, aggregates, children);

            if (problem is not ReadModelShape.ReadReferenceProblem.None)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask089,
                    shape.Location?.ToLocation(),
                    shape.SourceName,
                    reference.Property,
                    reference.Target,
                    problem is ReadModelShape.ReadReferenceProblem.KeyTypeMismatch
                        ? $"'{reference.IdTypeName}' is not that entity's key type"
                        : "more than one entity's name ends there"));

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

    // A name the face already uses: a column, the navigation back to the root, a collection.
    private static HashSet<string> Taken(
        ReadShape shape, Dictionary<string, List<(string RootReadType, string Name)>> inverses)
    {
        var taken = new HashSet<string>(shape.Members.Select(static m => m.Name), StringComparer.Ordinal);

        if (inverses.TryGetValue(shape.FullyQualifiedName, out var roots))
        {
            taken.UnionWith(roots.Select(static r => r.Name));
        }

        taken.UnionWith(shape.Children.Select(static c => c.Name));
        taken.UnionWith(shape.ValueCollections.Select(static c => c.Name));

        return taken;
    }

    // The rows of another entity that point at this one, one collection per navigation that points here:
    // Distribution.FromFuelingId gives FuelingRead.DistributionsFrom — the referencing type's plural, then whatever
    // the id says before the target's name. A name the target already uses, or that two navigations would both
    // claim, gets no collection and RASK108: the navigation it mirrors is still there, and a guessed second name is not.
    private static Dictionary<string, List<(string Name, string SourceReadType, string ForeignKey)>> Reverses(
        SourceProductionContext context,
        List<(ReadShape Shape, List<ReadNavigation> Navigations)> mappings,
        Dictionary<string, List<(string RootReadType, string Name)>> inverses)
    {
        var faces = mappings.ToDictionary(static m => m.Shape.FullyQualifiedName, StringComparer.Ordinal);
        var proposed = new List<(ReadShape Source, string ForeignKey, ReadShape Target, string Name)>();

        foreach (var (shape, navigations) in mappings)
        {
            foreach (var navigation in navigations)
            {
                var target = faces[navigation.TargetReadType].Shape;

                proposed.Add((shape, navigation.ForeignKey, target, ModelRegistryGenerator.SetNameOf(shape.SourceName) + Prefix(navigation.Name, target.SourceName)));
            }
        }

        var reverses = new Dictionary<string, List<(string Name, string SourceReadType, string ForeignKey)>>(StringComparer.Ordinal);

        foreach (var (source, foreignKey, target, name) in proposed)
        {
            var face = faces[target.FullyQualifiedName];
            if ((Clash(target, face.Navigations, inverses, name) ?? Rival(proposed, source, foreignKey, target, name)) is { } clash)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask108, source.Location?.ToLocation(), target.Name, name, clash.Taken, clash.Rename, source.SourceName, foreignKey));
                continue;
            }

            var navigations = faces[source.FullyQualifiedName].Navigations;
            var index = navigations.FindIndex(n => string.Equals(n.ForeignKey, foreignKey, StringComparison.Ordinal));
            navigations[index] = navigations[index] with { Inverse = name };

            if (!reverses.TryGetValue(target.FullyQualifiedName, out var list))
            {
                reverses[target.FullyQualifiedName] = list = [];
            }

            list.Add((name, source.FullyQualifiedName, foreignKey));
        }

        return reverses;
    }

    // What the collection's name takes after the plural: the navigation's words before its target's name (FromFueling ->
    // From). A configured navigation need not end in its target's: one named by its id after the target's last word
    // (ForeignTaxProductCategory, to a ForeignTaxCategory) adds nothing, and one named after nothing of it (Auditor, to
    // a Payee) adds its whole name, so it never takes the plain plural from the id that does point by name.
    private static string Prefix(string navigation, string target)
    {
        if (navigation.EndsWith(target, StringComparison.Ordinal))
        {
            return navigation.Substring(0, navigation.Length - target.Length);
        }

        return navigation.EndsWith(ConfiguredReferences.LastWord(target), StringComparison.Ordinal) ? string.Empty : navigation;
    }

    // What the target's face already calls this name, and what to rename instead of the id — or null when it is free.
    private static (string Taken, string Rename)? Clash(
        ReadShape target,
        List<ReadNavigation> navigations,
        Dictionary<string, List<(string RootReadType, string Name)>> inverses,
        string name)
    {
        if (target.Members.Any(m => string.Equals(m.Name, name, StringComparison.Ordinal)))
        {
            return ($"a column of {target.SourceName}", "column");
        }

        if (navigations.Exists(n => string.Equals(n.Name, name, StringComparison.Ordinal)) ||
            (inverses.TryGetValue(target.FullyQualifiedName, out var roots) &&
             roots.Exists(r => string.Equals(r.Name, name, StringComparison.Ordinal))))
        {
            return ($"a navigation of {target.Name}", "navigation's id");
        }

        if (target.Children.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)) ||
            target.ValueCollections.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
        {
            return ($"a collection of {target.SourceName}", "collection");
        }

        return null;
    }

    // Another id that would give the same target the same collection name.
    private static (string Taken, string Rename)? Rival(
        List<(ReadShape Source, string ForeignKey, ReadShape Target, string Name)> proposed,
        ReadShape source,
        string foreignKey,
        ReadShape target,
        string name)
    {
        foreach (var other in proposed)
        {
            if (ReferenceEquals(other.Target, target) && string.Equals(other.Name, name, StringComparison.Ordinal) &&
                !(ReferenceEquals(other.Source, source) && string.Equals(other.ForeignKey, foreignKey, StringComparison.Ordinal)))
            {
                return ($"the collection for {other.Source.SourceName}.{other.ForeignKey}", "other id");
            }
        }

        return null;
    }

    // Exact before suffix, and an aggregate before a child within each: a VirtualTankId reaches the VirtualTank child
    // even though it also ends in an aggregate's name, Tank. The first rule whose name matches settles it.
    private static (ReadNavigation? Navigation, ReadModelShape.ReadReferenceProblem Problem) Infer(
        ReadReference reference,
        string owner,
        IReadOnlyDictionary<string, ReadModelShape.ReadTarget> aggregates,
        IReadOnlyDictionary<string, ReadModelShape.ReadTarget> children)
    {
        foreach (var exact in new[] { true, false })
        {
            foreach (var targets in new[] { aggregates, children })
            {
                if (ReadModelShape.TryResolve(reference, owner, targets, exact, out var navigation, out var problem))
                {
                    return (navigation, problem);
                }
            }
        }

        return (null, ReadModelShape.ReadReferenceProblem.None);
    }

    private static string EmitFace(
        ReadShape shape,
        List<ReadNavigation> navigations,
        Dictionary<string, List<(string RootReadType, string Name)>> inverses,
        Dictionary<string, List<(string Name, string SourceReadType, string ForeignKey)>> reverses)
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
        s.AppendLine("/// there is nothing here to change or save. Query it with <c>" + shape.SourceName + ".Where(…)</c>.");
        s.AppendLine("/// </remarks>");
        s.Append(shape.Accessibility).Append(" sealed class ").Append(shape.Name)
            .AppendLine(" : global::Rask.Data.IReadModel");
        s.AppendLine("{");
        AppendFaceMembers(s, shape, navigations, inverses);

        if (reverses.TryGetValue(shape.FullyQualifiedName, out var pointing))
        {
            AppendReverses(s, pointing);
        }

        s.AppendLine("}");
        s.AppendLine();
        AppendReadAccess(s, shape);

        return s.ToString();
    }

    private static void AppendFaceMembers(
        StringBuilder s,
        ReadShape shape,
        List<ReadNavigation> navigations,
        Dictionary<string, List<(string RootReadType, string Name)>> inverses)
    {
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

        if (inverses.TryGetValue(shape.FullyQualifiedName, out var roots))
        {
            // Under one root it is always there; under several, only the one the row belongs to is.
            var several = roots.Count > 1;

            foreach (var (rootReadType, name) in roots)
            {
                s.AppendLine(several
                    ? "    /// <summary>The aggregate this is part of, when it is part of this one.</summary>"
                    : "    /// <summary>The aggregate this is part of.</summary>");
                s.Append("    public ").Append(rootReadType).Append(several ? "? " : " ").Append(name)
                    .AppendLine(several ? " { get; init; }" : " { get; init; } = null!;");
                s.AppendLine();
            }
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
    }

    private static void AppendReverses(StringBuilder s, List<(string Name, string SourceReadType, string ForeignKey)> reverses)
    {
        foreach (var (name, source, foreignKey) in reverses)
        {
            s.Append("    /// <summary>The <see cref=\"").Append(source).Append("\" /> rows whose <c>").Append(foreignKey)
                .AppendLine("</c> is this one.</summary>");
            s.Append("    public global::System.Collections.Generic.IReadOnlyList<").Append(source).Append("> ").Append(name)
                .Append(" { get; init; } = new global::System.Collections.Generic.List<").Append(source).AppendLine(">();");
            s.AppendLine();
        }
    }

    private static void AppendReadAccess(StringBuilder s, ReadShape shape)
    {
        s.Append("/// <summary>Queries <see cref=\"").Append(shape.SourceTypeName)
            .Append("\" /> through its read face, <see cref=\"").Append(shape.FullyQualifiedName).AppendLine("\" />.</summary>");
        // Every terminal ends in a defaulted CancellationToken, so Count() beside Count(predicate) trips
        // RS0026 by construction; .editorconfig turns it off for the same reason, but its [*.cs] never matches a
        // generated tree.
        s.AppendLine("#pragma warning disable RS0026");
        s.Append(shape.Accessibility).Append(" static class ").Append(shape.SourceName).AppendLine("ReadAccess");
        s.AppendLine("{");
        s.Append("    extension(").Append(shape.SourceTypeName).AppendLine(")");
        s.AppendLine("    {");

        var read = shape.FullyQualifiedName;
        var query = Query + "<" + read + ">";
        var open = "global::Rask.Data.GeneratedReadQuery.Of<" + read + ">()";

        s.AppendLine("        /// <summary>Every row, as a query that has not run yet: <c>await Product.All</c>.</summary>");
        s.Append("        public static ").Append(query).Append(" All => ").Append(open).AppendLine(";");
        s.AppendLine();

        foreach (var (summary, signature, call) in Forwarders)
        {
            s.Append("        /// <summary>").Append(summary).AppendLine("</summary>");
            s.Append("        public static ").Append(signature.Replace("{Q}", query).Replace("{R}", read))
                .Append(" =>").AppendLine();
            s.Append("            ").Append(open).Append('.').Append(call).AppendLine(";");
            s.AppendLine();
        }

        s.AppendLine("    }");
        s.AppendLine("}");
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
            AppendMapping(s, shape, navigations);
        }

        s.AppendLine("    ];");
        s.AppendLine("}");

        return s.ToString();
    }

    private static void AppendMapping(StringBuilder s, ReadShape shape, List<ReadNavigation> navigations)
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
                .Append(Literal(navigation.ForeignKey))
                .Append(navigation.Inverse is null ? string.Empty : ", " + Literal(navigation.Inverse))
                .AppendLine("),");
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

    // Roslyn's own escaping: a table or column name can come from the user's mapping attributes, and a hand escape
    // that knew only \ and " let a line break end the literal (CS1010).
    private static string Literal(string value) =>
        Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    // One file per face, named after the type it reads so a stack trace or a "go to generated" lands
    // somewhere recognisable. The global:: prefix and the dots go, because a hint name is a file name.
    private static string HintName(ReadShape shape) =>
        shape.FullyQualifiedName.Substring("global::".Length).Replace('.', '_') + ".g.cs";
}
