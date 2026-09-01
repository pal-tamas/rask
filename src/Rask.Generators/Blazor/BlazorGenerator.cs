using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators.Blazor;

/// <summary>
///     Completes a component deriving from <c>BlazorComponent&lt;T&gt;</c>, taking its properties
///     from the hosted Blazor component's own <c>[Parameter]</c>s.
/// </summary>
/// <remarks>
///     <para>
///         Nothing is redeclared. A Blazor component already states its surface — <c>[Parameter]</c>
///         properties and <c>EventCallback</c>s — so an island body is empty and the steps are read
///         straight off the hosted type. Restating them in C# would be duplicated work that drifts
///         silently the first time the library is upgraded.
///     </para>
///     <para>
///         This generator writes the PROPERTIES and the parameter writer. Their chain setters come
///         from <c>ComponentFactoryGenerator</c>, which reads the same list through
///         <see cref="BlazorParameters" /> — shared precisely because one source generator never sees
///         another's output, so the two computations must not be allowed to diverge.
///     </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class BlazorGenerator : IIncrementalGenerator
{
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
        if (compilation.GetTypeByMetadataName("Rask.Blazor.BlazorComponent`1") is null)
        {
            // The app does not reference Rask.Blazor. Nothing to do, and nothing to say about it.
            return;
        }

        var islands = new List<(INamedTypeSymbol Island, INamedTypeSymbol Hosted)>();
        foreach (var type in Types(compilation.Assembly.GlobalNamespace))
        {
            if (type.IsAbstract || type.TypeKind != TypeKind.Class)
            {
                continue;
            }

            if (BlazorParameters.HostedTypeOf(type) is not { } hosted)
            {
                continue;
            }

            if (!IsPartial(type))
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask061, LocationOf(type), type.Name));
                continue;
            }

            islands.Add((type, hosted));
        }

        // The name is what identifies an island in the rendered markup, so it has to be unique.
        var byName = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var (island, _) in islands)
        {
            if (byName.TryGetValue(island.Name, out var first))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask064, LocationOf(island), first.ToDisplayString(), island.ToDisplayString(), island.Name));
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
                spc.ReportDiagnostic(Diagnostic.Create(Rask066, LocationOf(island), island.Name, hosted.Name));
            }

            // A hand-written writer wins outright. Emitting beside it would be a duplicate member,
            // and an author who wrote one is deliberately mapping the parameters themselves.
            if (island.GetMembers("WriteParameters").Any(m => !m.IsImplicitlyDeclared))
            {
                continue;
            }

            var parameters = BlazorParameters.Read(island, hosted);
            spc.AddSource(
                $"{island.ToDisplayString()}.Blazor.g.cs",
                SourceText.From(Render(island, parameters), Encoding.UTF8));
        }
    }

    private static string Render(INamedTypeSymbol island, List<BlazorParam> parameters)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        if (!island.ContainingNamespace.IsGlobalNamespace)
        {
            sb.Append("namespace ").Append(island.ContainingNamespace.ToDisplayString()).AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("partial class ").AppendLine(island.Name);
        sb.AppendLine("{");

        foreach (var p in parameters)
        {
            sb.Append("    /// <summary>Feeds the hosted component's <c>").Append(p.Parameter)
                .AppendLine("</c> parameter.</summary>");

            // `required` for an [EditorRequired] parameter: it is what makes this a chain step the
            // call site cannot omit, and it is also what keeps the non-nullable property from
            // tripping CS8618 without inventing an initializer — which would EXCLUDE it from the
            // chain entirely rather than making it mandatory.
            sb.Append("    public ").Append(p.IsRequired ? "required " : string.Empty)
                .Append(p.ChainTypeFqn).Append(' ').Append(p.Name)
                .AppendLine(" { get; set; }");
            sb.AppendLine();
        }

        sb.AppendLine("    /// <inheritdoc />");
        sb.AppendLine(
            "    protected override void WriteParameters("
            + "global::System.Collections.Generic.Dictionary<string, object?> into)");
        sb.AppendLine("    {");
        foreach (var p in parameters)
        {
            if (p.IsRequired)
            {
                // Required: the chain cannot build the component without it, so there is no "unset"
                // state to test for, and a null check on a non-nullable member is a warning.
                sb.Append("        into[\"").Append(p.Parameter).Append("\"] = ")
                    .Append(Value(p)).AppendLine(";");
                continue;
            }

            // Omit rather than write null: ParameterView is authoritative, so a null would CLOBBER
            // the hosted component's own default rather than mean "not specified".
            sb.Append("        if (this.").Append(p.Name).AppendLine(" is not null)");
            sb.AppendLine("        {");
            sb.Append("            into[\"").Append(p.Parameter).Append("\"] = ")
                .Append(Value(p)).AppendLine(";");
            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string Value(BlazorParam p)
    {
        if (!p.IsEventCallback)
        {
            return "this." + p.Name;
        }

        // A plain Rask delegate becomes the EventCallback the hosted component declared. The factory
        // overload is public and takes the receiver, so no reflection is involved.
        return p.EventArg is null
            ? $"global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create(this, this.{p.Name})"
            : $"global::Microsoft.AspNetCore.Components.EventCallback.Factory.Create<{p.EventArg}>(this, this.{p.Name})";
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is TypeDeclarationSyntax d
            && d.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

    private static Location LocationOf(INamedTypeSymbol type) =>
        type.Locations.FirstOrDefault() ?? Location.None;

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
}
