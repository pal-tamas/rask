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
///     Pairs sibling <c>.css</c> files with their component classes (e.g.
///     <c>Counter.cs</c> ↔ <c>Counter.css</c>) and emits a module initializer that registers
///     the CSS into <c>ScopedCssRegistry</c>. Replaces the inline
///     <c>protected override string? Css =&gt; "..."</c> pattern.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ComponentScopedCssGenerator : IIncrementalGenerator
{
    private const string ComponentFullName = "Rask.Core.Component";

    private static readonly DiagnosticDescriptor Rask015 = new(
        "RASK015",
        "Orphan scoped-CSS file",
        "Scoped-CSS file '{0}' has no matching component class — add a Component subclass named '{1}' in the "
        + "same folder, rename the file to match one, or set "
        + "<RaskScopedCssAutoInclude>false</RaskScopedCssAutoInclude> if it is a global stylesheet",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK015"));

    private static readonly DiagnosticDescriptor Rask016 = new(
        "RASK016",
        "Ambiguous scoped-CSS match",
        "Scoped-CSS file '{0}' matches multiple component classes named '{1}': {2}. Move one to disambiguate.",
        "Rask.Generators",
        DiagnosticSeverity.Error,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK016"));

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

        var cssFiles = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            .Select(static (f, ct) => new CssFile(f.Path, f.GetText(ct)?.ToString() ?? string.Empty));

        var combined = components.Collect().Combine(cssFiles.Collect());

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
        ImmutableArray<CssFile> cssFiles)
    {
        if (cssFiles.IsDefaultOrEmpty)
        {
            return;
        }

        // Index components by (directory, simple-name). A `.css` file at /Pages/Foo.css
        // pairs with the Component subclass whose .cs lives in /Pages/ and whose simple
        // type name is "Foo". Multiple matches → RASK016 (ambiguous).
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

        var pairs = new List<(ComponentInfo Component, string Css)>();
        var emittedFqns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var css in cssFiles)
        {
            if (string.IsNullOrWhiteSpace(css.Contents))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(css.Path);
            if (string.IsNullOrEmpty(stem))
            {
                continue;
            }

            var dir = NormalizeDirectory(css.Path);
            var key = MakeKey(dir, stem);

            if (!byDirAndName.TryGetValue(key, out var matches) || matches.Count == 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask015,
                    Location.None,
                    css.Path,
                    stem));
                continue;
            }

            if (matches.Count > 1)
            {
                var fqns = string.Join(", ", matches.Select(m => m.FullyQualifiedName));
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask016,
                    Location.None,
                    css.Path,
                    stem,
                    fqns));
                continue;
            }

            var match = matches[0];
            if (!emittedFqns.Add(match.FullyQualifiedName))
            {
                // Same component matched by two .css files (shouldn't happen unless two
                // different-cased filenames exist on a case-insensitive FS — defensive).
                continue;
            }

            pairs.Add((match, css.Contents));
        }

        if (pairs.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("internal static class __RaskScopedCssRegistration");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void Init() => RefreshAll();");
        sb.AppendLine();
        sb.AppendLine("    internal static void RefreshAll()");
        sb.AppendLine("    {");

        foreach (var (component, css) in pairs.OrderBy(p => p.Component.FullyQualifiedName, StringComparer.Ordinal))
        {
            sb.Append("        global::Rask.Core.ScopedAssets.ScopedAssetRegistry.RegisterCss(typeof(")
                .Append(component.FullyQualifiedName)
                .Append("), ");
            AppendVerbatimStringLiteral(sb, css);
            sb.AppendLine(");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("__RaskScopedCssRegistration.g.cs",
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

    private readonly record struct CssFile(string Path, string Contents);
}
