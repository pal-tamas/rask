using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Rask.Generators.Shared;

namespace Rask.Generators.Islands;

/// <summary>
///     Turns a component marked <c>[Island]</c> into a host element whose subtree a foreign renderer
///     owns, and writes its props as JSON with no reflection.
/// </summary>
/// <remarks>
///     <para>
///         An island declares nothing but its props. Everything that makes it an island — the host
///         element, the opaque-subtree boundary, the hydration step, the props writer, the
///         <c>&lt;script&gt;</c> that boots the client runtime — is emitted here, into the same
///         <c>partial</c>. That is what lets the declaration stay an ordinary component: no base type
///         to inherit, no interface to implement, and a migration that is one attribute and one
///         deletion.
///     </para>
///     <para>
///         Only the current assembly is walked, which is correct rather than a limitation: the partial
///         has to be generated in the compilation that declares the class, so a component library
///         holding islands generates its own.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class IslandGenerator : IIncrementalGenerator
{
    private const string IslandAttributeName = "Rask.Islands.IslandAttribute";
    private const string ComponentFullName = "Rask.Core.Component";
    private const string SkipFactoryName = "Rask.Core.SkipFactoryAttribute";

    private static readonly DiagnosticDescriptor Rask054 = new(
        "RASK055",
        "Island component must be partial",
        "Island '{0}' must be declared 'partial' — its host element, props writer and hydration step are generated into the same class",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Everything that makes a component an island is generated into a second part of the class. "
                     + "Without 'partial' there is nowhere to put it, so the component would compile as an ordinary "
                     + "Rask component and render its own markup instead of the front-end file beside it — a silent "
                     + "wrong answer rather than a build failure.",
        helpLinkUri: DiagnosticHelp.Link("RASK055"));

    private static readonly DiagnosticDescriptor Rask055 = new(
        "RASK056",
        "Island prop has no wire encoding",
        "Island '{0}' cannot send prop '{1}' to the browser: {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "An island's props are serialized to JSON by generated code rather than by reflection, so a "
                     + "shape the generator cannot express has to be reported now rather than arriving as null in "
                     + "the browser. Supported: the primitive types, string, Guid, the date/time types, Uri, enums, "
                     + "byte[], nullable versions of those, arrays and lists of them, string-keyed dictionaries, and "
                     + "records or classes composed of the same. Callbacks are supported as Action, Action<T>, "
                     + "Func<Task> and Func<T, Task>. Mark a property [SkipFactory] to keep it out of the props "
                     + "entirely.",
        helpLinkUri: DiagnosticHelp.Link("RASK056"));

    private static readonly DiagnosticDescriptor Rask056 = new(
        "RASK057",
        "Island declares its own Render",
        "Island '{0}' declares Render(), but an island's markup comes from '{1}' — remove Render()",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "An island renders a host element and lets its own framework fill the subtree, so a Render() "
                     + "override is never called. Left in place it reads as the component's markup while having no "
                     + "effect at all, which is worse than either behaviour on its own.",
        helpLinkUri: DiagnosticHelp.Link("RASK057"));

    private static readonly DiagnosticDescriptor Rask057 = new(
        "RASK058",
        "Island name collision",
        "Islands '{0}' and '{1}' share the simple name '{2}', which is the key the browser resolves a module by",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "The client runtime looks an island up by its simple type name, so two islands sharing one name "
                     + "would resolve to whichever module registered last — silently, and differently between builds. "
                     + "Rename one, or give it an explicit module with [Island(\"...\")].",
        helpLinkUri: DiagnosticHelp.Link("RASK058"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Driven straight off the compilation rather than a cached model, matching CqrsCodecGenerator:
        // the walk produces symbols, and symbols must not be held across an incremental-pipeline
        // boundary. Everything happens inside the output callback, so nothing outlives its compilation.
        context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) => Execute(spc, compilation));
    }

    private static void Execute(SourceProductionContext spc, Compilation compilation)
    {
        var attribute = compilation.GetTypeByMetadataName(IslandAttributeName);
        if (attribute is null)
        {
            // The app does not reference Rask.Islands. Nothing to do, and not a problem.
            return;
        }

        var componentType = compilation.GetTypeByMetadataName(ComponentFullName);
        if (componentType is null)
        {
            return;
        }

        var islands = new List<IslandModel>();
        var byName = new Dictionary<string, IslandModel>(StringComparer.Ordinal);

        foreach (var type in Types(compilation.Assembly.GlobalNamespace))
        {
            if (spc.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            var marker = type.GetAttributes().FirstOrDefault(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
            if (marker is null)
            {
                continue;
            }

            if (!Inherits(type, componentType))
            {
                // Not a component, so nothing downstream would ever render it. The attribute is the
                // author's stated intent though, so stay quiet rather than guessing at a rename.
                continue;
            }

            var model = Describe(spc, type, marker);
            if (model is null)
            {
                continue;
            }

            if (byName.TryGetValue(model.Name, out var clash))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask057, model.Location, clash.Fqn, model.Fqn, model.Name));
                continue;
            }

            byName[model.Name] = model;
            islands.Add(model);
        }

        foreach (var island in islands)
        {
            spc.AddSource($"{island.Fqn.Replace("global::", string.Empty)}.Island.g.cs",
                SourceText.From(Emit(island), Encoding.UTF8));
        }
    }

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

    private static IslandModel? Describe(SourceProductionContext spc, INamedTypeSymbol type, AttributeData marker)
    {
        var location = type.Locations.FirstOrDefault(l => l.IsInSource);
        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var isPartial = type.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(c => c.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

        if (!isPartial)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask054, location, type.Name));
            return null;
        }

        var explicitModule = marker.ConstructorArguments.Length == 1
                             && marker.ConstructorArguments[0].Value is string m
            ? m
            : null;

        var runtime = marker.NamedArguments
            .Where(n => n.Key == "Runtime")
            .Select(n => n.Value)
            .FirstOrDefault();

        var module = explicitModule ?? $"./{type.Name}.tsx";

        // A Render() the author wrote is never called — the element branch of the serializer takes over
        // the moment TagName is non-null — so it reads as this component's markup while doing nothing.
        var declaresRender = type.GetMembers("Render")
            .OfType<IMethodSymbol>()
            .Any(method => method.Parameters.Length == 0 && !method.IsStatic);

        if (declaresRender)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask056, location, type.Name, module));
            return null;
        }

        var model = new IslandModel
        {
            Name = type.Name,
            Fqn = fqn,
            Namespace = type.ContainingNamespace.IsGlobalNamespace
                ? null
                : type.ContainingNamespace.ToDisplayString(),
            Module = module,
            Runtime = Runtime(runtime, module),
            Location = location,
            DeclaresHydration = type.GetMembers("Hydration").Length > 0,
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

            if (property.Name == "Hydration")
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
                    Rask055,
                    property.Locations.FirstOrDefault(l => l.IsInSource) ?? location,
                    type.Name,
                    property.Name,
                    wire.Reason ?? "the type has no JSON encoding"));
                continue;
            }

            model.Props.Add(new IslandProp(property.Name, wireName, wire));
        }

        return model;
    }

    /// <summary>The callback shape a delegate prop takes, or null when it is not a callback at all.</summary>
    /// <remarks>
    ///     The four shapes Rask already auto-wraps. A delegate outside the set falls through to the wire
    ///     classifier, which rejects it with RASK056 — better than silently dropping a prop the author
    ///     clearly meant to be called.
    /// </remarks>
    private static CallbackShape? Callback(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Delegate)
        {
            return null;
        }

        var definition = named.OriginalDefinition.ToDisplayString();

        return definition switch
        {
            "System.Action" => new CallbackShape(null, false),
            "System.Action<T>" => new CallbackShape(named.TypeArguments[0], false),
            "System.Func<System.Threading.Tasks.Task>" => new CallbackShape(null, true),
            "System.Func<T, System.Threading.Tasks.Task>" => new CallbackShape(named.TypeArguments[0], true),
            _ => null,
        };
    }

    /// <summary>The wire spelling of the island's runtime.</summary>
    /// <remarks>
    ///     Resolved by enum member NAME rather than by its underlying number. The generator targets
    ///     netstandard2.0 and cannot reference the package it generates against, so the value arrives
    ///     as a boxed int — and matching on that would silently remap every island the day someone
    ///     inserts a member into <c>IslandRuntime</c>. A name cannot drift that way.
    /// </remarks>
    private static string Runtime(TypedConstant declared, string module)
    {
        switch (EnumMemberName(declared))
        {
            case "Lit":
                return "lit";
            case "React":
                return "react";
        }

        // Infer. React is the only runtime an extension can imply: .tsx and .jsx are unambiguous,
        // while a .ts says nothing at all — a Lit component is ordinary TypeScript, which is why Lit
        // has to name its runtime rather than being guessed at from the file.
        return "react";
    }

    /// <summary>The name of the enum member a <see cref="TypedConstant" /> holds, or null.</summary>
    private static string? EnumMemberName(TypedConstant constant)
    {
        if (constant.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } type || constant.Value is null)
        {
            return null;
        }

        foreach (var member in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.HasConstantValue && Equals(member.ConstantValue, constant.Value))
            {
                return member.Name;
            }
        }

        return null;
    }

    private static string Emit(IslandModel island)
    {
        var emitter = new IslandPropsEmitter();
        var body = new StringBuilder();

        body.AppendLine("        var buffer = new global::System.Buffers.ArrayBufferWriter<byte>(256);");
        body.AppendLine("        using (var writer = new global::System.Text.Json.Utf8JsonWriter(buffer))");
        body.AppendLine("        {");
        body.AppendLine("            writer.WriteStartObject();");

        foreach (var prop in island.Props)
        {
            var id = emitter.Ensure(prop.Wire);
            body.AppendLine($"            writer.WritePropertyName(\"{prop.WireName}\");");
            // Null-forgiving at the call site rather than nullable writer parameters. Every shape that
            // can be null already null-guards inside its writer, and a JSON null is the correct answer
            // for a null prop — so the annotation would only have to be threaded through every writer
            // to say something the runtime already handles.
            body.AppendLine($"            WP{id}(writer, this.{prop.ClrName}!);");
        }

        foreach (var handler in island.Handlers)
        {
            // A null callback omits its key entirely rather than writing null, so the front end sees
            // `undefined` and React's optional-prop handling does the right thing. Writing null would
            // also leave a stale key that looks callable in devtools.
            body.AppendLine($"            if (this.{handler.ClrName} is not null)");
            body.AppendLine("            {");
            body.AppendLine($"                writer.WritePropertyName(\"{handler.WireName}\");");
            body.AppendLine("                writer.WriteStartObject();");
            body.AppendLine("                writer.WriteString(\"$h\", "
                            + $"global::Rask.Islands.IslandHandlers.Register(this, this.{handler.ClrName}));");
            body.AppendLine("                writer.WriteEndObject();");
            body.AppendLine("            }");
        }

        body.AppendLine("            writer.WriteEndObject();");
        body.AppendLine("        }");
        body.AppendLine();
        body.AppendLine("        return global::System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);");

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        if (island.Namespace is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"namespace {island.Namespace};");
        }

        sb.AppendLine();
        sb.AppendLine($"partial class {island.Name}");
        sb.AppendLine("{");

        sb.AppendLine("    /// <summary>The host element a foreign renderer mounts into.</summary>");
        sb.AppendLine($"    protected override string? TagName => \"{IslandDefaultsHostTag}\";");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Everything below this element belongs to the island's own renderer.</summary>");
        sb.AppendLine("    protected override bool OpaqueSubtree => true;");
        sb.AppendLine();

        if (!island.DeclaresHydration)
        {
            sb.AppendLine("    /// <summary>When the adapter mounts. Defaults to as soon as the chunk has loaded.</summary>");
            sb.AppendLine("    public global::Rask.Islands.IslandHydration Hydration { get; set; }");
            sb.AppendLine();
        }

        sb.AppendLine("    /// <summary>Boots the client runtime. Deduplicated across every island on the page.</summary>");
        sb.AppendLine("    protected override global::Rask.Core.Component? HeadAssets =>");
        sb.AppendLine($"        Script.Src(\"{IslandDefaultsRuntimeUrl}\").Type(\"module\");");
        sb.AppendLine();

        sb.AppendLine("    /// <inheritdoc />");
        sb.AppendLine("    protected override void WriteAttributes(global::System.Text.StringBuilder sb)");
        sb.AppendLine("    {");
        sb.AppendLine($"        AppendAttr(sb, \"name\", \"{island.Name}\");");
        sb.AppendLine($"        AppendAttr(sb, \"module\", \"{island.Module}\");");
        sb.AppendLine($"        AppendAttr(sb, \"runtime\", \"{island.Runtime}\");");
        sb.AppendLine("        var hydrate = global::Rask.Islands.IslandDefaults.Wire(Hydration);");
        sb.AppendLine("        if (hydrate is not null)");
        sb.AppendLine("        {");
        sb.AppendLine("            AppendAttr(sb, \"hydrate\", hydrate);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        // AppendAttr, not sb.Append: it registers the Attribute frame as well as writing the");
        sb.AppendLine("        // markup. Without the frame the value renders once and never diffs again, so a prop");
        sb.AppendLine("        // change would stop reaching the adapter after the first paint.");
        sb.AppendLine("        AppendAttr(sb, \"props\", BuildIslandProps());");
        sb.AppendLine("    }");
        sb.AppendLine();

        sb.AppendLine("    /// <summary>The props, as the JSON the client runtime hands to the adapter.</summary>");
        sb.AppendLine("    private string BuildIslandProps()");
        sb.AppendLine("    {");
        sb.Append(body);
        sb.AppendLine("    }");

        var methods = emitter.Methods;
        if (methods.Length > 0)
        {
            sb.AppendLine();
            sb.Append(methods);
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    // Mirrors Rask.Islands.IslandDefaults. Duplicated as a literal because the generator targets
    // netstandard2.0 and cannot reference the package it generates against.
    private const string IslandDefaultsHostTag = "rask-island";
    private const string IslandDefaultsRuntimeUrl = "/_content/Rask.Islands/rask-islands.js";

    private sealed record CallbackShape(ITypeSymbol? Argument, bool IsAsync);

    private sealed record IslandProp(string ClrName, string WireName, WireType Wire);

    private sealed record IslandHandler(string ClrName, string WireName, CallbackShape Shape);

    private sealed class IslandModel
    {
        public string Name { get; set; } = string.Empty;
        public string Fqn { get; set; } = string.Empty;
        public string? Namespace { get; set; }
        public string Module { get; set; } = string.Empty;
        public string Runtime { get; set; } = string.Empty;
        public Location? Location { get; set; }
        public bool DeclaresHydration { get; set; }
        public List<IslandProp> Props { get; } = new();
        public List<IslandHandler> Handlers { get; } = new();
    }
}
