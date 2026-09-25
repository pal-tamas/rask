using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.ScopedScripts;

/// <summary>
///     Gives a component a typed private method for every function its scoped TypeScript exports, so it
///     calls its own script the way it calls its own code: <c>await Width(_box)</c> in place of
///     <c>js.InvokeAsync&lt;double&gt;("Rask.Card.width", _box)</c>. An exported class becomes a nested
///     proxy with a <c>New{Class}(…)</c> method; an interface becomes a nested record.
/// </summary>
/// <remarks>
///     The signatures come from the declaration file tsgo writes beside the compiled script (see
///     <c>Rask.Core.targets</c>), paired to the component exactly as the script itself is —
///     <see cref="AssetPairing" />, same folder and name. Pairing errors (RASK017/018/020) are
///     <c>ComponentScopedJsGenerator</c>'s; this generator stays silent on a file it cannot pair.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ScopedScriptCallsGenerator : IIncrementalGenerator
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string ExternalComponentFullName = "Rask.External.ExternalComponent";
    private const string SourceMetadataKey = "build_metadata.AdditionalFiles.RaskTsSource";

    private const string Runtime = "global::Rask.Core.ScopedAssets.ScopedScript";
    private const string ValueTask = "global::System.Threading.Tasks.ValueTask";

    internal static readonly DiagnosticDescriptor Rask094 = new(
        "RASK094",
        "Scoped TypeScript export has no typed method",
        "'{0}' in '{1}' gets no typed method on {2}: {3}",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Each function and class a component's scoped TypeScript exports becomes a typed private "
                     + "member of the component. One that cannot is left out and reported here, with the reason; "
                     + "the string call through IJSRuntime still reaches it.",
        helpLinkUri: DiagnosticHelp.Link("RASK094"));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var components = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c
                                    && c.BaseList is { Types.Count: > 0 }
                                    && !c.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                static (ctx, _) => GetComponent(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!.Value);

        // The framework's own member names, which a generated method must not hide. Read once from the
        // compilation rather than per component: Component alone has hundreds, and every component would
        // otherwise carry them through the incremental cache.
        var frameworkNames = context.CompilationProvider.Select(static (compilation, _) =>
        {
            // Only what the app can reach: hiding a private or internal member is CS0109, an error under
            // warnings-as-errors in generated code the author cannot touch.
            var names = new SortedSet<string>(StringComparer.Ordinal);
            for (var t = compilation.GetTypeByMetadataName(ComponentFullName); t is not null; t = t.BaseType)
            {
                foreach (var member in t.GetMembers())
                {
                    var reachable = member.DeclaredAccessibility switch
                    {
                        Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal => true,
                        Accessibility.Internal or Accessibility.ProtectedAndInternal =>
                            member.ContainingAssembly.GivesAccessTo(compilation.Assembly),
                        _ => false,
                    };
                    if (reachable && member.CanBeReferencedByName)
                    {
                        names.Add(member.Name);
                    }
                }
            }

            return string.Join("\n", names);
        });

        var declarations = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, ct) =>
            {
                var (file, options) = pair;
                options.GetOptions(file).TryGetValue(SourceMetadataKey, out var source);
                return new DeclarationFile(source ?? string.Empty, file.GetText(ct)?.ToString() ?? string.Empty);
            })
            .Where(static d => d.SourcePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase));

        var combined = components.Collect().Combine(declarations.Collect()).Combine(frameworkNames);
        context.RegisterSourceOutput(combined, static (spc, t) => Emit(spc, t.Left.Left, t.Left.Right, t.Right));
    }

    private static ComponentInfo? GetComponent(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax decl
            || ctx.SemanticModel.GetDeclaredSymbol(decl) is not INamedTypeSymbol symbol
            || symbol.IsAbstract || symbol.IsGenericType)
        {
            return null;
        }

        // A partial class declared in several files is seen once per file; the one beside the script is
        // the one that pairs, and the others simply find no file of their name.
        var path = decl.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var isComponent = false;
        var isExternal = false;
        var ownNames = new SortedSet<string>(StringComparer.Ordinal) { symbol.Name };
        ownNames.UnionWith(symbol.MemberNames);
        for (var t = symbol.BaseType; t is not null; t = t.BaseType)
        {
            var name = t.OriginalDefinition.ToDisplayString();
            if (name == ComponentFullName)
            {
                isComponent = true;
                break;
            }

            isExternal |= name == ExternalComponentFullName;
            ownNames.UnionWith(t.MemberNames);
        }

        if (!isComponent)
        {
            return null;
        }

        var partial = IsPartial(symbol);
        var containers = new List<string>();
        for (var outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            partial &= IsPartial(outer) && !outer.IsGenericType;
            var keyword = outer.IsRecord
                ? outer.TypeKind == TypeKind.Struct ? "record struct" : "record"
                : outer.TypeKind == TypeKind.Struct ? "struct" : "class";
            containers.Insert(0, keyword + " " + outer.Name);
        }

        var ns = symbol.ContainingNamespace is { IsGlobalNamespace: false } n ? n.ToDisplayString() : string.Empty;
        return new ComponentInfo(
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            path,
            ns,
            string.Join("\n", containers),
            partial,
            isExternal,
            string.Join("\n", ownNames));
    }

    private static bool IsPartial(INamedTypeSymbol symbol) =>
        symbol.DeclaringSyntaxReferences.All(r =>
            r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

    private static void Emit(
        SourceProductionContext spc,
        ImmutableArray<ComponentInfo> components,
        ImmutableArray<DeclarationFile> files,
        string frameworkNames)
    {
        if (files.IsDefaultOrEmpty)
        {
            return;
        }

        var byKey = new Dictionary<string, List<ComponentInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in components)
        {
            var key = AssetPairing.MakeKey(AssetPairing.NormalizeDirectory(c.FilePath), c.TypeName);
            if (!byKey.TryGetValue(key, out var list))
            {
                byKey[key] = list = new List<ComponentInfo>(1);
            }

            if (!list.Any(x => x.FullyQualifiedName == c.FullyQualifiedName))
            {
                list.Add(c);
            }
        }

        var reserved = new HashSet<string>(frameworkNames.Split('\n'), StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files.OrderBy(f => f.SourcePath, StringComparer.Ordinal))
        {
            var stem = Path.GetFileNameWithoutExtension(file.SourcePath);
            var key = AssetPairing.MakeKey(AssetPairing.NormalizeDirectory(file.SourcePath), stem);
            if (!byKey.TryGetValue(key, out var matches) || matches.Count != 1 || matches[0].IsExternal
                || !emitted.Add(matches[0].FullyQualifiedName))
            {
                continue;
            }

            var component = matches[0];
            var source = Generate(spc, component, file, reserved);
            if (source is not null)
            {
                var hint = component.FullyQualifiedName.Replace("global::", string.Empty).Replace('<', '_').Replace('>', '_');
                spc.AddSource(hint + ".ScopedScript.g.cs", SourceText.From(source, Encoding.UTF8));
            }
        }
    }

    private static string? Generate(
        SourceProductionContext spc, ComponentInfo component, DeclarationFile file, HashSet<string> reserved)
    {
        var decls = DeclarationReader.Read(file.Contents);
        var location = Location.Create(file.SourcePath, new TextSpan(0, 0), new LinePositionSpan(default, default));
        var fileName = Path.GetFileName(file.SourcePath);

        void Report(string export, string reason) =>
            spc.ReportDiagnostic(Diagnostic.Create(Rask094, location, export, fileName, component.TypeName, reason));

        foreach (var name in decls.NotCallable)
        {
            Report(name, "it is a value, not a function — only functions (a declaration or an arrow in a const) and classes reach C#");
        }

        if (decls.Functions.Count == 0 && decls.Classes.Count == 0)
        {
            return null;
        }

        if (!component.IsPartial)
        {
            var first = decls.Functions.Count > 0 ? decls.Functions[0].Name : decls.Classes[0].Name;
            Report(first, "declare the class 'partial' (and any class it is nested in) so Rask can add its script's methods to it");
            return null;
        }

        // Two kinds of name a generated member can meet. One the component's own code declares is a real
        // clash and is reported. One it only INHERITS — a markup entry such as SVG's `Stop`, a framework
        // member — is hidden with `new`, the way a component's own member hides one today (`Markup.Stop`
        // still reaches the tag); otherwise a script could not export `stop()` or `filter()` at all.
        var taken = new HashSet<string>(component.MemberNames.Split('\n'), StringComparer.Ordinal);
        string Access(string name) => reserved.Contains(name) ? "private new" : "private";

        // An interface's record takes its name before any function does: `interface Viewport` and
        // `function viewport()` are one C# name, and the record is what a signature refers to.
        var recordNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var alias in decls.Aliases.Where(a => a.Value is TsObject).ToList())
        {
            var pascal = Names.Pascal(alias.Key);
            if (pascal is null || taken.Contains(pascal))
            {
                decls.Aliases[alias.Key] = new TsUnsupported(pascal is null
                    ? $"'{alias.Key}', whose name is not a C# identifier"
                    : $"'{alias.Key}', whose C# name '{pascal}' {component.TypeName} already uses");
                continue;
            }

            recordNames[pascal] = alias.Key;
        }

        var mapper = new TypeMapper(decls);
        var body = new StringBuilder();
        var identifier = "Rask." + component.TypeName + ".";

        var overloaded = new HashSet<string>(
            decls.Functions.GroupBy(f => f.Name).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.Ordinal);

        // Classes first: a function's signature may name one, and a class that could not be generated
        // must fail the functions that use it rather than leave them pointing at nothing.
        var proxies = new StringBuilder();
        foreach (var cls in decls.Classes)
        {
            var csName = Names.Pascal(cls.Name);
            if (cls.Generic)
            {
                Report(cls.Name, "a generic class has no single C# shape");
                mapper.Classes.Remove(cls.Name);
                continue;
            }

            var clash = csName is null ? "its name is not a C# identifier"
                : recordNames.TryGetValue(csName, out var shape) ? $"the interface '{shape}' already takes the C# name '{csName}' — rename one of them"
                : !taken.Add(csName) ? $"{component.TypeName} already has a member named '{csName}' — rename one of them"
                : null;
            if (clash is not null)
            {
                Report(cls.Name, clash);
                mapper.Classes.Remove(cls.Name);
                continue;
            }

            proxies.Append(EmitProxy(cls, csName!, Access(csName!), mapper, Report));

            if (cls.Constructors.Count == 1)
            {
                var ctor = new TsFunctionDecl("New" + csName, cls.Constructors[0], new TsNamed(cls.Name), false, cls.Doc);
                var newName = "New" + csName;
                if (!taken.Add(newName))
                {
                    Report(cls.Name, $"{component.TypeName} already has a member named '{newName}'");
                }
                else
                {
                    var method = EmitMethod(ctor, newName, identifier + "__new_" + cls.Name, mapper, Owner.Component,
                        $"Creates a <c>{cls.Name}</c> from <c>{fileName}</c>.", out var error,
                        accessibility: Access(newName));
                    if (method is null)
                    {
                        Report(cls.Name, error!);
                    }
                    else
                    {
                        body.Append(method);
                    }
                }
            }
            else if (cls.Constructors.Count > 1)
            {
                Report(cls.Name, "its constructor is overloaded, so there is no one New method to write — keep one signature");
            }
        }

        foreach (var fn in decls.Functions)
        {
            if (overloaded.Contains(fn.Name))
            {
                if (fn == decls.Functions.First(f => f.Name == fn.Name))
                {
                    Report(fn.Name, "it is overloaded — keep one signature (optional parameters cover most overloads)");
                }

                continue;
            }

            var csName = Names.Pascal(fn.Name);
            if (csName is null)
            {
                Report(fn.Name, "its name is not a C# identifier");
                continue;
            }

            if (recordNames.TryGetValue(csName, out var shape))
            {
                Report(fn.Name, $"the interface '{shape}' already takes the C# name '{csName}' — rename the function (say, 'read{csName}')");
                continue;
            }

            if (!taken.Add(csName))
            {
                Report(fn.Name, $"{component.TypeName} already has a member named '{csName}' — rename one of them");
                continue;
            }

            var method = EmitMethod(fn, csName, identifier + fn.Name, mapper, Owner.Component,
                $"Calls <c>{fn.Name}</c> in <c>{fileName}</c>.", out var error, accessibility: Access(csName));
            if (method is null)
            {
                Report(fn.Name, error!);
                continue;
            }

            body.Append(method);
        }

        var records = new StringBuilder();
        foreach (var record in mapper.Records)
        {
            records.Append(EmitRecord(record, Access(record.CsName)));
        }

        if (body.Length == 0 && proxies.Length == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1591");
        sb.AppendLine();
        if (component.Namespace.Length > 0)
        {
            sb.Append("namespace ").Append(component.Namespace).AppendLine(";");
            sb.AppendLine();
        }

        var containers = component.Containers.Length == 0 ? Array.Empty<string>() : component.Containers.Split('\n');
        foreach (var container in containers)
        {
            sb.Append("partial ").AppendLine(container).AppendLine("{");
        }

        sb.Append("partial class ").AppendLine(component.TypeName).AppendLine("{");
        sb.Append(body);
        sb.Append(proxies);
        sb.Append(records);
        sb.AppendLine("}");
        foreach (var _ in containers)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private enum Owner
    {
        Component,
        Proxy,
    }

    private static string EmitProxy(
        TsClassDecl cls, string csName, string accessibility, TypeMapper mapper, Action<string, string> report)
    {
        var sb = new StringBuilder();
        AppendSummary(sb, "    ", cls.Doc, $"The <c>{cls.Name}</c> class of this component's script. Released when the component unmounts.");
        sb.Append("    ").Append(accessibility).Append(" sealed class ").Append(csName).AppendLine(" : global::Rask.Core.ScopedAssets.ScriptObject");
        sb.AppendLine("    {");
        sb.Append("        internal ").Append(csName)
            .AppendLine("(global::Microsoft.JSInterop.IJSObjectReference reference) : base(reference) { }");

        var taken = new HashSet<string>(StringComparer.Ordinal)
        {
            csName, "Reference", "DisposeAsync", "CallScript", "CallScriptObject", "ScriptCallback",
            "Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone", "Finalize",
        };
        foreach (var group in cls.Methods.GroupBy(m => m.Name))
        {
            var method = group.First();
            var member = cls.Name + "." + method.Name;
            if (group.Count() > 1)
            {
                report(member, "it is overloaded — keep one signature");
                continue;
            }

            var name = Names.Pascal(method.Name);
            if (name is null || !taken.Add(name))
            {
                report(member, name is null ? "its name is not a C# identifier" : $"'{name}' is already taken on the proxy");
                continue;
            }

            var text = EmitMethod(method, name, method.Name, mapper, Owner.Proxy,
                $"Calls <c>{method.Name}</c> on this instance.", out var error, "        ", "public");
            if (text is null)
            {
                report(member, error!);
                continue;
            }

            sb.Append(text);
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string? EmitMethod(
        TsFunctionDecl fn,
        string csName,
        string identifier,
        TypeMapper mapper,
        Owner owner,
        string fallbackSummary,
        out string? error,
        string indent = "    ",
        string accessibility = "private")
    {
        error = null;
        if (fn.Generic)
        {
            error = "a generic function has no single C# signature";
            return null;
        }

        var parameters = new List<(MappedParameter Mapped, string Name)>();
        var args = new List<string>();
        var records = new List<string>();
        foreach (var p in fn.Parameters)
        {
            var mapped = mapper.MapParameter(p, owner == Owner.Proxy);
            if (mapped.Error is not null)
            {
                error = $"parameter '{p.Name}' is {mapped.Error}";
                return null;
            }

            var name = Names.Parameter(p.Name);
            parameters.Add((mapped, name));
            args.Add(string.Format(mapped.ArgFormat!, name));
            records.AddRange(mapped.Records);
        }

        var ret = mapper.MapReturn(fn.Returns);
        if (ret.Error is not null)
        {
            error = $"it returns {ret.Error}";
            return null;
        }

        records.AddRange(ret.Records);

        var sb = new StringBuilder();
        var (summary, paramDocs, returnsDoc) = DocComment.Split(fn.Doc);
        sb.Append(indent).Append("/// <summary>").Append(Xml(summary.Length > 0 ? summary : null) ?? fallbackSummary).AppendLine("</summary>");
        foreach (var p in fn.Parameters)
        {
            if (paramDocs.TryGetValue(p.Name, out var text) && text.Length > 0)
            {
                sb.Append(indent).Append("/// <param name=\"").Append(Names.Parameter(p.Name).TrimStart('@')).Append("\">")
                    .Append(Xml(text)).AppendLine("</param>");
            }
        }

        if (returnsDoc is { Length: > 0 })
        {
            sb.Append(indent).Append("/// <returns>").Append(Xml(returnsDoc)).AppendLine("</returns>");
        }

        // Keep a record's members through trimming: it crosses to the script by reflection over its
        // runtime type, which nothing else in a trimmed app roots.
        foreach (var record in records.Distinct())
        {
            sb.Append(indent)
                .Append("[global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors, typeof(")
                .Append(record).AppendLine("))]");
        }

        var argArray = args.Count == 0
            ? "global::System.Array.Empty<object?>()"
            : "new object?[] { " + string.Join(", ", args) + " }";

        // An unset optional argument must reach the script as `undefined`, which JSON cannot carry - so the
        // trailing ones are dropped instead of sent as null, and the script's own defaults apply.
        var required = fn.Parameters.TakeWhile(p => !p.Optional).Count();
        if (required < fn.Parameters.Count)
        {
            argArray = $"{Runtime}.Trim({argArray}, {required})";
        }

        var returnType = ret.Kind == ReturnKind.Void ? ValueTask : ValueTask + "<" + ret.Type + ">";
        // A tuple arrives as a JSON array; the generated reader takes it apart with each element's own type.
        var readTuple = ret.Kind == ReturnKind.Tuple
            ? $"static __r => ({string.Join(", ", ret.Elements.Select((t, i) => $"{Runtime}.Item<{t}>(__r, {i})"))})"
            : null;

        string call;
        if (owner == Owner.Component)
        {
            call = ret.Kind switch
            {
                ReturnKind.Tuple => $"{Runtime}.Tuple<{ret.Type}>(this, \"{identifier}\", {readTuple}, {argArray})",
                ReturnKind.Void => $"{Runtime}.Call(this, \"{identifier}\", {argArray})",
                ReturnKind.Object => $"{Runtime}.Object(this, \"{identifier}\", static __r => new {ret.Type}(__r), {argArray})",
                _ => $"{Runtime}.Call<{ret.Type}>(this, \"{identifier}\", {argArray})",
            };
        }
        else
        {
            call = ret.Kind switch
            {
                ReturnKind.Tuple => $"CallScriptTuple<{ret.Type}>(\"{identifier}\", {readTuple}, {argArray})",
                ReturnKind.Void => $"CallScript(\"{identifier}\", {argArray})",
                ReturnKind.Object => $"CallScriptObject(\"{identifier}\", static __r => new {ret.Type}(__r), {argArray})",
                _ => $"CallScript<{ret.Type}>(\"{identifier}\", {argArray})",
            };
        }

        // One overload per sync/async choice of each callback parameter — see TypeMapper.MapCallback.
        var header = sb.ToString();
        sb.Clear();
        var callbacks = parameters.Select((p, i) => (p, i)).Where(x => x.p.Mapped.AsyncType is not null)
            .Select(x => x.i).ToList();
        for (var mask = 0; mask < 1 << callbacks.Count; mask++)
        {
            var list = new string[parameters.Count];
            var defaulted = true;
            for (var i = parameters.Count - 1; i >= 0; i--)
            {
                var (mapped, name) = parameters[i];
                var slot = callbacks.IndexOf(i);
                var type = slot >= 0 && (mask & (1 << slot)) != 0 ? mapped.AsyncType : mapped.Type;

                // Only the all-sync overload keeps a callback's default, so a call that omits it binds to
                // exactly one; and a default is dropped from anything left of a parameter that has none.
                defaulted &= mapped.Optional && (slot < 0 || mask == 0);
                list[i] = type + " " + name + (defaulted ? mapped.DefaultValue : string.Empty);
            }

            sb.Append(header);
            sb.Append(indent).Append(accessibility).Append(' ').Append(returnType).Append(' ').Append(csName)
                .Append('(').Append(string.Join(", ", list)).AppendLine(") =>");
            sb.Append(indent).Append("    ").Append(call).AppendLine(";");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EmitRecord(RecordShape record, string accessibility)
    {
        var sb = new StringBuilder();
        AppendSummary(sb, "    ", record.Doc, $"The <c>{record.TsName}</c> shape of this component's script.");
        sb.Append("    ").Append(accessibility).Append(" sealed record ").AppendLine(record.CsName);
        sb.AppendLine("    {");
        foreach (var p in record.Properties)
        {
            sb.Append("        [global::System.Text.Json.Serialization.JsonPropertyName(\"").Append(p.JsonName).AppendLine("\")]");
            if (!p.Required)
            {
                // Left out when unset, so the script reads `undefined` for it as it would for its own optional field.
                sb.AppendLine("        [global::System.Text.Json.Serialization.JsonIgnore(Condition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]");
            }

            sb.Append("        public ").Append(p.Required ? "required " : string.Empty).Append(p.Type).Append(' ')
                .Append(p.CsName).AppendLine(" { get; init; }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void AppendSummary(StringBuilder sb, string indent, string? doc, string fallback)
    {
        var summary = DocComment.Split(doc).Summary;
        sb.Append(indent).Append("/// <summary>").Append(summary.Length > 0 ? Xml(summary) : fallback).AppendLine("</summary>");
    }

    // JSDoc is Markdown-ish: `code` spans become <c>, which is what an IDE renders as code.
    private static string? Xml(string? text) =>
        text is null
            ? null
            : System.Text.RegularExpressions.Regex.Replace(
                text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", string.Empty).Replace("\n", " "),
                "`([^`]+)`",
                "<c>$1</c>");

    private readonly record struct ComponentInfo(
        string TypeName,
        string FullyQualifiedName,
        string FilePath,
        string Namespace,
        string Containers,
        bool IsPartial,
        bool IsExternal,
        string MemberNames);

    private readonly record struct DeclarationFile(string SourcePath, string Contents);
}
