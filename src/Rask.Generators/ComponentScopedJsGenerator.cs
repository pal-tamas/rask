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
///     Pairs sibling <c>.ts</c> files with their component classes (e.g.
///     <c>Counter.cs</c> ↔ <c>Counter.ts</c>) and emits a module initializer that registers the
///     compiled JavaScript into <c>ScopedAssetRegistry</c>. The author writes idiomatic TypeScript
///     (<c>export function mount(el: HTMLElement) { … }</c>); the registry wraps the body at runtime
///     and exposes each export on <c>window.Rask["Counter"]</c>.
/// </summary>
/// <remarks>
///     <para>
///         The generator never sees the TypeScript. A source generator cannot run a compiler, so
///         <c>Rask.Core.targets</c> compiles each sibling <c>.ts</c> to <c>obj/…/rask/ts/</c> before
///         <c>CoreCompile</c> and hands the OUTPUT over as an <c>AdditionalFile</c>, tagged with the
///         path it came from.
///     </para>
///     <para>
///         Everything therefore keys off <b>that tag</b>, not off the file it is handed. Pairing a
///         component with the compiled path would look for <c>Counter.cs</c> inside <c>obj/</c> and
///         match nothing, and a diagnostic reported against it would name a generated file the
///         author never wrote.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ComponentScopedJsGenerator : IIncrementalGenerator
{
    private const string ComponentFullName = "Rask.Core.Component";

    /// <summary>The metadata carrying the <c>.ts</c> a compiled file came from.</summary>
    private const string SourceMetadataKey = "build_metadata.AdditionalFiles.RaskTsSource";

    /// <summary>The <c>.js</c> files found beside components, which Rask no longer compiles.</summary>
    private const string StrayJsPropertyKey = "build_property.RaskStrayScopedJs";

    private static readonly DiagnosticDescriptor Rask017 = new(
        "RASK017",
        "Orphan scoped-TS file",
        "Scoped-TS file '{0}' has no matching component class — add a Component subclass named '{1}' in the "
        + "same folder, rename the file to match one, or set "
        + "<RaskScopedTsAutoInclude>false</RaskScopedTsAutoInclude> if it is a plain wwwroot script",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "As RASK015, for a '{Name}.ts' sibling: scoped TypeScript is matched by file name and folder, so "
                     + "a script with no component of that name beside it is registered against nothing.",
        helpLinkUri: DiagnosticHelp.Link("RASK017"));

    private static readonly DiagnosticDescriptor Rask018 = new(
        "RASK018",
        "Ambiguous scoped-TS match",
        "Scoped-TS file '{0}' matches multiple component classes named '{1}': {2}. Move one to disambiguate.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "As RASK016, for scoped TypeScript: two components of the same name in one folder make the "
                     + "pairing arbitrary.",
        helpLinkUri: DiagnosticHelp.Link("RASK018"));

    private static readonly DiagnosticDescriptor Rask020 = new(
        "RASK020",
        "Scoped-TS simple-name collision",
        "Two or more components with scoped TypeScript share the simple type name '{0}': {1}. The browser-side namespace key window.Rask[\"{0}\"] is shared by all of them and the last registration silently wins. Rename one, move it to a differently-named sibling, or expose your exports under a sub-namespace inside the TypeScript file. Promote to error in csproj with <WarningsAsErrors>RASK020</WarningsAsErrors>.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Scoped TypeScript is exposed to the browser as window.Rask['Name'], keyed on the component's "
                     + "SIMPLE name — so two components of the same name in different namespaces share one key and the "
                     + "last one registered wins, silently. Promote to an error with "
                     + "<WarningsAsErrors>RASK020</WarningsAsErrors>.",
        helpLinkUri: DiagnosticHelp.Link("RASK020"));

    private static readonly DiagnosticDescriptor Rask054 = new(
        "RASK055",
        "Scoped JavaScript is no longer supported",
        "'{0}' sits beside component '{1}', and Rask no longer compiles or registers a scoped '.js' file — rename it "
        + "to '{1}.ts'. The body needs no change: TypeScript is a superset of JavaScript, so an existing module is "
        + "already valid, and it is compiled before the browser sees it.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        description: "Scoped component assets are TypeScript. A '.js' sibling is left where it is rather than quietly "
                     + "ignored, because a scoped script that stops being registered does not fail — it produces a "
                     + "component whose window.Rask methods are simply absent, which surfaces as a control that does "
                     + "nothing.",
        helpLinkUri: DiagnosticHelp.Link("RASK055"));

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

        // Combining with the options provider re-runs this on every keystroke, because the provider
        // is not equatable. That is only acceptable because the Select immediately projects into an
        // equatable record struct, so everything downstream still caches on content — the same
        // mitigation ComponentFactoryGenerator uses. Returning the provider itself, or an
        // ImmutableArray (which compares by reference), would silently re-run the whole generator
        // for every character typed.
        var assets = context.AdditionalTextsProvider
            .Where(static f => f.Path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, ct) =>
            {
                var (file, options) = pair;
                options.GetOptions(file).TryGetValue(SourceMetadataKey, out var source);
                return new ScopedAsset(
                    source ?? string.Empty,
                    file.GetText(ct)?.ToString() ?? string.Empty);
            })

            // A compiled scoped asset always carries the .ts it came from. Anything else in
            // @(AdditionalFiles) that happens to be a .js belongs to another package and is not
            // ours to register — the old .js glob would have claimed it.
            .Where(static a => a.SourcePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase));

        var strayJs = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
        {
            options.GlobalOptions.TryGetValue(StrayJsPropertyKey, out var value);
            return value ?? string.Empty;
        });

        var combined = components.Collect().Combine(assets.Collect()).Combine(strayJs);

        context.RegisterSourceOutput(combined,
            static (spc, t) => Emit(spc, t.Left.Left, t.Left.Right, t.Right));
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
        ImmutableArray<ScopedAsset> assets,
        string strayJs)
    {
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

        ReportStrayJavaScript(spc, byDirAndName, strayJs);

        if (assets.IsDefaultOrEmpty)
        {
            return;
        }

        var pairs = new List<(ComponentInfo Component, string Js, string SourcePath)>();
        var emittedFqns = new HashSet<string>(StringComparer.Ordinal);

        foreach (var asset in assets)
        {
            var stem = Path.GetFileNameWithoutExtension(asset.SourcePath);
            if (string.IsNullOrEmpty(stem))
            {
                continue;
            }

            var dir = NormalizeDirectory(asset.SourcePath);
            var key = MakeKey(dir, stem);

            if (!byDirAndName.TryGetValue(key, out var matches) || matches.Count == 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask017,
                    SourceLocation(asset.SourcePath),
                    asset.SourcePath,
                    stem));
                continue;
            }

            if (matches.Count > 1)
            {
                var fqns = string.Join(", ", matches.Select(m => m.FullyQualifiedName));
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask018,
                    SourceLocation(asset.SourcePath),
                    asset.SourcePath,
                    stem,
                    fqns));
                continue;
            }

            // Whitespace-only content is skipped AFTER pairing, so an empty file still reports
            // RASK017/018 rather than vanishing. It is also the state every scoped asset is in
            // before the first real build — a design-time build does not run the compiler — so
            // this must not be an error.
            if (string.IsNullOrWhiteSpace(asset.Contents))
            {
                continue;
            }

            var match = matches[0];
            if (!emittedFqns.Add(match.FullyQualifiedName))
            {
                continue;
            }

            pairs.Add((match, asset.Contents, asset.SourcePath));
        }

        if (pairs.Count == 0)
        {
            return;
        }

        // RASK020 — detect simple-name collisions across registered components. Two
        // components in different namespaces with the same simple type name compete for
        // the same window.Rask[{SimpleName}] slot; the last registration wins silently.
        // This catches the collision at build time so users get a chance to rename or
        // namespace their exports before runtime surprises hit.
        var bySimpleName = pairs
            .GroupBy(p => p.Component.TypeName, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);
        foreach (var collisionGroup in bySimpleName)
        {
            var fqns = string.Join(", ",
                collisionGroup.Select(p => p.Component.FullyQualifiedName));
            spc.ReportDiagnostic(Diagnostic.Create(
                Rask020,
                SourceLocation(collisionGroup.First().SourcePath),
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

        foreach (var pair in pairs.OrderBy(p => p.Component.FullyQualifiedName, StringComparer.Ordinal))
        {
            sb.Append("        global::Rask.Core.ScopedAssets.ScopedAssetRegistry.RegisterJs(typeof(")
                .Append(pair.Component.FullyQualifiedName)
                .Append("), ");
            AppendVerbatimStringLiteral(sb, pair.Js);
            sb.AppendLine(");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        spc.AddSource("__RaskScopedJsRegistration.g.cs",
            SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    ///     RASK055 — a <c>.js</c> sitting where a scoped asset would go.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Raised here rather than from MSBuild, and the reason is false positives. MSBuild can
    ///         only see the filesystem, so its rule would be "a <c>.js</c> beside a <c>.cs</c>" —
    ///         which breaks a consumer whose <c>Foo.cs</c> is an ordinary static class and whose
    ///         <c>Foo.js</c> is a vendored script, with no opt-out to reach for. That is the same
    ///         over-reach that made the old <c>.js</c> glob accumulate its wwwroot/Resources/Browser
    ///         exclusions.
    ///     </para>
    ///     <para>
    ///         Here the test is semantic: a <c>.js</c> beside a non-abstract, non-generic
    ///         <c>Component</c> subclass of that name. That is exactly the set of files that worked
    ///         as scoped JavaScript yesterday, so nothing else can be caught by it.
    ///     </para>
    /// </remarks>
    private static void ReportStrayJavaScript(
        SourceProductionContext spc,
        Dictionary<string, List<ComponentInfo>> byDirAndName,
        string strayJs)
    {
        if (strayJs.Length == 0)
        {
            return;
        }

        foreach (var path in strayJs.Split(';'))
        {
            if (path.Length == 0)
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(stem))
            {
                continue;
            }

            var key = MakeKey(NormalizeDirectory(path), stem);
            if (!byDirAndName.TryGetValue(key, out var matches) || matches.Count == 0)
            {
                continue;
            }

            spc.ReportDiagnostic(Diagnostic.Create(Rask054, SourceLocation(path), path, stem));
        }
    }

    /// <summary>
    ///     A location in a file that is not part of the compilation.
    /// </summary>
    /// <remarks>
    ///     The alternative is <c>Location.None</c>, which is what these diagnostics used while they
    ///     described a file csc had open. Now that the file handed to csc is generated output in
    ///     <c>obj/</c>, "no location" would leave an error about <c>Counter.ts</c> with nothing to
    ///     click and nothing to blame.
    /// </remarks>
    private static Location SourceLocation(string path) =>
        Location.Create(path, new TextSpan(0, 0), new LinePositionSpan(default, default));

    /// <summary>
    ///     The directory of <paramref name="path" />, in a form the two sides of the pairing agree on.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A component's path comes from Roslyn (<c>SyntaxTree.FilePath</c>); a scoped asset's comes
    ///         from MSBuild, as the <c>RaskTsSource</c> metadata on the compiled file. On macOS the two
    ///         disagree: <c>/var</c>, <c>/tmp</c> and <c>/etc</c> are symlinks into <c>/private</c>,
    ///         Roslyn reports the resolved path and MSBuild's <c>%(FullPath)</c> keeps the short one. So
    ///         a project anywhere under those — every project in a temp directory, which is every
    ///         scaffolded project a test builds — pairs a <c>.ts</c> against nothing and reports RASK017
    ///         for a component sitting right beside it.
    ///     </para>
    ///     <para>
    ///         Scoped CSS never had this: both of its sides come from Roslyn, so they agree by
    ///         construction. TypeScript is the first pairing where one side is an MSBuild string, and
    ///         this is the seam that introduced.
    ///     </para>
    ///     <para>
    ///         Resolving the link properly is not open to a generator — it must not touch the file
    ///         system. Collapsing the one alias macOS actually uses is what is available, and it is
    ///         enough: the prefix is fixed, documented, and applies to every path under it.
    ///     </para>
    /// </remarks>
    private static string NormalizeDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        dir = dir.Replace('\\', '/');

        return dir.StartsWith("/private/", StringComparison.Ordinal) ? dir.Substring("/private".Length) : dir;
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

    /// <summary>One compiled scoped asset: where it came from, and what it compiled to.</summary>
    /// <remarks>
    ///     <c>SourcePath</c> is the author's <c>.ts</c> and drives every decision — pairing, and the
    ///     location of every diagnostic. <c>Contents</c> is the compiled JavaScript and is only ever
    ///     written into the registration.
    /// </remarks>
    private readonly record struct ScopedAsset(string SourcePath, string Contents);
}
