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
                     + "or classes composed of the same. Callbacks are supported as Action, Action<T>, Func<Task> "
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

        if (islands.Count > 0)
        {
            spc.AddSource("RaskExternalGeneratedTypeScript.g.cs",
                SourceText.From(TypeScriptCarrier(islands), Encoding.UTF8));
        }
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

            model.Props.Add(new IslandProp(property.Name, wireName, wire,
                property.Type.NullableAnnotation == NullableAnnotation.Annotated));
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
            // A callback with an argument is registered as a WRAPPER, not as itself. The dispatcher
            // has no general Action<T> case and cannot have one — T is only known where the component
            // is compiled — so the raw delegate fell through to a DynamicInvoke with no arguments and
            // threw on the first click. The wrapper reads the argument here, where the type is known.
            var registered = handler.Shape.Argument is null
                ? $"this.{handler.ClrName}"
                : $"__Arg{handler.ClrName}";

            body.AppendLine("                writer.WriteString(\"$h\", "
                            + $"global::Rask.External.ExternalHandlers.Register(this, {registered}));");
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

        foreach (var handler in island.Handlers)
        {
            if (handler.Shape.Argument is null)
            {
                continue;
            }

            var read = ScalarRead(handler.Shape.Argument);
            var type = handler.Shape.Argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var invoke = handler.Shape.IsAsync
                ? $"global::System.Func<global::System.Text.Json.JsonElement, global::System.Threading.Tasks.Task>"
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
            // from a void-returning lambda is CS8030.
            sb.AppendLine(handler.Shape.IsAsync
                ? $"        return this.{handler.ClrName}!(__v);"
                : $"        this.{handler.ClrName}!(__v);");
            sb.AppendLine("    };");
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

    private sealed record CallbackShape(ITypeSymbol? Argument, bool IsAsync);

    private sealed record IslandProp(string ClrName, string WireName, WireType Wire, bool IsNullable);

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
