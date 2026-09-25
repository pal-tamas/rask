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
            if (string.Equals(name, ComponentFullName, StringComparison.Ordinal))
            {
                isComponent = true;
                break;
            }

            isExternal |= string.Equals(name, ExternalComponentFullName, StringComparison.Ordinal);
            ownNames.UnionWith(t.MemberNames);
        }

        if (!isComponent)
        {
            return null;
        }

        var (containers, partial) = ContainerHeaders(symbol);
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

    // The types a component is nested in, outermost first, and whether it and all of them are partial.
    private static (List<string> Headers, bool Partial) ContainerHeaders(INamedTypeSymbol symbol)
    {
        var partial = IsDeclaredPartial(symbol);
        var containers = new List<string>();
        for (var outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            partial &= IsDeclaredPartial(outer) && !outer.IsGenericType;
            var keyword = (outer.IsRecord, outer.TypeKind == TypeKind.Struct) switch
            {
                (true, true) => "record struct",
                (true, false) => "record",
                (false, true) => "struct",
                _ => "class",
            };
            containers.Insert(0, keyword + " " + outer.Name);
        }

        return (containers, partial);
    }

    private static bool IsDeclaredPartial(INamedTypeSymbol symbol) =>
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

            if (!list.Exists(x => string.Equals(x.FullyQualifiedName, c.FullyQualifiedName, StringComparison.Ordinal)))
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

        var script = new ScriptEmitter(
            component.TypeName, component.Namespace, component.Containers, component.MemberNames, fileName, reserved, Report);
        script.ClaimRecordNames(decls);
        var mapper = new TypeMapper(decls);

        // Classes first: a function's signature may name one, and a class that could not be generated
        // must fail the functions that use it rather than leave them pointing at nothing.
        foreach (var cls in decls.Classes)
        {
            script.EmitClass(cls, mapper);
        }

        var overloaded = new HashSet<string>(
            decls.Functions.GroupBy(f => f.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key),
            StringComparer.Ordinal);
        foreach (var fn in decls.Functions)
        {
            if (!overloaded.Contains(fn.Name))
            {
                script.EmitFunction(fn, mapper);
            }
            else if (fn == decls.Functions.First(f => string.Equals(f.Name, fn.Name, StringComparison.Ordinal)))
            {
                Report(fn.Name, "it is overloaded — keep one signature (optional parameters cover most overloads)");
            }
        }

        return script.Assemble(mapper);
    }

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
