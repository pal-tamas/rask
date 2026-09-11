using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.External.PackageIslands;
using Rask.Generators.Shared;

namespace Rask.Generators.External;

/// <summary>
///     Completes a component deriving from <c>ReactComponent</c> or <c>LitComponent</c>, writing its
///     props as JSON with no reflection.
/// </summary>
/// <remarks>
///     <para>
///         Almost everything that makes a component external is hand-written on
///         <c>ExternalComponent</c> itself — the host element, the opaque-subtree boundary, the slot
///         grouping, the hydration property, the runtime <c>&lt;script&gt;</c> and the attribute
///         writer. Only three things need generating, because only the compiler knows them: the
///         component's name, the module beside it, and a writer for its declared props.
///     </para>
///     <para>
///         The runtime comes from the base class rather than from an attribute argument, so it cannot
///         disagree with what actually mounts, and Lit no longer has to name itself twice.
///     </para>
///     <para>
///         A <em>package island</em> — one whose constant <c>Module</c> names an npm package rather than a
///         file beside it — also gets its props from the committed <c>{Name}.props.json</c> snapshot
///         beside it: the properties are declared here, their chain steps by the factory generator, and
///         both read <see cref="PackageIslandProps" /> so they cannot disagree.
///     </para>
///     <para>
///         Only the current assembly is walked, which is correct rather than a limitation: the partial
///         has to be generated in the compilation that declares the class, so a component library
///         holding these generates its own.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ExternalGenerator : IIncrementalGenerator
{
    private const string SkipFactoryName = "Rask.Core.SkipFactoryAttribute";

    // RASK057 ("declares its own Render") is retired. ExternalComponent seals Render(), so writing
    // one is now CS0239 from the compiler itself — a rule the type system can state does not need an
    // analyzer to notice it.

    private static readonly DiagnosticDescriptor Rask056 = new(
        "RASK056",
        "External component must be partial",
        "'{0}' must be declared 'partial' — its name, module and props writer are generated into the same class",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "An external component is completed by a second part of the class. Without 'partial' there is "
                     + "nowhere to put it, so the three members ExternalComponent declares abstract are never "
                     + "implemented and the class does not compile — reported here, against the declaration, rather "
                     + "than as three unimplemented members whose names mean nothing to the author.",
        helpLinkUri: DiagnosticHelp.Link("RASK056"));

    private static readonly DiagnosticDescriptor Rask057 = new(
        "RASK057",
        "External component prop has no wire encoding",
        "'{0}' cannot send prop '{1}' to the browser: {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Props are serialized to JSON by generated code rather than by reflection, so a shape the "
                     + "generator cannot express has to be reported now rather than arriving as null in the browser. "
                     + "Supported: the primitive types, string, Guid, the date/time types, Uri, enums, byte[], "
                     + "nullable versions of those, arrays and lists of them, string-keyed dictionaries, and records "
                     + "or classes composed of the same. Callbacks are supported as Callback, Callback<T>, "
                     + "Action, Action<T>, Func<Task> "
                     + "and Func<T, Task>. Mark a property [SkipFactory] to keep it out of the props entirely.",
        helpLinkUri: DiagnosticHelp.Link("RASK057"));

    private static readonly DiagnosticDescriptor Rask058 = new(
        "RASK058",
        "External component name collision",
        "'{0}' and '{1}' share the simple name '{2}', which is the key the browser resolves a module by",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "The client runtime looks a component up by its simple type name, so two sharing one name would "
                     + "resolve to whichever module registered last — silently, and differently between builds. "
                     + "Rename one, or give it an explicit module by overriding Module.",
        helpLinkUri: DiagnosticHelp.Link("RASK058"));

    private static readonly DiagnosticDescriptor Rask059 = new(
        "RASK059",
        "Module override must be a constant string",
        "'{0}' overrides Module with an expression the build cannot read — return a constant string literal",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "The bundler needs the module specifier at BUILD time, to generate the entry that pairs the "
                     + "component with its adapter — long before any of this code runs. So the override has to be a "
                     + "literal the generator can read out of the syntax: `protected override string Module => "
                     + "\"@acme/charts/Chart\";`. Anything computed would leave the browser resolving a name the "
                     + "bundle never built.",
        helpLinkUri: DiagnosticHelp.Link("RASK059"));

    private static readonly DiagnosticDescriptor Rask077 = new(
        "RASK077",
        "Package island has no props snapshot",
        "'{0}' renders '{1}' from a package, but no '{0}.props.json' sits beside it, so none of its props were "
        + "generated — build with the package installed to extract it and commit the file, or declare the props on "
        + "'{0}' in C#",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "An island whose Module names an npm package gets its props from the package's own TypeScript, "
                     + "read into a snapshot the build writes beside the class. With neither a snapshot nor a prop "
                     + "declared by hand, the island renders with no way to set anything — which is almost always a "
                     + "snapshot that was never extracted or never committed. A warning rather than an error, because "
                     + "the island still renders.",
        helpLinkUri: DiagnosticHelp.Link("RASK077"));

    private static readonly DiagnosticDescriptor Rask078 = new(
        "RASK078",
        "Props snapshot cannot be read",
        "'{0}.props.json' cannot be used: {1} — re-extract it with the package installed rather than editing it by hand",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A props snapshot is written by the build from the package's TypeScript, and read here to generate "
                     + "the island's props. One this Rask.External cannot read — malformed, or written by a newer "
                     + "extractor — generates nothing, and saying so at the line it breaks on beats an island whose "
                     + "chain steps silently vanished.",
        helpLinkUri: DiagnosticHelp.Link("RASK078"));

    private static readonly DiagnosticDescriptor Rask079 = new(
        "RASK079",
        "Props snapshot describes a different component",
        "'{0}.props.json' was extracted for {1} — re-extract it, or correct the island's base class or Module",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A snapshot records which runtime and which module it was extracted from. When the class beside it "
                     + "now names another — the base class changed runtime, or Module points at a different export — "
                     + "its props describe some other component, so none are generated rather than steps the "
                     + "component does not have.",
        helpLinkUri: DiagnosticHelp.Link("RASK079"));

    private static readonly DiagnosticDescriptor Rask080 = new(
        "RASK080",
        "Package prop was not generated",
        "'{0}' has no chain step for the package's '{1}': {2} — declare it on '{0}' in C# to pass it anyway",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Most TypeScript props map onto a C# type: strings, numbers, booleans, dates, literal unions (as "
                     + "enums), arrays, maps and objects of those, and callbacks. A prop that does not — a union of "
                     + "unrelated types, a render function, a callback that must return a value — is left out of the "
                     + "island's steps and named here. Declaring the property on the island by hand, with a type you "
                     + "choose, sends it under the package's own name.",
        helpLinkUri: DiagnosticHelp.Link("RASK080"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Driven straight off the compilation rather than a cached model, matching CqrsCodecGenerator:
        // the walk produces symbols, and symbols must not be held across an incremental-pipeline
        // boundary. Everything happens inside the output callback, so nothing outlives its compilation.
        // The manifest URL comes from the build (RaskExternalPublicBase, which the targets default per
        // project kind), because only MSBuild knows whether this project is an app or a class library
        // -- and a library's static web assets are served from somewhere the client cannot guess.
        var manifest = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) =>
            provider.GlobalOptions.TryGetValue("build_property.RaskExternalManifestUrl", out var url)
                ? url
                : null);

        var snapshots = PackageIslandProps.Snapshots(context);

        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(manifest).Combine(snapshots),
            static (spc, t) => Execute(spc, t.Left.Left, t.Left.Right, t.Right));
    }

    private static void Execute(
        SourceProductionContext spc,
        Compilation compilation,
        string? manifestUrl,
        EquatableArray<PropsSnapshot> snapshots)
    {
        // Resolved one at a time and kept only if present, rather than required all-or-nothing. An
        // older Rask.External that predates a runtime would return null for it, and a combined guard
        // would then switch the WHOLE generator off — no props, no module, no diagnostics — for a
        // project whose components are all fine.
        var bases = new List<(INamedTypeSymbol Base, string Runtime)>();
        foreach (var (baseName, runtimeKey, _) in ExternalRuntimes.All)
        {
            if (compilation.GetTypeByMetadataName(baseName) is { } declared)
            {
                bases.Add((declared, runtimeKey));
            }
        }

        if (bases.Count == 0)
        {
            // The app does not reference Rask.External. Nothing to do, and not a problem.
            return;
        }

        // Every package island is resolved before any is described, and all of them together: a generated type
        // lands at namespace level, so its name is allocated across the whole set — exactly as the factory
        // generator allocates it — or one island's enum could take a name another island's step refers to.
        var paired = new List<(IslandFacts Facts, PropsSnapshot Snapshot)>();
        if (snapshots.Count > 0)
        {
            foreach (var candidate in Types(compilation.Assembly.GlobalNamespace))
            {
                if (PackageIslandProps.Facts(candidate) is { } facts
                    && PackageIslandProps.Find(snapshots, facts) is { } snapshot)
                {
                    paired.Add((facts, snapshot));
                }
            }
        }

        var resolved = PackageIslandProps.ResolveAll(paired);

        var islands = new List<ComponentModel>();
        var byName = new Dictionary<string, ComponentModel>(StringComparer.Ordinal);

        foreach (var type in Types(compilation.Assembly.GlobalNamespace))
        {
            if (spc.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            // The runtime IS the base class, so one lookup answers both "is this ours?" and "which
            // adapter mounts it?". An abstract class in the middle of someone's own hierarchy is
            // skipped: it declares no props of its own to write, and generating for it would emit
            // implementations of members its concrete subclasses must override anyway.
            string? runtime = null;
            foreach (var (declared, runtimeKey) in bases)
            {
                if (Inherits(type, declared))
                {
                    runtime = runtimeKey;
                    break;
                }
            }

            if (runtime is null)
            {
                continue;
            }

            if (type.IsAbstract)
            {
                continue;
            }

            var model = Describe(spc, type, runtime, snapshots, resolved);
            if (model is null)
            {
                continue;
            }

            if (byName.TryGetValue(model.Name, out var clash))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask058, model.Location, clash.Fqn, model.Fqn, model.Name));
                continue;
            }

            byName[model.Name] = model;
            islands.Add(model);
        }

        // Only carried when it is not the app's default -- the client already assumes that one, so
        // stamping it on every island in every app would be noise on the wire and in the markup.
        var libraryManifest =
            manifestUrl is { Length: > 0 }
            && !string.Equals(manifestUrl, "/_rask/external/manifest.json", StringComparison.Ordinal)
                ? manifestUrl
                : null;

        foreach (var island in islands)
        {
            spc.AddSource($"{island.Fqn.Replace("global::", string.Empty)}.External.g.cs",
                SourceText.From(Emit(island, libraryManifest), Encoding.UTF8));
        }

        if (islands.Count > 0)
        {
            // A package island has no front-end file of its own to import a props interface into — its
            // types are the package's — so it is left out of the TypeScript the build writes.
            spc.AddSource("RaskExternalGeneratedTypeScript.g.cs",
                SourceText.From(TypeScriptCarrier(islands.Where(static i => !i.IsPackage).ToList()), Encoding.UTF8));

            spc.AddSource("RaskExternalIslands.g.cs",
                SourceText.From(IslandCarrier(islands), Encoding.UTF8));
        }
    }

    /// <summary>
    ///     Carries each island's declared runtime and module out of the compiler as string constants.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The build used to read a runtime off the file extension, and that worked only while
    ///         every runtime owned one: <c>.tsx</c> meant React, <c>.ts</c> beside a <c>.cs</c> meant
    ///         Lit. React, Preact and Solid all write <c>.tsx</c>, and Angular writes the same
    ///         <c>.ts</c> Lit does — so an extension now names a FAMILY, not a runtime, and the glob
    ///         has nothing left to decide with.
    ///     </para>
    ///     <para>
    ///         Guessing wrong is silent. A Solid island handed React's adapter compiles, bundles,
    ///         ships and loads; the chunk default-exports an adapter; the adapter calls
    ///         <c>createRoot</c> on a Solid component and mounts nothing, with the browser reporting a
    ///         failure that names neither Solid nor the guess that caused it.
    ///     </para>
    ///     <para>
    ///         So the base class — the one place the runtime cannot drift from what actually mounts —
    ///         becomes the authority for the build too. The values ride out in the assembly as
    ///         constants and an MSBuild task lifts them back out of the PE metadata, the same
    ///         arrangement <see cref="TypeScriptCarrier" /> already uses for the prop types.
    ///     </para>
    /// </remarks>
    private static string IslandCarrier(List<ComponentModel> components)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.External.Generated;");
        sb.AppendLine();
        sb.AppendLine("internal static class RaskExternalIslands");
        sb.AppendLine("{");

        for (var i = 0; i < components.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            // "runtime|module" in one constant rather than two fields per component. The reader wants
            // both together and a single field cannot go half-missing, which two could.
            sb.Append("    public const string ").Append(components[i].Name).Append(" = ")
                .Append(Literal(components[i].Runtime + "|" + components[i].Module)).AppendLine(";");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    ///     Carries each component's prop types out of the compiler as string constants.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is what makes the contract two-way. Without it a <c>.tsx</c> types its own props by
    ///         hand and a renamed C# property breaks the front end silently, at runtime, in the browser
    ///         — the exact failure the feature claims to prevent.
    ///     </para>
    ///     <para>
    ///         A source generator cannot write files: it has no build directory, and an incremental run
    ///         can be cancelled after producing half its output. So the text rides in the assembly as
    ///         constants and an MSBuild task lifts it back out of the PE metadata, the same arrangement
    ///         the CQRS lane already uses. The constants are internal — nothing reads them from C#, and
    ///         public ones would put the whole front end into the app's API surface.
    ///     </para>
    /// </remarks>
    private static string TypeScriptCarrier(List<ComponentModel> components)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.External.Generated;");
        sb.AppendLine();
        sb.AppendLine("internal static class RaskExternalGeneratedTypeScript");
        sb.AppendLine("{");

        for (var i = 0; i < components.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            // Keyed by the component's simple name, which RASK058 already guarantees is unique —
            // the same key the client runtime resolves a module by, so the two cannot drift.
            sb.Append("    public const string ").Append(components[i].Name).Append(" = ")
                .Append(Literal(PropsDeclaration(components[i]))).AppendLine(";");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>The <c>.d.ts</c> a component's front-end file imports its props from.</summary>
    private static string PropsDeclaration(ComponentModel component)
    {
        var emitter = new TypeScriptEmitter();
        var members = new StringBuilder();

        foreach (var prop in component.Props)
        {
            // Ensure() returns the type expression AND emits any named interface it needs, so the
            // records a prop is composed of are declared in the same file that references them.
            //
            // The `| null` is added here rather than read off the WireType: nullability of a REFERENCE
            // type rides on the property's NullableAnnotation, and WireShape only folds that into a
            // member when it classifies a whole record. A component's props are classified one at a
            // time, so at this level the annotation is the generator's to carry. A nullable prop stays
            // required rather than optional — the writer emits the key with a JSON null, because
            // "never set" and "set to nothing" are different facts.
            var type = emitter.Ensure(prop.Wire);
            if (prop.IsNullable && !type.EndsWith(" | null", StringComparison.Ordinal))
            {
                type += " | null";
            }

            members.Append("  ").Append(prop.WireName).Append(": ").Append(type).AppendLine(";");
        }

        foreach (var handler in component.Handlers)
        {
            // Optional because an unwired callback omits its key entirely rather than sending null,
            // so the front end genuinely sees `undefined` and React's optional-prop handling applies.
            //
            // Void even for a Func<T, Task>: the callback crosses as a handler reference and the
            // client hands back a plain function that ships the payload, so there is nothing on the
            // front end to await. Typing it as returning a promise would describe a value that does
            // not exist.
            var argument = handler.Shape.Argument is null
                ? string.Empty
                : "value: " + emitter.Ensure(WireShape.Classify(handler.Shape.Argument, allowFile: false));

            members.Append("  ").Append(handler.WireName).Append("?: (")
                .Append(argument).AppendLine(") => void;");
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated from the C# component of the same name. Do not edit: the next build");
        sb.AppendLine("// overwrites it, and the C# is the source of truth for what crosses the boundary.");
        sb.AppendLine();

        var declarations = emitter.Declarations;
        if (declarations.Length > 0)
        {
            sb.Append(declarations.TrimEnd()).AppendLine().AppendLine();
        }

        sb.Append("export interface ").Append(component.Name).AppendLine("Props {");
        sb.Append(members);
        sb.AppendLine("}");
        return sb.ToString();
    }

    // Escaped rather than a raw string literal: the TypeScript carries quotes and braces of its own,
    // and an escaped literal cannot be broken by anything a doc comment or a prop name contains.
    private static string Literal(string value) =>
        Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(value, quote: true);

    /// <summary>Every named type in the assembly, nested types included.</summary>
    private static IEnumerable<INamedTypeSymbol> Types(INamespaceOrTypeSymbol root)
    {
        foreach (var member in root.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol ns:
                    foreach (var nested in Types(ns))
                    {
                        yield return nested;
                    }

                    break;

                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nested in Types(type))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private static bool Inherits(INamedTypeSymbol type, INamedTypeSymbol target)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t, target))
            {
                return true;
            }
        }

        return false;
    }

    private static ComponentModel? Describe(
        SourceProductionContext spc,
        INamedTypeSymbol type,
        string runtime,
        EquatableArray<PropsSnapshot> snapshots,
        Dictionary<IslandFacts, PackageIsland> resolved)
    {
        var location = type.Locations.FirstOrDefault(l => l.IsInSource);
        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var isPartial = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(c => c.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

        if (!isPartial)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask056, location, type.Name));
            return null;
        }

        // The module defaults to the sibling file, paired by filename exactly as scoped CSS and scoped
        // JS already are. Each runtime implies its own extension — React .tsx, Vue .vue, Svelte
        // .svelte, and Lit .ts because a Lit component is ordinary TypeScript. The base class is what
        // says which, so none of it has to be declared twice.
        //
        // Read out of the SYNTAX rather than evaluated, because the value is needed at build time — the
        // bundler generates one entry module per component long before any of this code could run.
        var declaredModule = ModuleLiteral.Read(type);
        if (declaredModule.Failed)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask059, declaredModule.Location ?? location, type.Name));
            return null;
        }

        var module = declaredModule.Value ?? $"./{type.Name}.{ExternalRuntimes.Extension(runtime)}";

        var model = new ComponentModel
        {
            Name = type.Name,
            Fqn = fqn,
            Namespace = type.ContainingNamespace.IsGlobalNamespace
                ? null
                : type.ContainingNamespace.ToDisplayString(),
            Module = module,
            Runtime = runtime,
            Location = location,
            DeclaresModule = declaredModule.Declared,
            IsPackage = PackageSpecifier.IsBare(module),
        };

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer || property.SetMethod is null
                || property.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == SkipFactoryName))
            {
                continue;
            }

            var wireName = WireShape.WireName(property);

            if (Callback(property.Type) is { } shape)
            {
                model.Handlers.Add(new IslandHandler(property.Name, wireName, shape));
                continue;
            }

            var wire = WireShape.Classify(property.Type, allowFile: false);
            if (wire.Kind == WireKind.Unsupported)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask057,
                    property.Locations.FirstOrDefault(l => l.IsInSource) ?? location,
                    type.Name,
                    property.Name,
                    wire.Reason ?? "the type has no JSON encoding"));
                continue;
            }

            model.Props.Add(new IslandProp(
                property.Name,
                wireName,
                wire,
                property.Type.NullableAnnotation == NullableAnnotation.Annotated,
                property.IsRequired,
                property.Type.IsReferenceType
                || property.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T));
        }

        if (model.IsPackage)
        {
            DescribePackage(spc, type, model, snapshots, resolved, declaredModule.Location ?? location);
        }

        return model;
    }

    /// <summary>
    ///     Pairs a package island with its committed props snapshot, reporting what cannot be generated.
    /// </summary>
    private static void DescribePackage(
        SourceProductionContext spc,
        INamedTypeSymbol type,
        ComponentModel model,
        EquatableArray<PropsSnapshot> snapshots,
        Dictionary<IslandFacts, PackageIsland> resolved,
        Location? moduleLocation)
    {
        if (PackageIslandProps.Facts(type) is not { } facts)
        {
            return;
        }

        model.Facts = facts;

        if (PackageIslandProps.Find(snapshots, facts) is not { } snapshot)
        {
            // An island that declares props by hand is a deliberate choice to type them in C#, not a
            // missing snapshot — the shape docs/islands.md has always shown for a vendor component.
            if (facts.UserProps.Count == 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask077, moduleLocation, type.Name, model.Module));
            }

            return;
        }

        model.Snapshot = snapshot;
        if (!resolved.TryGetValue(facts, out var island))
        {
            return;
        }

        switch (island.Verdict)
        {
            case PackageVerdict.Unreadable:
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask078, SnapshotLocation(snapshot.Path, snapshot.DefectLine, snapshot.DefectColumn),
                    type.Name, island.VerdictDetail));
                return;

            case PackageVerdict.RuntimeMismatch:
            case PackageVerdict.ModuleMismatch:
                spc.ReportDiagnostic(Diagnostic.Create(Rask079, moduleLocation, type.Name, island.VerdictDetail));
                return;
        }

        foreach (var problem in island.Problems)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Rask080, SnapshotLocation(snapshot.Path, problem.Line, problem.Column),
                type.Name, problem.PropName, problem.Reason));
        }

        model.Package = island;

        foreach (var prop in island.Props)
        {
            // A generated property whose name matches an inherited member HIDES it, which is CS0108 and
            // fatal here. Rask's own instance members were already renamed away by the resolver; what can
            // still match is a static chain entry inherited from RaskMarkup (Label, Title, Form), which a
            // prop may shadow exactly as Element's own Title does — by saying `new`.
            if (!prop.DeclaredByUser && InheritsMemberNamed(type, prop.ClrName))
            {
                model.NewNames.Add(prop.ClrName);
            }
        }
    }

    private static bool InheritsMemberNamed(INamedTypeSymbol type, string name)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.GetMembers(name).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    // An additional file has no syntax tree, so its location is built from the path and the position the
    // snapshot reader recorded. The IDE and the command line both show it as `file(line,column)`.
    private static Location SnapshotLocation(string path, int line, int column)
    {
        var position = new LinePosition(Math.Max(line - 1, 0), Math.Max(column - 1, 0));
        return Location.Create(path, new TextSpan(0, 0), new LinePositionSpan(position, position));
    }

    /// <summary>The callback shape a prop takes, or null when it is not a callback at all.</summary>
    /// <remarks>
    ///     <para>
    ///         The four shapes Rask already auto-wraps, plus the CARRIER that holds any one of them.
    ///         Something outside the set falls through to the wire classifier, which rejects it with
    ///         RASK057 — better than silently dropping a prop the author clearly meant to be called.
    ///     </para>
    ///     <para>
    ///         A carrier is not a delegate, and that is the point of it: a delegate-typed property swallows
    ///         its own chain step, because C# reads <c>x.OnPick(fn)</c> as invoking the property and never
    ///         reaches the extension setter (CS1593). So an island prop that is set through the chain —
    ///         which is every one of them — has to be a carrier, and this has to know that.
    ///     </para>
    ///     <para>
    ///         One difference travels with it: a bare delegate says statically whether it is asynchronous,
    ///         and a carrier does not — it holds either shape and decides when invoked. So a carrier's
    ///         bridge is always emitted in the asynchronous form. See <c>EmitArgumentBridges</c>.
    ///     </para>
    /// </remarks>
    private static CallbackShape? Callback(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        // `Callback?` is `Nullable<Callback>` — a struct — so the carrier is one level in.
        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            && named.TypeArguments[0] is INamedTypeSymbol inner)
        {
            named = inner;
        }

        var definition = named.OriginalDefinition.ToDisplayString();

        // Carriers first: they are not delegates, so the TypeKind guard below would reject them.
        var carrier = definition switch
        {
            "Rask.Core.Callback" => new CallbackShape(null, true, IsCarrier: true),
            "Rask.Core.Callback<T>" => new CallbackShape(named.TypeArguments[0], true, IsCarrier: true),
            _ => null,
        };

        if (carrier is not null)
        {
            return carrier;
        }

        if (named.TypeKind != TypeKind.Delegate)
        {
            return null;
        }

        return definition switch
        {
            "System.Action" => new CallbackShape(null, false),
            "System.Action<T>" => new CallbackShape(named.TypeArguments[0], false),
            "System.Func<System.Threading.Tasks.Task>" => new CallbackShape(null, true),
            "System.Func<T, System.Threading.Tasks.Task>" => new CallbackShape(named.TypeArguments[0], true),
            _ => null,
        };
    }

    private static string Emit(ComponentModel island, string? manifestUrl)
    {
        var emitter = new PropsWriterEmitter(stringEnums: island.IsPackage);
        var package = island.Package is { } resolved ? new PackageIslandEmitter(island.Facts!, resolved) : null;
        var body = new StringBuilder();

        foreach (var prop in island.Props)
        {
            var id = emitter.Ensure(prop.Wire);
            var key = Literal(WireFor(island, prop.ClrName, prop.WireName));

            // A package island omits a prop that is not set, so the package's own default applies — a JSON
            // null would override it. Everywhere else a null prop is written as null: a hand-written front
            // end types it as `T | null` and required, because "never set" and "set to nothing" differ.
            if (island.IsPackage && !prop.IsRequired && prop.CanBeNull)
            {
                body.AppendLine($"        if (this.{prop.ClrName} is not null)");
                body.AppendLine("        {");
                body.AppendLine($"            writer.WritePropertyName({key});");
                body.AppendLine($"            WP{id}(writer, this.{prop.ClrName}!);");
                body.AppendLine("        }");
                continue;
            }

            body.AppendLine($"        writer.WritePropertyName({key});");
            // Null-forgiving at the call site rather than nullable writer parameters. Every shape that
            // can be null already null-guards inside its writer, and a JSON null is the correct answer
            // for a null prop — so the annotation would only have to be threaded through every writer
            // to say something the runtime already handles.
            body.AppendLine($"        WP{id}(writer, this.{prop.ClrName}!);");
        }

        foreach (var handler in island.Handlers)
        {
            // A callback with an argument is registered as a WRAPPER, not as itself. The dispatcher
            // has no general Action<T> case and cannot have one — T is only known where the component
            // is compiled — so the raw delegate fell through to a DynamicInvoke with no arguments and
            // threw on the first click. The wrapper reads the argument here, where the type is known.
            // A carrier is not itself dispatchable, so what is registered is the delegate it holds —
            // the same `value?.Handler` unwrap ElementEvents.SetHandler does at the DOM boundary.
            // `Handler` is `Delegate?` — the guard below proves the CARRIER was supplied, not that it
            // holds anything — so the null-forgiving operator is what says "a carrier that reached a
            // prop always came from a setter that refused null".
            var raw = handler.Shape.IsCarrier
                ? $"this.{handler.ClrName}!.Value.Handler!"
                : $"this.{handler.ClrName}";

            var registered = handler.Shape.Argument is null
                ? raw
                : $"__Arg{handler.ClrName}";

            var wire = WireFor(island, handler.ClrName, handler.WireName);

            if (island.IsPackage)
            {
                var argIndex = handler.Shape.Argument is null ? -1 : ForwardedArgIndex(island, wire);
                body.Append(PackageIslandEmitter.WriteCallback(handler.ClrName, wire, argIndex, registered));
                continue;
            }

            // A null callback omits its key entirely rather than writing null, so the front end sees
            // `undefined` and React's optional-prop handling does the right thing. Writing null would
            // also leave a stale key that looks callable in devtools.
            body.AppendLine($"        if (this.{handler.ClrName} is not null)");
            body.AppendLine("        {");
            body.AppendLine($"            writer.WritePropertyName({Literal(wire)});");
            body.AppendLine("            writer.WriteStartObject();");
            body.AppendLine("            writer.WriteString(\"$h\", "
                            + $"global::Rask.External.ExternalHandlers.Register(this, {registered}));");
            body.AppendLine("            writer.WriteEndObject();");
            body.AppendLine("        }");
        }

        if (package is not null)
        {
            foreach (var prop in island.Package!.Props)
            {
                if (prop.DeclaredByUser)
                {
                    continue;
                }

                if (prop.Callback is { } callback)
                {
                    var registered = callback.ArgType is null
                        ? $"this.{prop.ClrName}!.Value.Handler!"
                        : $"__Arg{prop.ClrName}";
                    body.Append(PackageIslandEmitter.WriteCallback(prop.ClrName, prop.Wire, callback.ArgIndex, registered));
                    continue;
                }

                body.Append(package.WriteValue(prop));
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        if (island.Snapshot is { } snapshot && island.Package is not null)
        {
            // Where the generated half of this class came from, so a reader of the generated file can find
            // the snapshot — and the version it was taken at — without knowing the convention.
            sb.Append("// Props from ")
                .Append(PackageIslandNaming.SingleLine(snapshot.Module))
                .Append(snapshot.PackageVersion is { } version ? " " + PackageIslandNaming.SingleLine(version) : string.Empty)
                .Append(", runtime ").Append(PackageIslandNaming.SingleLine(snapshot.Runtime))
                .AppendLine(", read from its committed props snapshot.");
            if (snapshot.Skipped.Count > 0)
            {
                sb.Append("// Not generated: ")
                    .AppendLine(PackageIslandNaming.SingleLine(string.Join(", ",
                        snapshot.Skipped.Select(static s => s.Name + " (" + s.Reason + ")"))));
            }
        }

        sb.AppendLine("#nullable enable");
        if (island.Namespace is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"namespace {island.Namespace};");
        }

        sb.AppendLine();
        sb.AppendLine($"partial class {island.Name}");
        sb.AppendLine("{");

        // Only what the compiler alone knows. Everything else — the host element, the opaque boundary,
        // the slot grouping, the hydration property, the runtime script, the attribute writer — is
        // hand-written on ExternalComponent, where it can be read.
        sb.AppendLine("    /// <summary>The name the client runtime resolves this component's module by.</summary>");
        sb.AppendLine($"    protected override string ComponentName => \"{island.Name}\";");
        sb.AppendLine();

        // Only when it is not the app's own. An island in an app -- the overwhelmingly common case --
        // generates nothing here, and the base class's null means the host element carries no attribute
        // and the client uses its default.
        if (manifestUrl is { Length: > 0 })
        {
            sb.AppendLine("    /// <summary>The manifest that resolves this island, when it is not the app's own.</summary>");
            sb.AppendLine($"    protected override string? ManifestUrl => {Literal(manifestUrl)};");
            sb.AppendLine();
        }

        if (!island.DeclaresModule)
        {
            sb.AppendLine("    /// <summary>The front-end file beside this one, paired by filename.</summary>");
            sb.AppendLine($"    protected override string Module => {Literal(island.Module)};");
            sb.AppendLine();
        }

        if (package is not null)
        {
            foreach (var prop in island.Package!.Props)
            {
                if (prop.DeclaredByUser)
                {
                    continue;
                }

                sb.Append(package.Declaration(prop, island.NewNames.Contains(prop.ClrName)));
                sb.AppendLine();
            }

            foreach (var prop in island.Package.Props)
            {
                if (!prop.DeclaredByUser && prop.Callback?.ArgType is not null)
                {
                    sb.Append(package.Bridge(prop));
                }
            }
        }

        foreach (var handler in island.Handlers)
        {
            if (handler.Shape.Argument is null)
            {
                continue;
            }

            var read = ScalarRead(handler.Shape.Argument);
            var type = handler.Shape.Argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            // A carrier does not say statically which shape it holds, so its bridge is the asynchronous
            // one either way — `Invoke` hands back null when the handler was synchronous, and
            // `?? Task.CompletedTask` turns that into the completed task this signature promises. No
            // state machine is created for a synchronous handler; the null IS the fast path.
            var invoke = handler.Shape.IsAsync
                ? "global::System.Func<global::System.Text.Json.JsonElement, global::System.Threading.Tasks.Task>"
                : "global::System.Action<global::System.Text.Json.JsonElement>";

            sb.AppendLine($"    /// <summary>Feeds the argument to <c>{handler.ClrName}</c> from the dispatched frame.</summary>");
            sb.AppendLine("    /// <remarks>");
            sb.AppendLine("    ///     The client sends the argument as the first element of <c>args</c>. Read here, where");
            sb.AppendLine("    ///     the type is known, so nothing reflects and the component still trims.");
            sb.AppendLine("    /// </remarks>");
            sb.AppendLine($"    private {invoke} __Arg{handler.ClrName} => __p =>");
            sb.AppendLine("    {");
            sb.AppendLine("        // A frame carrying no args is a stale id or a hand-written client; the default is a");
            sb.AppendLine("        // better answer than an exception that takes the page down.");
            sb.AppendLine($"        {type} __v = default!;");
            sb.AppendLine("        if (__p.ValueKind == global::System.Text.Json.JsonValueKind.Object");
            sb.AppendLine("            && __p.TryGetProperty(\"args\", out var __a)");
            sb.AppendLine("            && __a.ValueKind == global::System.Text.Json.JsonValueKind.Array");
            sb.AppendLine("            && __a.GetArrayLength() > 0)");
            sb.AppendLine("        {");
            sb.AppendLine($"            __v = {read};");
            sb.AppendLine("        }");
            sb.AppendLine();
            // `return` only for the async shape: the sync one is an Action, and returning a value
            // from a void-returning lambda is CS8030. A carrier is always the async shape (see above)
            // and is invoked through the struct rather than called like a delegate.
            var call = handler.Shape.IsCarrier
                ? $"this.{handler.ClrName}!.Value.Invoke(__v) "
                  + "?? global::System.Threading.Tasks.Task.CompletedTask"
                : $"this.{handler.ClrName}!(__v)";

            sb.AppendLine(handler.Shape.IsAsync
                ? $"        return {call};"
                : $"        {call};");
            sb.AppendLine("    };");
            sb.AppendLine();
        }

        sb.AppendLine("    /// <summary>The props, as members of the JSON object the client runtime hands to the adapter.</summary>");
        sb.AppendLine("    protected override void WriteProps(global::System.Text.Json.Utf8JsonWriter writer)");
        sb.AppendLine("    {");
        sb.Append(body);
        sb.AppendLine("    }");

        var methods = emitter.Methods;
        if (methods.Length > 0)
        {
            sb.AppendLine();
            sb.Append(methods);
        }

        if (package is not null && package.WriterMethods.Length > 0)
        {
            sb.AppendLine();
            sb.Append(package.WriterMethods);
        }

        sb.AppendLine("}");

        if (package is not null)
        {
            sb.Append(package.Types());
        }

        return sb.ToString();
    }

    /// <summary>
    ///     The JSON key a hand-declared member of a package island is written under.
    /// </summary>
    /// <remarks>
    ///     The package's own name when the member stands for one of its props — <c>AriaLabel</c> has to be
    ///     sent as <c>aria-label</c>, which no C# name can spell — unless the author pinned a name of their
    ///     own with <c>[JsonPropertyName]</c>, which always wins.
    /// </remarks>
    private static string WireFor(ComponentModel island, string clrName, string wireName)
    {
        if (island.Package is null || !string.Equals(wireName, CamelCase(clrName), StringComparison.Ordinal))
        {
            return wireName;
        }

        foreach (var prop in island.Package.Props)
        {
            if (prop.DeclaredByUser && string.Equals(prop.ClrName, clrName, StringComparison.Ordinal))
            {
                return prop.Wire;
            }
        }

        return wireName;
    }

    /// <summary>
    ///     Which argument the client forwards for a hand-declared callback with an argument: the package's
    ///     first non-event argument when the snapshot describes the callback, otherwise the first.
    /// </summary>
    /// <remarks>
    ///     When the snapshot says every argument is an event, nothing is forwarded (-1) rather than the first.
    ///     Forwarding an event is the failure <c>$a</c> exists to prevent: it holds <c>view: window</c>, the host's
    ///     <c>JSON.stringify</c> throws, and the call never reaches C#. The callback still runs, with its
    ///     argument's default.
    /// </remarks>
    private static int ForwardedArgIndex(ComponentModel island, string wire)
    {
        if (island.Snapshot is { } snapshot)
        {
            foreach (var prop in snapshot.Props)
            {
                if (string.Equals(prop.Wire, wire, StringComparison.Ordinal) && prop.Type.Kind == "callback")
                {
                    return PackageIslandProps.ForwardedArgIndex(prop.Type);
                }
            }
        }

        return 0;
    }

    private static string CamelCase(string name) =>
        name.Length == 0 || char.IsLower(name[0]) ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    /// <summary>
    ///     The expression that reads a callback argument of <paramref name="type" /> out of the frame.
    /// </summary>
    /// <remarks>
    ///     Scalars only, deliberately. A richer argument needs the reflection-free reader the CQRS
    ///     codecs already generate (WireCodecEmitter), which is not shared out of that assembly yet —
    ///     and JsonSerializer.Deserialize would work today at the cost of the trimming and AOT
    ///     guarantee this feature is built on. Anything outside this table is RASK057 rather than a
    ///     silent default.
    /// </remarks>
    private static string? ScalarRead(ITypeSymbol type)
    {
        var element = "__a[0]";

        if (type.TypeKind == TypeKind.Enum)
        {
            var underlying = (type as INamedTypeSymbol)?.EnumUnderlyingType?.SpecialType;
            var reader = underlying == SpecialType.System_Int64 ? "GetInt64()" : "GetInt32()";
            return $"({type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){element}.{reader}";
        }

        return type.SpecialType switch
        {
            SpecialType.System_Int32 => $"{element}.GetInt32()",
            SpecialType.System_Int64 => $"{element}.GetInt64()",
            SpecialType.System_Double => $"{element}.GetDouble()",
            SpecialType.System_Single => $"{element}.GetSingle()",
            SpecialType.System_Decimal => $"{element}.GetDecimal()",
            SpecialType.System_Boolean => $"{element}.GetBoolean()",
            SpecialType.System_String => $"{element}.GetString()!",
            _ => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) switch
            {
                "global::System.Guid" => $"{element}.GetGuid()",
                "global::System.DateTimeOffset" => $"{element}.GetDateTimeOffset()",
                "global::System.DateTime" => $"{element}.GetDateTime()",
                _ => null,
            },
        };
    }

    private sealed record CallbackShape(ITypeSymbol? Argument, bool IsAsync, bool IsCarrier = false);

    /// <summary>A prop the author declared in C#.</summary>
    /// <param name="ClrName">The property's C# name.</param>
    /// <param name="WireName">The JSON key it is written under — camelCase, or a pinned <c>[JsonPropertyName]</c>.</param>
    /// <param name="Wire">Its wire shape.</param>
    /// <param name="IsNullable">Whether the property is annotated nullable.</param>
    /// <param name="IsRequired">Whether it is a <c>required</c> member — written even when null.</param>
    /// <param name="CanBeNull">Whether a null check compiles for it: a reference type or a <c>Nullable&lt;T&gt;</c>.</param>
    private sealed record IslandProp(
        string ClrName,
        string WireName,
        WireType Wire,
        bool IsNullable,
        bool IsRequired,
        bool CanBeNull);

    private sealed record IslandHandler(string ClrName, string WireName, CallbackShape Shape);

    private sealed class ComponentModel
    {
        public string Name { get; set; } = string.Empty;
        public string Fqn { get; set; } = string.Empty;
        public string? Namespace { get; set; }
        public string Module { get; set; } = string.Empty;
        public string Runtime { get; set; } = string.Empty;
        public Location? Location { get; set; }

        /// <summary>Whether the author wrote their own <c>Module</c>, so the generator must not.</summary>
        public bool DeclaresModule { get; set; }

        /// <summary>Whether <see cref="Module" /> names a package rather than a file beside the class.</summary>
        public bool IsPackage { get; set; }

        /// <summary>The package island's facts, when it is one.</summary>
        public IslandFacts? Facts { get; set; }

        /// <summary>The snapshot paired with a package island, when one was found.</summary>
        public PropsSnapshot? Snapshot { get; set; }

        /// <summary>The resolved snapshot, when it is usable.</summary>
        public PackageIsland? Package { get; set; }

        /// <summary>Generated props that must say <c>new</c>, because they shadow an inherited chain entry.</summary>
        public HashSet<string> NewNames { get; } = new(StringComparer.Ordinal);

        /// <summary>
        ///     The manifest this island resolves through, or null for the app's own.
        /// </summary>
        /// <remarks>
        ///     Set from the build rather than discovered from the symbol: whether a project is an app or
        ///     a class library is an MSBuild fact, and a library's static web assets are served under
        ///     <c>_content/&lt;PackageId&gt;/</c> where the client cannot guess them.
        /// </remarks>
        public string? ManifestUrl { get; set; }

        public List<IslandProp> Props { get; } = new();
        public List<IslandHandler> Handlers { get; } = new();
    }
}
