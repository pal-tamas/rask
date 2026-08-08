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

namespace Rask.Generators;

/// <summary>
///     Pairs sibling <c>.js</c> files with their component classes (e.g.
///     <c>Counter.cs</c> ↔ <c>Counter.js</c>) and emits a module initializer that
///     registers the JS source into <c>ScopedJsRegistry</c>. The author writes
///     idiomatic ES-module syntax (<c>export function mount(el) { ... }</c>); the
///     registry wraps the body in a <c>Rask.scoped.register(...)</c> call at runtime.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ComponentScopedJsGenerator : IIncrementalGenerator
{
    private const string ComponentFullName = "Rask.Core.Component";

    private static readonly DiagnosticDescriptor Rask017 = new(
        "RASK017",
        "Orphan scoped-JS file",
        "Scoped-JS file '{0}' has no matching component class — add a Component subclass named '{1}' in the "
        + "same folder, rename the file to match one, or set "
        + "<RaskScopedJsAutoInclude>false</RaskScopedJsAutoInclude> if it is a plain wwwroot script",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "As RASK015, for a '{Name}.js' sibling: scoped JS is matched by file name and folder, so a script "
                     + "with no component of that name beside it is registered against nothing.",
        helpLinkUri: DiagnosticHelp.Link("RASK017"));

    private static readonly DiagnosticDescriptor Rask018 = new(
        "RASK018",
        "Ambiguous scoped-JS match",
        "Scoped-JS file '{0}' matches multiple component classes named '{1}': {2}. Move one to disambiguate.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "As RASK016, for scoped JS: two components of the same name in one folder make the pairing "
                     + "arbitrary.",
        helpLinkUri: DiagnosticHelp.Link("RASK018"));

    private static readonly DiagnosticDescriptor Rask020 = new(
        "RASK020",
        "Scoped-JS simple-name collision",
        "Two or more components with scoped JS share the simple type name '{0}': {1}. The browser-side namespace key window.Rask[\"{0}\"] is shared by all of them and the last registration silently wins. Rename one, move it to a differently-named sibling, or expose your exports under a sub-namespace inside the JS file. Promote to error in csproj with <WarningsAsErrors>RASK020</WarningsAsErrors>.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Scoped JS is exposed to the browser as window.Rask['Name'], keyed on the component's SIMPLE name "
                     + "— so two components of the same name in different namespaces share one key and the last one "
                     + "registered wins, silently. Promote to an error with "
                     + "<WarningsAsErrors>RASK020</WarningsAsErrors>.",
        helpLinkUri: DiagnosticHelp.Link("RASK020"));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var components = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c
                                    && c.BaseList is { Types.Count: > 0 }
                                    && !c.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                static (ctx, _) => GetComponentInfo(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!.Value);

        var jsFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            .Select(static (f, ct) => new JsFile(f.Path, f.GetText(ct)?.ToString() ?? string.Empty));

        var combined = components.Collect().Combine(jsFiles.Collect());

        context.RegisterSourceOutput(combined,
            static (spc, t) => Emit(spc, t.Left, t.Right));
    }

    private static ComponentInfo? GetComponentInfo(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (symbol.IsAbstract || symbol.IsGenericType || symbol.IsUnboundGenericType)
        {
            return null;
        }

        if (!InheritsFromComponent(symbol))
        {
            return null;
        }

        var path = classDecl.SyntaxTree.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var fqn = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return new ComponentInfo(symbol.Name, fqn, path);
    }

    private static bool InheritsFromComponent(INamedTypeSymbol symbol)
    {
        for (var t = symbol.BaseType; t is not null; t = t.BaseType)
        {
            if (t.OriginalDefinition.ToDisplayString() == ComponentFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static void Emit(
        SourceProductionContext spc,
        ImmutableArray<ComponentInfo> components,
        ImmutableArray<JsFile> jsFiles)
    {
        if (jsFiles.IsDefaultOrEmpty)
        {
            return;
        }

        var byDirAndName = new Dictionary<string, List<ComponentInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in components)
        {
            var dir = NormalizeDirectory(c.FilePath);
            var key = MakeKey(dir, c.TypeName);
            if (!byDirAndName.TryGetValue(key, out var list))
            {
                list = new List<ComponentInfo>(1);
                byDirAndName[key] = list;
            }

            list.Add(c);
        }

        var pairs = new List<(ComponentInfo Component, string Js)>();
        var emittedFqns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var js in jsFiles)
        {
            if (string.IsNullOrWhiteSpace(js.Contents))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(js.Path);
            if (string.IsNullOrEmpty(stem))
            {
                continue;
            }

            var dir = NormalizeDirectory(js.Path);
            var key = MakeKey(dir, stem);

            if (!byDirAndName.TryGetValue(key, out var matches) || matches.Count == 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask017,
                    Location.None,
                    js.Path,
                    stem));
                continue;
            }

            if (matches.Count > 1)
            {
                var fqns = string.Join(", ", matches.Select(m => m.FullyQualifiedName));
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask018,
                    Location.None,
                    js.Path,
                    stem,
                    fqns));
                continue;
            }

            var match = matches[0];
            if (!emittedFqns.Add(match.FullyQualifiedName))
            {
                continue;
            }

            pairs.Add((match, js.Contents));
        }

        if (pairs.Count == 0)
        {
            return;
        }

        // RASK020 — detect simple-name collisions across registered components. Two
        // components in different namespaces with the same simple type name compete for
        // the same window.Rask[{SimpleName}] slot; the last registration wins silently.
        // This catches the collision at build time so users get a chance to rename or
        // namespace their JS-side exports before runtime surprises hit.
        var bySimpleName = pairs
            .GroupBy(p => p.Component.TypeName, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);
        foreach (var collisionGroup in bySimpleName)
        {
            var fqns = string.Join(", ",
                collisionGroup.Select(p => p.Component.FullyQualifiedName));
            spc.ReportDiagnostic(Diagnostic.Create(
                Rask020,
                Location.None,
                collisionGroup.Key,
                fqns));
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("internal static class __RaskScopedJsRegistration");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Init() => RefreshAll();");
        sb.AppendLine();
        sb.AppendLine("    internal static void RefreshAll()");
        sb.AppendLine("    {");

        foreach (var (component, js) in pairs.OrderBy(p => p.Component.FullyQualifiedName, StringComparer.Ordinal))
        {
            sb.Append("        global::Rask.Core.ScopedAssets.ScopedAssetRegistry.RegisterJs(typeof(")
                .Append(component.FullyQualifiedName)
                .Append("), ");
            AppendVerbatimStringLiteral(sb, js);
            sb.AppendLine(");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("__RaskScopedJsRegistration.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string NormalizeDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        return dir.Replace('\\', '/');
    }

    private static string MakeKey(string dir, string name) =>
        dir.Length == 0 ? name : dir + "/" + name;

    private static void AppendVerbatimStringLiteral(StringBuilder sb, string value)
    {
        sb.Append("@\"");
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                sb.Append("\"\"");
            }
            else
            {
                sb.Append(ch);
            }
        }

        sb.Append('"');
    }

    private readonly record struct ComponentInfo(string TypeName, string FullyQualifiedName, string FilePath);

    private readonly record struct JsFile(string Path, string Contents);
}
