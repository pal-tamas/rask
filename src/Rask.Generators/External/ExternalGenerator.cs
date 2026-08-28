using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
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
///         Only the current assembly is walked, which is correct rather than a limitation: the partial
///         has to be generated in the compilation that declares the class, so a component library
///         holding these generates its own.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ExternalGenerator : IIncrementalGenerator
{
    private const string ReactBaseName = "Rask.External.ReactComponent";
    private const string LitBaseName = "Rask.External.LitComponent";
    private const string SkipFactoryName = "Rask.Core.SkipFactoryAttribute";

    // RASK057 ("declares its own Render") is retired. ExternalComponent seals Render(), so writing
    // one is now CS0239 from the compiler itself — a rule the type system can state does not need an
    // analyzer to notice it.

    private static readonly DiagnosticDescriptor Rask056 = new(
        "RASK056",
        "external component must be partial",
        "'{0}' must be declared 'partial' — its name, module and props writer are generated into the same class",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "A external component is completed by a second part of the class. Without 'partial' there is "
                     + "nowhere to put it, so the three members ExternalComponent declares abstract are never "
                     + "implemented and the class does not compile — reported here, against the declaration, rather "
                     + "than as three unimplemented members whose names mean nothing to the author.",
        helpLinkUri: DiagnosticHelp.Link("RASK056"));

    private static readonly DiagnosticDescriptor Rask057 = new(
        "RASK057",
        "external component prop has no wire encoding",
        "'{0}' cannot send prop '{1}' to the browser: {2}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Props are serialized to JSON by generated code rather than by reflection, so a shape the "
                     + "generator cannot express has to be reported now rather than arriving as null in the browser. "
                     + "Supported: the primitive types, string, Guid, the date/time types, Uri, enums, byte[], "
                     + "nullable versions of those, arrays and lists of them, string-keyed dictionaries, and records "
                     + "or classes composed of the same. Callbacks are supported as Action, Action<T>, Func<Task> "
                     + "and Func<T, Task>. Mark a property [SkipFactory] to keep it out of the props entirely.",
        helpLinkUri: DiagnosticHelp.Link("RASK057"));

    private static readonly DiagnosticDescriptor Rask058 = new(
        "RASK058",
        "external component name collision",
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
        var reactBase = compilation.GetTypeByMetadataName(ReactBaseName);
        var litBase = compilation.GetTypeByMetadataName(LitBaseName);
        if (reactBase is null || litBase is null)
        {
            // The app does not reference Rask.External. Nothing to do, and not a problem.
            return;
        }

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
            string runtime;
            if (Inherits(type, reactBase))
            {
                runtime = "react";
            }
            else if (Inherits(type, litBase))
            {
                runtime = "lit";
            }
            else
            {
                continue;
            }

            if (type.IsAbstract)
            {
                continue;
            }

            var model = Describe(spc, type, runtime);
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

        foreach (var island in islands)
        {
            spc.AddSource($"{island.Fqn.Replace("global::", string.Empty)}.External.g.cs",
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

    private static ComponentModel? Describe(SourceProductionContext spc, INamedTypeSymbol type, string runtime)
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
        // JS already are. React implies .tsx; Lit implies .ts, because a Lit component is ordinary
        // TypeScript — which the base class has now said, so it no longer has to be declared twice.
        var declaredModule = DeclaredModule(spc, type, location);
        if (declaredModule is { Failed: true })
        {
            return null;
        }

        var module = declaredModule?.Value ?? $"./{type.Name}.{(runtime == "lit" ? "ts" : "tsx")}";

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
            DeclaresModule = declaredModule is not null,
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
                    Rask056,
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

    /// <summary>
    ///     The module a component names by overriding <c>Module</c>, or null when it does not.
    /// </summary>
    /// <remarks>
    ///     Read out of the SYNTAX rather than evaluated, because the value is needed at build time —
    ///     the bundler generates one entry module per component long before any of this code could run.
    ///     So only a literal will do, and anything else is RASK059 rather than a module specifier the
    ///     browser resolves to nothing.
    /// </remarks>
    private static ModuleOverride? DeclaredModule(
        SourceProductionContext spc,
        INamedTypeSymbol type,
        Location? fallback)
    {
        var property = type.GetMembers("Module").OfType<IPropertySymbol>().FirstOrDefault();
        if (property is null)
        {
            return null;
        }

        var location = property.Locations.FirstOrDefault(l => l.IsInSource) ?? fallback;

        var syntax = property.DeclaringSyntaxReferences
            .Select(r => r.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault();

        if (Literal(syntax?.ExpressionBody?.Expression) is { } arrow)
        {
            return new ModuleOverride(arrow, false);
        }

        if (Literal(syntax?.Initializer?.Value) is { } initializer)
        {
            return new ModuleOverride(initializer, false);
        }

        // A getter body — `get => "…";` or `get { return "…"; }` — reads identically to the author, so
        // accepting only the two forms above would be an arbitrary distinction.
        var getter = syntax?.AccessorList?.Accessors
            .FirstOrDefault(a => a.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GetAccessorDeclaration));

        if (Literal(getter?.ExpressionBody?.Expression) is { } getterArrow)
        {
            return new ModuleOverride(getterArrow, false);
        }

        var returned = getter?.Body?.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
        if (getter?.Body?.Statements.Count == 1 && Literal(returned?.Expression) is { } returnedLiteral)
        {
            return new ModuleOverride(returnedLiteral, false);
        }

        spc.ReportDiagnostic(Diagnostic.Create(Rask059, location, type.Name));
        return new ModuleOverride(string.Empty, true);
    }

    /// <summary>The text of a string literal expression, or null for anything else.</summary>
    private static string? Literal(ExpressionSyntax? expression) =>
        expression is LiteralExpressionSyntax literal
        && literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    /// <summary>The callback shape a delegate prop takes, or null when it is not a callback at all.</summary>
    /// <remarks>
    ///     The four shapes Rask already auto-wraps. A delegate outside the set falls through to the wire
    ///     classifier, which rejects it with RASK057 — better than silently dropping a prop the author
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


    private static string Emit(ComponentModel island)
    {
        var emitter = new PropsWriterEmitter();
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
                            + $"global::Rask.External.ExternalHandlers.Register(this, this.{handler.ClrName}));");
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

        // Only what the compiler alone knows. Everything else — the host element, the opaque boundary,
        // the slot grouping, the hydration property, the runtime script, the attribute writer — is
        // hand-written on ExternalComponent, where it can be read.
        sb.AppendLine("    /// <summary>The name the client runtime resolves this component's module by.</summary>");
        sb.AppendLine($"    protected override string ComponentName => \"{island.Name}\";");
        sb.AppendLine();

        if (!island.DeclaresModule)
        {
            sb.AppendLine("    /// <summary>The front-end file beside this one, paired by filename.</summary>");
            sb.AppendLine($"    protected override string Module => \"{island.Module}\";");
            sb.AppendLine();
        }

        sb.AppendLine("    /// <summary>The props, as the JSON the client runtime hands to the adapter.</summary>");
        sb.AppendLine("    protected override string WriteProps()");
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

    private sealed record CallbackShape(ITypeSymbol? Argument, bool IsAsync);

    private sealed record IslandProp(string ClrName, string WireName, WireType Wire);

    private sealed record IslandHandler(string ClrName, string WireName, CallbackShape Shape);

    /// <summary>A <c>Module</c> override's literal value, or a marker that it could not be read.</summary>
    private sealed record ModuleOverride(string Value, bool Failed);

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

        public List<IslandProp> Props { get; } = new();
        public List<IslandHandler> Handlers { get; } = new();
    }
}
