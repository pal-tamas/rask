using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Batteries.Generators;

/// <summary>
/// Builds the EF Core model from the <c>Rask.Data.Entity&lt;TId&gt;</c> types in the compilation, so an app declares
/// aggregates and nothing else — no <c>DbContext</c>, no <c>DbSet</c> property, no
/// <c>IEntityTypeConfiguration</c> class, no registration.
/// </summary>
/// <remarks>
/// <para>
/// Emits a per-assembly <c>[ModuleInitializer]</c> that contributes to <c>Rask.Data.ModelRegistry</c>:
/// each entity is mapped, every value-object property becomes a complex property (no marker — see
/// <c>AggregateShape.IsValueObjectType</c>), its strongly-typed id gets a value converter, and its own static
/// <c>Configure</c> is called last.
/// </para>
/// <para>
/// Generated calls, never reflection. An assembly scan would be removed or mis-answered by the trimmer,
/// and the symptom is a missing table or an unconverted key with a green build.
/// </para>
/// </remarks>
[Generator]
public sealed class ModelRegistryGenerator : IIncrementalGenerator
{
    private const string BuilderType = "EntityTypeBuilder";
    private const string BuilderNamespace = "Microsoft.EntityFrameworkCore.Metadata.Builders";

    private static readonly DiagnosticDescriptor Rask072 = new(
        "RASK072",
        "Entity Configure method will not be called",
        "'{0}.Configure' {1}, so Rask cannot call it and this entity's mapping rules never reach the "
        + "model; declare it as 'public static void Configure(EntityTypeBuilder<{0}> builder)'",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Rask maps every entity it finds and calls a static Configure on it for the rules that "
                     + "are that entity's own. The method is matched by signature, so one that is an instance "
                     + "method, is private, or takes something other than EntityTypeBuilder<TSelf> is simply "
                     + "not found. Without this warning the build stays green, the table is created from "
                     + "conventions alone, and the missing index or length is discovered in production.",
        helpLinkUri: DiagnosticHelp.Link("RASK072"));

    private static readonly DiagnosticDescriptor Rask073 = new(
        "RASK073",
        "Strongly-typed id has no usable value",
        "'{0}' is used as an entity's id but {1}, so Rask cannot build a value converter for it and EF "
        + "Core will refuse the model; give it a single public property (or field) holding the stored "
        + "value and a constructor taking that value",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A strongly-typed id is converted to its underlying value by a generated converter, "
                     + "which needs two things it can see: one property to read the value from, and one "
                     + "constructor to put it back. Reported here rather than left to EF Core, whose own "
                     + "message names the property and not the reason.",
        helpLinkUri: DiagnosticHelp.Link("RASK073"));

    private static readonly DiagnosticDescriptor Rask090 = new(
        "RASK090",
        "DbContext set name cannot be generated",
        "'db.{1}' is not generated for '{0}' because {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Every mapped entity gets a named set on DbContext — db.Orders beside db.Set<Order>() — "
                     + "from one documented rule, because an irregular guess is worse than a predictable one. "
                     + "A name two entities land on, or one DbContext already declares, is the case that rule "
                     + "cannot serve: renaming quietly would leave an accessor nobody could predict, and "
                     + "emitting it anyway would give a name that means something other than it says. Rask "
                     + "generates neither and says so; Set<T>() is unambiguous and still there.",
        helpLinkUri: DiagnosticHelp.Link("RASK090"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, _) => GetCandidate(ctx))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);

        // `RaskReadFacesOnly` assemblies map their own tables by hand (Rask.Auth's AddRaskAuth does), so a
        // contribution from here would map them a SECOND time — and into every app that merely references
        // the package, auth switched off included, because the contribution is registered by a module
        // initializer that runs when the assembly loads.
        context.RegisterSourceOutput(
            candidates.Collect().Combine(ReadFacesOnly.Of(context)),
            static (spc, pair) =>
            {
                if (!pair.Right)
                {
                    Emit(spc, pair.Left);
                }

                // Always: this records what a type asked for and maps nothing, so it is as safe in a
                // read-faces-only package as anywhere — and an append-only battery table is exactly the
                // case it exists for.
                EmitConventions(spc, pair.Left);
            });
    }

    private static Candidate? GetCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax declaration ||
            ctx.SemanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        // An abstract entity is a base in somebody's hierarchy, not a table. An open generic cannot be
        // mapped as itself, and a closed one has no declaration site to be named at — both skipped
        // silently, because a shared generic base is an ordinary way to factor columns out.
        if (!AggregateShape.IsMappedEntity(symbol) || !AggregateShape.TryGetIdType(symbol, out var idType))
        {
            return null;
        }

        var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var (configures, configureProblem) = FindConfigure(symbol, name);

        return new Candidate(
            name,
            symbol.Name,
            symbol.ContainingNamespace.IsGlobalNamespace
                ? ""
                : symbol.ContainingNamespace.ToDisplayString(),
            symbol.DeclaredAccessibility == Accessibility.Public,
            configures,
            configureProblem,
            SymbolLocation.From(symbol),
            ValueObjectPaths(symbol),
            StronglyTypedId.For(idType),
            ConstOf(symbol, "Stamps", "Timestamps"),
            ConstOf(symbol, "Deletes", "Deletion"),
            ConstOf(symbol, "Checks", "Concurrency"),
            ConstOf(symbol, "Broadcast", "Broadcasts"),
            ValueCollections(symbol),
            ConstOf(symbol, "Scope", "Tenancy"),
            ChildTypeNames(symbol));
    }

    // The child entities this aggregate holds. Needed because tenancy is declared on the ROOT and a child
    // has to take its root's answer: a child is part of that aggregate, so it belongs to whichever tenant the
    // root does, and it carries its own TenantId because its own read face is queryable on its own.
    private static EquatableArray<string> ChildTypeNames(INamedTypeSymbol entity)
    {
        var names = new List<string>();

        foreach (var member in entity.GetMembers().OfType<IPropertySymbol>())
        {
            if (GeneratedModelShape.DescribeChild(entity, member) is { } child)
            {
                names.Add(child.ChildType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }
        }

        return new EquatableArray<string>([.. names]);
    }

    // Every collection of values the entity holds, read here rather than at runtime: deciding whether an
    // element type is a value object needs the same rule the rest of the generator applies, and a second
    // implementation over reflection would be free to disagree with it.
    private static EquatableArray<ValueCollectionSpec> ValueCollections(INamedTypeSymbol entity)
    {
        var collections = GeneratedModelShape.ValueCollectionsOf(entity)
            .Select(static c => new ValueCollectionSpec(
                c.Name,
                c.Field?.Name,
                c.ValueObject?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            .ToArray();

        return new EquatableArray<ValueCollectionSpec>(collections);
    }

    // Every path from the entity down to a value-object property, so nested value objects are mapped all
    // the way. Depth-limited and cycle-guarded: a value object referring to its own type would otherwise
    // walk forever, and a deep graph is a modelling mistake rather than something to support silently.
    //
    // Each path remembers the type at every step, so the registry can drop a path whose type turns out to be a
    // strongly-typed id another entity is keyed by (that one is a converted column, not a complex type), and the
    // single stored property of a one-value type, whose column takes the property's own name.
    private static EquatableArray<ValueObjectPath> ValueObjectPaths(INamedTypeSymbol entity)
    {
        var paths = new List<ValueObjectPath>();
        Walk(entity, [], [], []);
        return new EquatableArray<ValueObjectPath>([.. paths]);

        void Walk(ITypeSymbol owner, ImmutableArray<string> prefix, ImmutableArray<string> types, ImmutableHashSet<string> seen)
        {
            if (prefix.Length >= AggregateShape.MaxValueObjectDepth)
            {
                return;
            }

            var properties = owner is INamedTypeSymbol named && prefix.Length > 0
                ? AggregateShape.StoredProperties(named)
                : owner.GetMembers().OfType<IPropertySymbol>().Where(static p =>
                    !p.IsStatic && !p.IsIndexer && p.GetMethod is not null && p.DeclaredAccessibility == Accessibility.Public);

            foreach (var property in properties)
            {
                if (property.Type is not INamedTypeSymbol type ||
                    !AggregateShape.IsValueObjectType(type) ||
                    property.GetAttributes().Any(static a =>
                        a.AttributeClass?.ToDisplayString() == "System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute"))
                {
                    continue;
                }

                var typeName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (seen.Contains(typeName))
                {
                    continue;
                }

                var stored = AggregateShape.StoredProperties(type).ToList();
                var singleValue = stored.Count == 1 && !AggregateShape.IsValueObjectType(stored[0].Type)
                    ? stored[0].Name
                    : null;

                var path = prefix.Add(property.Name);
                var pathTypes = types.Add(typeName);
                paths.Add(new ValueObjectPath(
                    new EquatableArray<string>([.. path]), new EquatableArray<string>([.. pathTypes]), singleValue));
                Walk(type, path, pathTypes, seen.Add(typeName));
            }
        }
    }

    // Looks for `public static void Configure(EntityTypeBuilder<TSelf>)`. Returns why a near-miss does
    // not match, so the build can say so rather than mapping by convention alone and looking fine.
    private static (bool Matches, string? Problem) FindConfigure(INamedTypeSymbol symbol, string fullyQualifiedName)
    {
        var named = symbol.GetMembers("Configure").OfType<IMethodSymbol>().ToList();
        if (named.Count == 0)
        {
            return (false, null);
        }

        if (named.Any(m => IsConfigure(m, fullyQualifiedName)))
        {
            return (true, null);
        }

        // The closest miss, reported as one reason, so the message names a single thing to change.
        var candidate = named[0];

        if (!candidate.IsStatic)
        {
            return (false, "is an instance method");
        }

        if (candidate.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected
            or Accessibility.ProtectedAndInternal)
        {
            return (false, "is not accessible from the generated model builder");
        }

        if (candidate.Parameters.Length != 1)
        {
            return (false, $"takes {candidate.Parameters.Length} parameters");
        }

        if (!candidate.ReturnsVoid)
        {
            return (false, "does not return void");
        }

        return (false, $"takes {candidate.Parameters[0].Type.ToDisplayString()} rather than the entity's builder");
    }

    private static bool IsConfigure(IMethodSymbol method, string fullyQualifiedName) =>
        method is
        {
            IsStatic: true,
            ReturnsVoid: true,
            IsGenericMethod: false,
            Parameters.Length: 1,
            DeclaredAccessibility: Accessibility.Public or Accessibility.Internal,
        }
        && method.Parameters[0].Type is INamedTypeSymbol { Name: BuilderType, TypeArguments.Length: 1 } builder
        && builder.ContainingNamespace?.ToDisplayString() == BuilderNamespace
        && builder.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == fullyQualifiedName;

    private static void Emit(SourceProductionContext context, ImmutableArray<Candidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        // Distinct because a partial class contributes one candidate per declaration, and ordered so the
        // generated file is stable across builds — an unstable one churns the incremental cache and every
        // diff that touches an entity.
        var entities = candidates
            .Distinct()
            .OrderBy(static c => c.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();

        foreach (var entity in entities)
        {
            if (entity.ConfigureProblem is { } problem)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask072, entity.Location?.ToLocation(), entity.SimpleName, problem));
            }

            if (entity.Id is { Problem: { } idProblem, TypeName: { } idTypeName })
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask073, entity.Location?.ToLocation(), idTypeName, idProblem));
            }
        }

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Rask.Data.Generated;");
        source.AppendLine();
        source.AppendLine("/// <summary>This assembly's contribution to the Rask entity model.</summary>");
        source.AppendLine("file static class __RaskModelRegistry");
        source.AppendLine("{");
        source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("    internal static void Initialize() =>");
        source.AppendLine("        global::Rask.Data.ModelRegistry.Replace(");
        source.AppendLine("            typeof(__RaskModelRegistry), MapEntities, ApplyConfigurations, ConfigureConventions);");
        source.AppendLine();

        // A strongly-typed id another entity is keyed by is a converted column wherever it appears (a foreign key
        // to Product is a ProductId too), never a complex type.
        var idTypes = new HashSet<string>(
            entities.Select(static e => e.Id.TypeName).Where(static t => t is not null).Select(static t => t!),
            StringComparer.Ordinal);

        EmitMapEntities(source, entities, idTypes);
        EmitConfigureConventions(source, entities);
        EmitApplyConfigurations(source, entities);

        source.AppendLine("}");

        context.AddSource("__RaskModelRegistry.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));

        EmitDbSets(context, entities);
    }

    /// <summary>
    ///     A named set on <c>DbContext</c> for every mapped entity — <c>db.Orders</c> beside the
    ///     <c>db.Set&lt;Order&gt;()</c> that still works.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Extension members, not properties on a generated context, so an application that brings its own
    ///         DbContext gets them too and nothing has to be <c>partial</c> — the same way <c>Order.Read</c>
    ///         reaches the aggregate.
    ///     </para>
    ///     <para>
    ///         Emitted into the entity's own namespace, which is where code that mentions the entity is already
    ///         looking. One class per namespace, so two namespaces' entities never share a declaration.
    ///     </para>
    /// </remarks>
    private static void EmitDbSets(SourceProductionContext context, List<Candidate> entities)
    {
        // The name is what makes an accessor, so a name two entities claim makes none. Grouped over the whole
        // compilation rather than per namespace: the extension member is reached by name once its namespace is
        // imported, so two namespaces colliding is exactly as ambiguous as one.
        var byName = new Dictionary<string, List<Candidate>>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            var name = SetNameOf(entity.SimpleName);
            if (!byName.TryGetValue(name, out var claimants))
            {
                byName[name] = claimants = [];
            }

            claimants.Add(entity);
        }

        var named = new List<(Candidate Entity, string Name)>();
        foreach (var pair in byName.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            var (name, claimants) = (pair.Key, pair.Value);

            if (claimants.Count == 1 && !IsDbContextMember(name))
            {
                named.Add((claimants[0], name));
                continue;
            }

            // A name DbContext already declares would lose silently — a member on the type itself wins over an
            // extension member — so it is refused for the same reason a clash between two entities is.
            if (claimants.Count == 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rask090,
                    claimants[0].Location?.ToLocation(),
                    claimants[0].SimpleName,
                    name,
                    $"DbContext already declares '{name}', and a member on the type itself always wins over an "
                    + $"extension member; reach it as 'db.Set<{claimants[0].SimpleName}>()'"));
                continue;
            }

            foreach (var claimant in claimants)
            {
                var others = string.Join(
                    ", ",
                    claimants.Where(c => !ReferenceEquals(c, claimant)).Select(static c => "'" + c.SimpleName + "'"));

                context.ReportDiagnostic(Diagnostic.Create(
                    Rask090,
                    claimant.Location?.ToLocation(),
                    claimant.SimpleName,
                    name,
                    $"{others} want it too, and an accessor that could mean either is worse than none; rename "
                    + $"one of them, or reach this one as 'db.Set<{claimant.SimpleName}>()'"));
            }
        }

        foreach (var group in named.GroupBy(static e => e.Entity.Namespace, StringComparer.Ordinal)
                     .OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            var source = new StringBuilder();
            source.AppendLine("// <auto-generated/>");
            source.AppendLine("#nullable enable");
            source.AppendLine();

            if (group.Key.Length > 0)
            {
                source.Append("namespace ").Append(group.Key).AppendLine(";");
                source.AppendLine();
            }

            source.AppendLine("/// <summary>The named sets of this namespace's entities.</summary>");
            source.AppendLine("/// <remarks>");
            source.AppendLine("/// On <c>DbContext</c> itself, so an application's own context has them without");
            source.AppendLine("/// being partial. A set resolves for the context that maps the entity — the same");
            source.AppendLine("/// rule <c>Set&lt;T&gt;()</c> follows, because that is all this is.");
            source.AppendLine("/// </remarks>");
            source.AppendLine("public static class __RaskDbSets");
            source.AppendLine("{");
            source.AppendLine("    extension(global::Microsoft.EntityFrameworkCore.DbContext db)");
            source.AppendLine("    {");

            var first = true;
            foreach (var (entity, name) in group.OrderBy(static e => e.Name, StringComparer.Ordinal))
            {
                if (!first)
                {
                    source.AppendLine();
                }

                first = false;

                source.Append("        /// <summary>The <see cref=\"").Append(entity.FullyQualifiedName)
                    .AppendLine("\" /> table.</summary>");
                source.Append("        ").Append(entity.IsPublic ? "public" : "internal")
                    .Append(" global::Microsoft.EntityFrameworkCore.DbSet<").Append(entity.FullyQualifiedName)
                    .Append("> ").Append(name).Append(" => db.Set<").Append(entity.FullyQualifiedName)
                    .AppendLine(">();");
            }

            source.AppendLine("    }");
            source.AppendLine("}");

            var file = group.Key.Length > 0 ? "__RaskDbSets." + group.Key + ".g.cs" : "__RaskDbSets.g.cs";
            context.AddSource(file, SourceText.From(source.ToString(), Encoding.UTF8));
        }
    }

    /// <summary>
    ///     The documented pluralisation: <c>s</c>, <c>es</c> after s/x/z/ch/sh, and <c>y</c> to <c>ies</c>
    ///     after a consonant.
    /// </summary>
    /// <remarks>
    ///     Deliberately not clever. An irregular-plural dictionary would be right more often and wrong
    ///     unpredictably, and a name you cannot guess from the type is worse than one you can — a Person
    ///     becomes db.Persons here, and that is the trade.
    /// </remarks>
    internal static string SetNameOf(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        if (name.EndsWith("s", StringComparison.Ordinal) || name.EndsWith("x", StringComparison.Ordinal) ||
            name.EndsWith("z", StringComparison.Ordinal) || name.EndsWith("ch", StringComparison.Ordinal) ||
            name.EndsWith("sh", StringComparison.Ordinal))
        {
            return name + "es";
        }

        if (name.EndsWith("y", StringComparison.Ordinal) && name.Length > 1 && !IsVowel(name[name.Length - 2]))
        {
            return name.Substring(0, name.Length - 1) + "ies";
        }

        return name + "s";

        static bool IsVowel(char c) => "aeiouAEIOU".IndexOf(c) >= 0;
    }

    // What a DbContext already declares. An extension member never wins against one of these, so a set that
    // lands on the name would compile and quietly mean something else.
    private static bool IsDbContextMember(string name) => name is
        "Database" or "ChangeTracker" or "Model" or "ContextId" or "Add" or "AddAsync" or "AddRange" or
        "AddRangeAsync" or "Attach" or "AttachRange" or "Dispose" or "DisposeAsync" or "Entry" or "Find" or
        "FindAsync" or "GetService" or "Remove" or "RemoveRange" or "SaveChanges" or "SaveChangesAsync" or
        "Set" or "Update" or "UpdateRange" or "Equals" or "GetHashCode" or "GetType" or "ToString";

    /// <summary>
    ///     Registers every entity that narrowed a convention — <c>Stamps</c>, <c>Deletes</c>, <c>Checks</c> —
    ///     so they can be honoured without reflecting over a const the trimmer is free to drop.
    /// </summary>
    private static void EmitConventions(SourceProductionContext context, ImmutableArray<Candidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var distinct = candidates.Distinct().ToList();

        // A child's tenancy is its root's, always. Declared on the root because that is where the aggregate
        // boundary is; carried on the child because the child's own read face would otherwise be unfiltered.
        var scopeByType = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entity in distinct.Where(static c => c.Scope is not null))
        {
            scopeByType[entity.FullyQualifiedName] = entity.Scope!.Value;

            foreach (var child in entity.ChildTypeNames)
            {
                scopeByType[child] = entity.Scope!.Value;
            }
        }

        var declared = distinct
            .Where(c => c.Stamps is not null || c.Deletes is not null || c.Checks is not null ||
                        c.Broadcast is not null ||
                        c.Collections.Count > 0 || scopeByType.ContainsKey(c.FullyQualifiedName))
            .OrderBy(static c => c.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();

        if (declared.Count == 0)
        {
            return;
        }

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("namespace Rask.Data.Generated;");
        source.AppendLine();
        source.AppendLine("/// <summary>What this assembly's entities asked of the conventions.</summary>");
        source.AppendLine("file static class __RaskConventions");
        source.AppendLine("{");
        source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("    internal static void Initialize()");
        source.AppendLine("    {");

        foreach (var entity in declared)
        {
            Declare(source, entity.FullyQualifiedName, "Timestamps", entity.Stamps);
            Declare(
                source,
                entity.FullyQualifiedName,
                "Tenancy",
                scopeByType.TryGetValue(entity.FullyQualifiedName, out var scope) ? scope : null);
            Declare(source, entity.FullyQualifiedName, "Deletion", entity.Deletes);
            Declare(source, entity.FullyQualifiedName, "Concurrency", entity.Checks);
            Declare(source, entity.FullyQualifiedName, "Broadcasts", entity.Broadcast);

            foreach (var collection in entity.Collections)
            {
                source.Append("        global::Rask.Data.ConventionRegistry.DeclareCollection(typeof(")
                    .Append(entity.FullyQualifiedName).Append("), ").Append(Literal(collection.Property))
                    .Append(", ").Append(Literal(collection.Field)).Append(", ")
                    .Append(collection.ElementTypeName is { } element ? "typeof(" + element + ")" : "null")
                    .AppendLine(");");
            }
        }

        source.AppendLine("    }");
        source.AppendLine("}");

        context.AddSource("__RaskConventions.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));

        static string Literal(string? value) =>
            value is null ? "null" : "\"" + value + "\"";

        static void Declare(StringBuilder source, string entity, string enumName, int? value)
        {
            if (value is not { } declared)
            {
                return;
            }

            source.Append("        global::Rask.Data.ConventionRegistry.Declare(typeof(").Append(entity)
                .Append("), (global::Rask.Data.").Append(enumName).Append(')')
                .Append(declared.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        }
    }

    private static void EmitMapEntities(StringBuilder source, List<Candidate> entities, HashSet<string> idTypes)
    {
        source.AppendLine("    private static void MapEntities(global::Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)");
        source.AppendLine("    {");

        foreach (var entity in entities)
        {
            source.Append("        var ").Append(Local(entity)).Append(" = modelBuilder.Entity<")
                .Append(entity.FullyQualifiedName).AppendLine(">();");

            // Value objects become complex properties: part of the row, not a joined table.
            var paths = entity.ValueObjects.Where(p => !p.Types.Any(idTypes.Contains)).ToList();
            EmitComplexProperties(source, Local(entity), paths, depth: 0, indent: "        ");
        }

        source.AppendLine("    }");
        source.AppendLine();
    }

    // Nested value objects are configured through the lambda overload rather than by chaining onto a
    // re-entered builder: EF Core binds a complex type's constructor from the properties declared on the
    // builder it was given, so a nested property configured on a second builder for the same complex type
    // is not there when it binds, and model creation fails with "cannot bind" on the parameter.
    private static void EmitComplexProperties(
        StringBuilder source,
        string receiver,
        List<ValueObjectPath> paths,
        int depth,
        string indent)
    {
        var groups = paths
            .Where(p => p.Segments.Count > 0)
            .GroupBy(p => p.Segments[0], StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var self = group.First(p => p.Segments.Count == 1);
            var nested = group
                .Where(p => p.Segments.Count > 1)
                .Select(p => new ValueObjectPath(
                    new EquatableArray<string>(p.Segments.Skip(1)),
                    new EquatableArray<string>(p.Types.Skip(1)),
                    p.SingleValue,
                    p.ColumnPrefix is null ? group.Key : p.ColumnPrefix + "_" + group.Key))
                .ToList();

            source.Append(indent).Append(receiver).Append(".ComplexProperty(x => x.").Append(group.Key);

            // A one-value type is still a complex type, but its one column takes the property's name — `Email`,
            // not `Email_Value` — so the table reads as if the value were stored directly.
            if (nested.Count == 0 && self.SingleValue is { } value)
            {
                var column = self.ColumnPrefix is null ? group.Key : self.ColumnPrefix + "_" + group.Key;
                var single = "b" + depth;
                source.Append(", ").Append(single).Append(" => ").Append(single).Append(".Property(v => v.").Append(value)
                    .Append(").HasColumnName(\"").Append(column).AppendLine("\"));");
                continue;
            }

            if (nested.Count == 0)
            {
                source.AppendLine(");");
                continue;
            }

            var inner = "b" + depth;
            source.Append(", ").Append(inner).AppendLine(" =>").Append(indent).AppendLine("{");
            EmitComplexProperties(source, inner, nested, depth + 1, indent + "    ");
            source.Append(indent).AppendLine("});");
        }
    }

    private static void EmitConfigureConventions(StringBuilder source, List<Candidate> entities)
    {
        source.AppendLine("    private static void ConfigureConventions(");
        source.AppendLine("        global::Microsoft.EntityFrameworkCore.ModelConfigurationBuilder configurationBuilder)");
        source.AppendLine("    {");

        // One registration per id TYPE, not per entity: a foreign key to Product is a ProductId too, and
        // registering the conversion twice for the same type is an error rather than a no-op.
        var ids = entities
            .Select(static e => e.Id)
            .Where(static id => id is { Problem: null, TypeName: not null })
            .Select(static id => id!)
            .GroupBy(static id => id.TypeName!, StringComparer.Ordinal)
            .Select(static g => g.First())
            .OrderBy(static id => id.TypeName, StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
        {
            source.AppendLine("        // No entity uses a strongly-typed id.");
            source.AppendLine("        _ = configurationBuilder;");
        }
        else
        {
            foreach (var id in ids)
            {
                source.Append("        configurationBuilder.Properties<").Append(id.TypeName).AppendLine(">()");
                source.Append("            .HaveConversion<").Append(ConverterName(id)).AppendLine(">();");
            }
        }

        source.AppendLine("    }");
        source.AppendLine();

        foreach (var id in ids)
        {
            source.Append("    private sealed class ").Append(ConverterName(id))
                .Append(" : global::Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<")
                .Append(id.TypeName).Append(", ").Append(id.ValueTypeName).AppendLine(">");
            source.AppendLine("    {");
            source.Append("        public ").Append(ConverterName(id)).AppendLine("()");
            source.Append("            : base(static id => id.").Append(id.ValueMember)
                .Append(", static value => new ").Append(id.TypeName).AppendLine("(value))");
            source.AppendLine("        {");
            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine();
        }
    }

    private static void EmitApplyConfigurations(StringBuilder source, List<Candidate> entities)
    {
        source.AppendLine("    private static void ApplyConfigurations(global::Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)");
        source.AppendLine("    {");

        var configured = entities.Where(static e => e.Configures).ToList();
        if (configured.Count == 0)
        {
            source.AppendLine("        // No entity declares a static Configure.");
            source.AppendLine("        _ = modelBuilder;");
        }
        else
        {
            foreach (var entity in configured)
            {
                source.Append("        ").Append(entity.FullyQualifiedName)
                    .Append(".Configure(modelBuilder.Entity<").Append(entity.FullyQualifiedName).AppendLine(">());");
            }
        }

        source.AppendLine("    }");
    }

    // From the fully qualified name, not the simple one: two entities called Product in two namespaces
    // would otherwise declare the same local twice and the generated registry would not compile.
    private static string Local(Candidate entity) =>
        "__" + entity.FullyQualifiedName.Replace("global::", "").Replace('.', '_');

    private static string ConverterName(StronglyTypedId id) =>
        "__" + id.TypeName!.Split('.').Last().Replace("<", "").Replace(">", "") + "Converter";

    // Segments and Types run in step, root first. SingleValue names the one stored property of a one-value type;
    // ColumnPrefix is the owning path's column name once a path has been re-rooted under a nested builder.
    // A collection of values the entity holds. Symbols cannot cross a pipeline step, so the element travels
    // as its fully-qualified name, and null there means a primitive collection rather than a JSON one.
    private readonly record struct ValueCollectionSpec(string Property, string? Field, string? ElementTypeName);

    private readonly record struct ValueObjectPath(
        EquatableArray<string> Segments,
        EquatableArray<string> Types,
        string? SingleValue,
        string? ColumnPrefix = null);

    // Internal so the model generator's Create asks "is this a strongly-typed id, and over what" of the same
    // definition the registry registers the value converter from.
    internal readonly record struct StronglyTypedId(string? TypeName, string? ValueTypeName, string? ValueMember, string? Problem)
    {
        // A strongly-typed id is a user-defined type with one public value and a constructor that takes
        // it. Anything the BCL already maps (Guid, int, string, …) is not one and needs no converter.
        public static StronglyTypedId For(ITypeSymbol? idType)
        {
            if (idType is not INamedTypeSymbol named || IsBuiltIn(named))
            {
                return default;
            }

            var typeName = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var values = named.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(static p => !p.IsStatic && !p.IsIndexer && p.GetMethod is not null &&
                                   p.DeclaredAccessibility == Accessibility.Public)
                .ToList();

            if (values.Count != 1)
            {
                return new StronglyTypedId(typeName, null, null,
                    values.Count == 0 ? "has no public property to read its value from"
                        : $"has {values.Count} public properties, so which one is the stored value is ambiguous");
            }

            var value = values[0];
            var valueTypeName = value.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            var hasCtor = named.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public &&
                c.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, value.Type));

            return hasCtor
                ? new StronglyTypedId(typeName, valueTypeName, value.Name, null)
                : new StronglyTypedId(typeName, valueTypeName, value.Name,
                    $"has no public constructor taking a {value.Type.ToDisplayString()}");
        }

        private static bool IsBuiltIn(INamedTypeSymbol type) =>
            type.SpecialType != SpecialType.None ||
            type.ContainingNamespace?.ToDisplayString() is "System";
    }

    /// <summary>
    ///     What a <c>public const Timestamps Stamps</c> says, or <c>null</c> when the entity does not say.
    /// </summary>
    /// <remarks>
    ///     Read here, at compile time, because a const is inlined at every use site and the field itself can
    ///     be trimmed — a runtime reflection read would find nothing in a trimmed publish and quietly fall
    ///     back to All, so an app would carry different columns in debug and in release.
    /// </remarks>
    private static int? ConstOf(INamedTypeSymbol symbol, string name, string enumName)
    {
        foreach (var field in symbol.GetMembers(name).OfType<IFieldSymbol>())
        {
            if (field is { IsConst: true, ConstantValue: int value } &&
                field.Type is { ContainingNamespace: { Name: "Data", ContainingNamespace: { Name: "Rask", ContainingNamespace.IsGlobalNamespace: true } } } type &&
                type.Name == enumName)
            {
                return value;
            }
        }

        return null;
    }

    private sealed record Candidate(
        string FullyQualifiedName,
        string SimpleName,
        string Namespace,
        bool IsPublic,
        bool Configures,
        string? ConfigureProblem,
        SymbolLocation? Location,
        EquatableArray<ValueObjectPath> ValueObjects,
        StronglyTypedId Id,
        int? Stamps,
        int? Deletes,
        int? Checks,
        int? Broadcast,
        EquatableArray<ValueCollectionSpec> Collections,
        int? Scope,
        EquatableArray<string> ChildTypeNames);
}
