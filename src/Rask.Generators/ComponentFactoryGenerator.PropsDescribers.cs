using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    // The one fact about a component that an assembly boundary destroys: which of its properties a
    // builder chain has to set.
    //
    // A member initializer compiles into the constructor. It leaves NO symbol-level trace, and a metadata
    // symbol has no DeclaringSyntaxReferences to fall back on, so from a referencing compilation
    // `string Title` and `string Title = ""` are indistinguishable — RASK038 has no way to police a
    // referenced library's RASK001 props, permanently (CrossAssemblyRequiredPropertyTests). The language's
    // `required` modifier is the only kind metadata preserves, and it is not this kind.
    //
    // So the answer is published from here, where it is already known: the same rule RASK001 applies, over
    // the same property set, in the compilation that reported it.
    // Re-deriving it on the consumer's side is not "harder" — it is impossible; re-deriving it from a
    // richer source would be a second copy free to drift. `required` props are skipped: metadata keeps
    // those, and the consumer reads them straight off the symbol.
    // What a component tells the devtools about its own properties: one override per component, and only in a build
    // that carries the tools. A Release build emits nothing here, which is why a shipped app neither describes its own
    // state nor pays for the call — the base method is empty and the JIT drops it.
    //
    // Values are read BY NAME in generated code, never reflected over: reflection would be a trimming hazard in the
    // one place a trimmed publish must survive intact, and it would run getters that do work. A sensitive property is
    // decided at build time (IsSensitiveProp) and its value is not read at all, so it cannot reach a panel or a frame.
    private static void EmitPropsDescribers(
        SourceProductionContext spc, ImmutableArray<Candidate> candidates, bool devTools,
        bool seesComponentInternals)
    {
        if (!devTools || candidates.IsDefaultOrEmpty)
        {
            return;
        }

        // The language's rule, not a choice: a `protected internal` member overridden where the internal half is NOT
        // visible must be declared `protected` alone, and where it IS visible — Core itself and every friend Core
        // names — it must keep both words. Writing one of them everywhere is CS0507 in half the compilations.
        var modifier = seesComponentInternals ? "protected internal override" : "protected override";

        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);
        var wrote = false;

        foreach (var c in DistinctByType(candidates))
        {
            // A generator cannot add a member to a type that is not partial, and a nested one would need its enclosing
            // types re-opened — more than a description is worth. Both stay silent: the component still works, it just
            // shows no props.
            if (!c.IsPartial || HasEnclosingType(c))
            {
                continue;
            }

            // An element is never a node of the DevTools component tree (the serializer's devtools hook skips every
            // Element), so describing its props would be code nobody reads — and, on the one element type that is not
            // sealed (HTMLElement), a public member that exists in Debug and not in Release.
            if (c.IsElement)
            {
                continue;
            }

            // Its own, once each: the shared Element/Component surface is described by Core's own overrides, and a
            // bound control's interface props are the same property seen twice.
            var props = c.Properties
                .Where(static p => !p.IsSharedSurfaceProp && !p.IsBoundInterfaceProp)
                .GroupBy(static p => p.Name, StringComparer.Ordinal)
                .Select(static g => g.First())
                .OrderBy(static p => p.Name, StringComparer.Ordinal)
                .ToList();
            if (props.Count == 0)
            {
                continue;
            }

            wrote = true;
            EmitPropsDescriber(sb, c, props, modifier);
        }

        if (wrote)
        {
            spc.AddSource("RaskPropsDescribers.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    private static void EmitPropsDescriber(StringBuilder sb, Candidate c, List<PropInfo> props, string modifier)
    {
        sb.AppendLine();
        var hasNs = !string.IsNullOrEmpty(c.Namespace);
        if (hasNs)
        {
            // Block-scoped: one file carries every component, and a file may hold only one file-scoped namespace.
            sb.Append("namespace ").AppendLine(c.Namespace);
            sb.AppendLine("{");
        }

        // The BARE type parameters: a type parameter's [DynamicallyAccessedMembers] belongs on the declaration that
        // introduces it, and repeating it on a second partial declaration of the same type is CS0579.
        sb.Append("partial class ").Append(c.TypeName).Append(c.TypeParameters)
            .AppendLine(c.TypeParameterConstraints);
        sb.AppendLine("{");
        sb.AppendLine("    /// <inheritdoc />");
        sb.AppendLine(
            "    [global::System.ComponentModel.EditorBrowsable("
            + "global::System.ComponentModel.EditorBrowsableState.Never)]");
        sb.Append("    ").Append(modifier).AppendLine(
            " void DescribeProps(global::Rask.Core.Diagnostics.DevTools.PropsDescriber describer)");
        sb.AppendLine("    {");
        sb.AppendLine("        base.DescribeProps(describer);");
        foreach (var p in props)
        {
            var type = Unqualified(p.TypeFqn);
            if (p.IsSensitive)
            {
                sb.Append("        describer.AddRedacted(\"").Append(p.Name).Append("\", \"").Append(type)
                    .AppendLine("\");");
                continue;
            }

            sb.Append("        describer.Add(\"").Append(p.Name).Append("\", \"").Append(type)
                .Append("\", global::Rask.Core.Diagnostics.DevTools.PropsDescriber.Format(this.")
                .Append(p.Escaped).AppendLine("));");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        if (hasNs)
        {
            sb.AppendLine("}");
        }
    }

    // A component declared inside another type: its fully qualified name still carries a '.' once the namespace and
    // any type arguments are taken off.
    private static bool HasEnclosingType(Candidate c)
    {
        var name = c.FullyQualifiedName;
        if (name.StartsWith("global::", StringComparison.Ordinal))
        {
            name = name.Substring("global::".Length);
        }

        var generic = name.IndexOf('<');
        if (generic >= 0)
        {
            name = name.Substring(0, generic);
        }

        if (!string.IsNullOrEmpty(c.Namespace) && name.StartsWith(c.Namespace + ".", StringComparison.Ordinal))
        {
            name = name.Substring(c.Namespace.Length + 1);
        }

        return name.IndexOf('.') >= 0;
    }

    // The type as a developer reads it in their own code, rather than as the compiler spells it.
    private static string Unqualified(string typeFqn) =>
        typeFqn.StartsWith("global::", StringComparison.Ordinal)
            ? typeFqn.Substring("global::".Length)
            : typeFqn;

    private static void EmitPublishedRequiredProperties(
        SourceProductionContext spc, ImmutableArray<Candidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var lines = new List<string>();
        foreach (var c in DistinctByType(candidates))
        {
            var required = c.Properties
                .Where(static p => !p.IsInitOnly && !p.UserMarkedRequired && IsRequiredFactoryParam(p))
                .Select(static p => p.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static n => n, StringComparer.Ordinal)
                .ToList();
            if (required.Count == 0)
            {
                continue;
            }

            var sb = new StringBuilder("[assembly: global::Rask.Core.RaskRequiredProperties(\"");
            sb.Append(Analyzers.BuilderEntry.TypeKey(c.FullyQualifiedName)).Append('"');
            foreach (var name in required)
            {
                sb.Append(", \"").Append(name).Append('"');
            }

            lines.Add(sb.Append(")]").ToString());
        }

        if (lines.Count == 0)
        {
            return;
        }

        var file = new StringBuilder();
        EmitGeneratedFileHeader(file);
        file.AppendLine();
        lines.Sort(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            file.AppendLine(line);
        }

        spc.AddSource("RaskRequiredProperties.g.cs", SourceText.From(file.ToString(), Encoding.UTF8));
    }
}
