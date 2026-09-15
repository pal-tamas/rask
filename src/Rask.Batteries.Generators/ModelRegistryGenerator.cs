using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Cqrs.Generators;
using Rask.Generators.Shared;

namespace Rask.Data.Generators;

/// <summary>
/// Builds the EF Core model from the <c>Rask.Data.Model</c> types in the compilation, so an app declares
/// entities and nothing else — no <c>DbContext</c>, no <c>DbSet</c> property, no
/// <c>IEntityTypeConfiguration</c> class, no registration.
/// </summary>
/// <remarks>
/// <para>
/// Emits a per-assembly <c>[ModuleInitializer]</c> that contributes to <c>Rask.Data.ModelRegistry</c>:
/// each entity is mapped, its <c>IValueObject</c> properties become complex properties, its
/// strongly-typed id gets a value converter, and its own static <c>Configure</c> is called last.
/// </para>
/// <para>
/// Generated calls, never reflection. An assembly scan would be removed or mis-answered by the trimmer,
/// and the symptom is a missing table or an unconverted key with a green build.
/// </para>
/// </remarks>
[Generator]
public sealed class ModelRegistryGenerator : IIncrementalGenerator
{
    private const string ModelBase = "Model";
    private const string RaskDataNamespace = "Rask.Data";
    private const string ValueObjectInterface = "Rask.Data.IValueObject";
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

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList.Types.Count: > 0 },
                static (ctx, _) => GetCandidate(ctx))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!);

        context.RegisterSourceOutput(candidates.Collect(), static (spc, all) => Emit(spc, all));
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
        if (symbol.IsAbstract || symbol.IsStatic || symbol.IsGenericType || !TryGetIdType(symbol, out var idType))
        {
            return null;
        }

        var name = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var (configures, configureProblem) = FindConfigure(symbol, name);

        return new Candidate(
            name,
            symbol.Name,
            configures,
            configureProblem,
            SymbolLocation.From(symbol),
            ValueObjectPaths(symbol),
            StronglyTypedId.For(idType));
    }

    // Walks to Model<TId>, handing back TId. Returns false for a class that is not an entity at all.
    // Defined in GeneratedModelShape, so "what is an entity" has one answer in the registry, the model
    // generator, and every generator that reconstructs a generated model it cannot see.
    internal static bool TryGetIdType(INamedTypeSymbol symbol, out ITypeSymbol? idType) =>
        GeneratedModelShape.TryGetIdType(symbol, out idType);

    // Every path from the entity down to a value-object property, so nested value objects are mapped all
    // the way. Depth-limited and cycle-guarded: a value object referring to its own type would otherwise
    // walk forever, and a deep graph is a modelling mistake rather than something to support silently.
    private static EquatableArray<ValueObjectPath> ValueObjectPaths(INamedTypeSymbol entity)
    {
        var paths = new List<ValueObjectPath>();
        Walk(entity, [], []);
        return new EquatableArray<ValueObjectPath>([.. paths]);

        void Walk(ITypeSymbol owner, ImmutableArray<string> prefix, ImmutableHashSet<string> seen)
        {
            if (prefix.Length >= 4)
            {
                return;
            }

            foreach (var property in owner.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsStatic || property.IsIndexer || property.GetMethod is null ||
                    property.DeclaredAccessibility != Accessibility.Public ||
                    property.Type is not INamedTypeSymbol type ||
                    !Implements(type, ValueObjectInterface))
                {
                    continue;
                }

                var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (seen.Contains(typeName))
                {
                    continue;
                }

                var path = prefix.Add(property.Name);
                paths.Add(new ValueObjectPath(new EquatableArray<string>([.. path])));
                Walk(type, path, seen.Add(typeName));
            }
        }
    }

    private static bool Implements(ITypeSymbol type, string fullyQualifiedInterface) =>
        type.AllInterfaces.Any(i => i.ToDisplayString() == fullyQualifiedInterface);

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
        source.AppendLine("internal static class __RaskModelRegistry");
        source.AppendLine("{");
        source.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("    internal static void Initialize() =>");
        source.AppendLine("        global::Rask.Data.ModelRegistry.Replace(");
        source.AppendLine("            typeof(__RaskModelRegistry), MapEntities, ApplyConfigurations, ConfigureConventions);");
        source.AppendLine();

        EmitMapEntities(source, entities);
        EmitConfigureConventions(source, entities);
        EmitApplyConfigurations(source, entities);

        source.AppendLine("}");

        context.AddSource("__RaskModelRegistry.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void EmitMapEntities(StringBuilder source, List<Candidate> entities)
    {
        source.AppendLine("    private static void MapEntities(global::Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)");
        source.AppendLine("    {");

        foreach (var entity in entities)
        {
            source.Append("        var ").Append(Local(entity)).Append(" = modelBuilder.Entity<")
                .Append(entity.FullyQualifiedName).AppendLine(">();");

            // Value objects become complex properties: part of the row, not a joined table.
            EmitComplexProperties(source, Local(entity), [.. entity.ValueObjects], depth: 0, indent: "        ");
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
            var nested = group
                .Where(p => p.Segments.Count > 1)
                .Select(p => new ValueObjectPath(new EquatableArray<string>(p.Segments.Skip(1))))
                .ToList();

            source.Append(indent).Append(receiver).Append(".ComplexProperty(x => x.").Append(group.Key);

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

    private readonly record struct ValueObjectPath(EquatableArray<string> Segments);

    // Internal so the model generator's CreateAsync asks "is this a strongly-typed id, and over what" of the same
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

    private sealed record Candidate(
        string FullyQualifiedName,
        string SimpleName,
        bool Configures,
        string? ConfigureProblem,
        SymbolLocation? Location,
        EquatableArray<ValueObjectPath> ValueObjects,
        StronglyTypedId Id);
}
