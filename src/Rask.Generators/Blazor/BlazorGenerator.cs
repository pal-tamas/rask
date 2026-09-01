using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Blazor;

/// <summary>
///     Completes a component deriving from <c>BlazorComponent&lt;T&gt;</c>, taking its chain steps
///     from the hosted Blazor component's own <c>[Parameter]</c>s.
/// </summary>
/// <remarks>
///     <para>
///         Nothing is redeclared. A Blazor component already states its surface — <c>[Parameter]</c>
///         properties and <c>EventCallback</c>s — so an island body is empty and the chain steps are
///         read straight off the hosted type. Restating them in C# would be duplicated work that can
///         silently drift when the library is upgraded.
///     </para>
///     <para>
///         This generator writes only <c>WriteParameters</c> — the reflection-free mapping from the
///         island's chain props onto the hosted component's parameter names. The PROPERTIES and their
///         chain setters come from <c>ComponentFactoryGenerator</c>, which reads the hosted type's
///         parameters as part of building the island's own prop list.
///     </para>
///     <para>
///         They cannot be split the other way round. One source generator never sees another's
///         output, so a property written here would be invisible to the factory generator and would
///         get no chain step at all — the island would compile with a property nobody could set from
///         markup. Whatever emits the properties has to be whatever emits their setters.
///     </para>
///     <para>
///         Only a hosted type from a REFERENCED assembly can be read this way. A <c>.razor</c> in the
///         same project is produced by the Razor source generator, so its parameters are invisible
///         here for exactly the same reason — that is RASK066, and the island still works, just
///         unverified.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class BlazorGenerator : IIncrementalGenerator
{
    private const string IslandBaseName = "Rask.Blazor.BlazorComponent`1";
    private const string ParameterAttrName = "Microsoft.AspNetCore.Components.ParameterAttribute";
    private const string EventCallbackName = "Microsoft.AspNetCore.Components.EventCallback";
    private const string RenderFragmentName = "Microsoft.AspNetCore.Components.RenderFragment";
    private const string BlazorParameterAttrName = "Rask.Blazor.BlazorParameterAttribute";

    private static readonly DiagnosticDescriptor Rask061 = new(
        "RASK061",
        "Blazor island must be partial",
        "'{0}' hosts a Blazor component, so its parameter writer and chain steps are generated into "
        + "the same class — add the 'partial' modifier",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        "An island hosting a Blazor component is completed by a generated second part of the class: "
        + "the chain steps taken from the hosted component's [Parameter] properties, and the "
        + "reflection-free writer that maps them onto it. Without 'partial' there is nowhere to put "
        + "any of it.",
        DiagnosticHelp.Link("RASK061"));

    private static readonly DiagnosticDescriptor Rask064 = new(
        "RASK064",
        "Two Blazor islands share a simple name",
        "'{0}' and '{1}' are both named '{2}' — an island's name identifies it in the rendered markup, "
        + "so rename one",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Error,
        true,
        "An island's simple name is written into the rendered markup as the host element's 'name' "
        + "attribute, so it identifies the island in the page and to anything reading it. Two "
        + "islands sharing a name are ambiguous there, and the namespace does not disambiguate.",
        DiagnosticHelp.Link("RASK064"));

    private static readonly DiagnosticDescriptor Rask066 = new(
        "RASK066",
        "Hosted Blazor component's parameters cannot be verified",
        "'{0}' hosts '{1}', which is generated in this same project, so its parameters cannot be read "
        + "at compile time and no chain steps were generated for them. Move the .razor to a Razor "
        + "Class Library and reference it to get them checked.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        "Chain steps are read from the hosted component's [Parameter] properties, which works for a "
        + "type from a referenced project or package. A .razor in the same project is produced by "
        + "the Razor source generator, and one source generator never sees another's output, so its "
        + "parameters cannot be resolved here. The island still renders; what is lost is "
        + "compile-time checking of what you pass it.",
        DiagnosticHelp.Link("RASK066"));

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Straight off the compilation, matching ExternalGenerator and CqrsCodecGenerator: symbols
        // must not be held across an incremental boundary.
        context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) => Emit(spc, compilation));
    }

    private static void Emit(SourceProductionContext spc, Compilation compilation)
    {
        var baseType = compilation.GetTypeByMetadataName(IslandBaseName);
        if (baseType is null)
        {
            // The app does not reference Rask.Blazor. Nothing to do, and nothing to say about it.
            return;
        }

        var parameterAttr = compilation.GetTypeByMetadataName(ParameterAttrName);
        var renameAttr = compilation.GetTypeByMetadataName(BlazorParameterAttrName);

        var islands = new List<(INamedTypeSymbol Island, INamedTypeSymbol Hosted)>();
        foreach (var type in Types(compilation.Assembly.GlobalNamespace))
        {
            if (type.IsAbstract || type.TypeKind != TypeKind.Class)
            {
                continue;
            }

            if (HostedTypeOf(type, baseType) is not { } hosted)
            {
                continue;
            }

            if (!IsPartial(type))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask061, Location(type), type.Name));
                continue;
            }

            islands.Add((type, hosted));
        }

        // The name is what identifies an island in the rendered markup, so it has to be unique.
        var byName = new Dictionary<string, INamedTypeSymbol>();
        foreach (var (island, _) in islands)
        {
            if (byName.TryGetValue(island.Name, out var first))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask064, Location(island), first.ToDisplayString(), island.ToDisplayString(), island.Name));
                continue;
            }

            byName[island.Name] = island;
        }

        foreach (var (island, hosted) in islands)
        {
            // An error type means the hosted component is generated in this same compilation — the
            // Razor source generator produced it, and one generator cannot see another's output.
            if (hosted.TypeKind == TypeKind.Error)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask066, Location(island), island.Name, hosted.Name));
            }

            // A hand-written writer wins outright. Emitting beside it would be a duplicate member,
            // and an author who wrote one is deliberately mapping the parameters themselves.
            if (island.GetMembers("WriteParameters").Any(m => !m.IsImplicitlyDeclared))
            {
                continue;
            }

            var parameters = ReadParameters(hosted, parameterAttr, renameAttr, island);
            spc.AddSource(
                $"{island.ToDisplayString()}.Blazor.g.cs",
                SourceText.From(Render(island, parameters), Encoding.UTF8));
        }
    }

    private static List<Param> ReadParameters(
        INamedTypeSymbol hosted,
        INamedTypeSymbol? parameterAttr,
        INamedTypeSymbol? renameAttr,
        INamedTypeSymbol island)
    {
        var result = new List<Param>();
        if (parameterAttr is null)
        {
            return result;
        }

        // Names the island (or a base) already declares are left alone: a hand-written property is an
        // explicit override and must win over anything generated for it.
        var declared = new HashSet<string>(
            Members(island).Select(m => m.Name),
            System.StringComparer.Ordinal);

        // Renames the author asked for, keyed by the HOSTED parameter they point at.
        var renames = new Dictionary<string, string>(System.StringComparer.Ordinal);
        if (renameAttr is not null)
        {
            foreach (var member in island.GetMembers().OfType<IPropertySymbol>())
            {
                foreach (var attr in member.GetAttributes())
                {
                    if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, renameAttr)
                        && attr.ConstructorArguments.Length == 1
                        && attr.ConstructorArguments[0].Value is string target)
                    {
                        renames[target] = member.Name;
                    }
                }
            }
        }

        for (var t = hosted; t is not null; t = t.BaseType)
        {
            foreach (var prop in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.IsStatic || prop.SetMethod is null || prop.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (!prop.GetAttributes().Any(a =>
                        SymbolEqualityComparer.Default.Equals(a.AttributeClass, parameterAttr)))
                {
                    continue;
                }

                // ChildContent is the indexer, not a step — Rask children cross as markup.
                if (prop.Name == "ChildContent" || result.Any(p => p.Parameter == prop.Name))
                {
                    continue;
                }

                var fullType = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // A templated parameter has no Rask equivalent yet; skip rather than emit something
                // that compiles and cannot work.
                if (fullType.StartsWith("global::" + RenderFragmentName, System.StringComparison.Ordinal))
                {
                    continue;
                }

                var name = renames.TryGetValue(prop.Name, out var renamed) ? renamed : prop.Name;
                if (declared.Contains(name))
                {
                    continue;
                }

                result.Add(new Param(
                    prop.Name,
                    name,
                    fullType,
                    IsEventCallback(prop.Type),
                    EventArg(prop.Type)));
            }
        }

        return result;
    }

    private static bool IsEventCallback(ITypeSymbol type) =>
        type.OriginalDefinition.ToDisplayString() is EventCallbackName or EventCallbackName + "<T>";

    private static string? EventArg(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeArguments.Length: 1 } named
        && named.OriginalDefinition.ToDisplayString().StartsWith(EventCallbackName, System.StringComparison.Ordinal)
            ? named.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;

    private static string Render(INamedTypeSymbol island, List<Param> parameters)
    {
        var ns = island.ContainingNamespace.IsGlobalNamespace
            ? null
            : island.ContainingNamespace.ToDisplayString();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        if (ns is not null)
        {
            sb.Append("namespace ").Append(ns).AppendLine(";").AppendLine();
        }

        sb.Append("partial class ").Append(island.Name).AppendLine();
        sb.AppendLine("{");

        foreach (var p in parameters)
        {
            sb.Append("    /// <summary>Feeds the hosted component's <c>")
                .Append(p.Parameter).AppendLine("</c> parameter.</summary>");
            sb.Append("    public ").Append(p.ClrType).Append(' ').Append(p.Name).AppendLine(" { get; set; }");
        }

        sb.AppendLine();
        sb.AppendLine("    /// <inheritdoc />");
        sb.AppendLine(
            "    protected override void WriteParameters("
            + "global::System.Collections.Generic.Dictionary<string, object?> into)");
        sb.AppendLine("    {");
        foreach (var p in parameters)
        {
            // Omit rather than write null: ParameterView is authoritative, so a null would CLOBBER
            // the hosted component's own default rather than mean "not specified".
            sb.Append("        if (this.").Append(p.Name).AppendLine(" is not null)");
            sb.AppendLine("        {");
            if (p.IsEventCallback)
            {
                var create = p.EventArg is null
                    ? $"global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, this.{p.Name})"
                    : $"global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<{p.EventArg}>(this, this.{p.Name})";
                sb.Append("            into[\"").Append(p.Parameter).Append("\"] = ").Append(create).AppendLine(";");
            }
            else
            {
                sb.Append("            into[\"").Append(p.Parameter).Append("\"] = this.")
                    .Append(p.Name).AppendLine(";");
            }

            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static INamedTypeSymbol? HostedTypeOf(INamedTypeSymbol type, INamedTypeSymbol unboundBase)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, unboundBase)
                && t.TypeArguments.Length == 1
                && t.TypeArguments[0] is INamedTypeSymbol hosted)
            {
                return hosted;
            }
        }

        return null;
    }

    private static IEnumerable<ISymbol> Members(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers())
            {
                yield return m;
            }
        }
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax d
            && d.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

    private static Location Location(INamedTypeSymbol type) =>
        type.Locations.FirstOrDefault() ?? Microsoft.CodeAnalysis.Location.None;

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol ns)
    {
        foreach (var t in ns.GetTypeMembers())
        {
            yield return t;
            foreach (var nested in t.GetTypeMembers())
            {
                yield return nested;
            }
        }

        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var t in Types(child))
            {
                yield return t;
            }
        }
    }

    private readonly record struct Param(
        string Parameter,
        string Name,
        string ClrType,
        bool IsEventCallback,
        string? EventArg);
}
