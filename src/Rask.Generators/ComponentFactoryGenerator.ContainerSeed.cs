using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rask.Generators.Shared;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    // A CONTAINER JOINED BY A SEEDED COMPONENT: one entry name, two ways in.
    //
    // `Ui.Field` is a container — no type to pin, nothing required, reached by a property that hands back the
    // component itself — and `Ui.Field.Bind(() => m.Name)` opens a generic component whose `[RaskChainEntry]`
    // names the container's entry. A property cannot hand back both, so when a seeded component joins a seedless
    // namesake the entry hands back the SEED, and the seed carries the container's chain as well: its own steps,
    // its children indexers and a conversion to it, each building the container through the same entry the
    // property would have used. Nothing is built until a step says which of the two the chain is, so the
    // container a Bind step passes over is never constructed — and once a container step is taken the chain is
    // the container, which has no Bind, so the two cannot be mixed.
    //
    // Only a plain Component container qualifies: an Element's chain is the whole HTML surface, which the seed
    // would have to repeat member by member. A joined Element, a second container, or a container step whose
    // name an opening already takes is two components claiming one name — RASK040, as before.

    private static readonly string[] SeedOwnSteps = ["Key", "Of"];

    /// <summary>The seedless namesake a seeded component's entry also opens, or null when there is none.</summary>
    private static Candidate? JoinedContainer(IEnumerable<Candidate> entries, string entryName, string ns) =>
        entries.FirstOrDefault(c =>
            !NeedsSeed(c)
            && string.Equals(c.EntryName, entryName, StringComparison.Ordinal)
            && string.Equals(c.Namespace, ns, StringComparison.Ordinal)
            && string.Equals(c.TypeName, c.EntryName, StringComparison.Ordinal));

    /// <summary>Whether <paramref name="c" />'s entry hands back a seed: its own, or the one a joined component brings.</summary>
    private static bool HandsBackSeed(Candidate c, IEnumerable<Candidate> entries) =>
        NeedsSeed(c)
        || entries.Any(o =>
            NeedsSeed(o)
            && string.Equals(o.EntryName, c.EntryName, StringComparison.Ordinal)
            && string.Equals(o.Namespace, c.Namespace, StringComparison.Ordinal));

    /// <summary>
    ///     Whether the components on one entry name can share it: all seeded, or seeded ones joined to exactly one
    ///     seedless namesake container whose steps none of their openings takes.
    /// </summary>
    private static bool CanShareEntry(List<Candidate> members)
    {
        var containers = members.Where(static c => !NeedsSeed(c)).ToList();
        if (containers.Count == 0 || members.Count == 1)
        {
            return true;
        }

        if (containers.Count != 1 || containers[0].IsElement
            || !string.Equals(containers[0].TypeName, containers[0].EntryName, StringComparison.Ordinal))
        {
            return false;
        }

        var openings = new HashSet<string>(SeedOwnSteps, StringComparer.Ordinal);
        foreach (var seeded in members.Where(NeedsSeed))
        {
            openings.UnionWith(Openings(seeded).Where(static o => o.Count != 0).Select(static o => o[0].ParamName));
            openings.UnionWith(RequiredSteps(seeded).Select(static r => r.ParamName));
        }

        return !ContainerStepNames(containers[0]).Any(openings.Contains);
    }

    private static IEnumerable<string> ContainerStepNames(Candidate container) =>
        ForwardedProps(container).Select(static p => p.Name)
            .Concat(EnumSteps(OwnSetterProps(container)).Select(static s => s.Member));

    private static IEnumerable<PropInfo> ForwardedProps(Candidate container) =>
        OwnSetterProps(container).Where(p => !IsExclusiveOpening(container, p.Name));

    /// <summary>The container's chain on the seed: each step builds it first, through its own entry.</summary>
    private static void EmitContainerSteps(
        StringBuilder sb, Candidate container, string seedName, string pad, string assemblyName, bool seedCarriesKey)
    {
        var self = container.FullyQualifiedName;
        var setters = "global::RaskBuilderSetters" + assemblyName + ".";

        sb.Append(pad).AppendLine();
        sb.Append(pad).Append("// ").Append(container.TypeName)
            .AppendLine(" itself, built when one of its own steps is taken.");
        sb.Append(pad).Append("private ").Append(self).AppendLine(" Container()");
        sb.Append(pad).AppendLine("{");
        sb.Append(pad).Append("    var __c = ").Append(EntryBody(container, null, assemblyName)).AppendLine(";");
        if (seedCarriesKey)
        {
            EmitCarriedKeyClaim(sb, pad, "_key");
        }

        sb.Append(pad).AppendLine("    return __c;");
        sb.Append(pad).AppendLine("}");

        sb.Append(pad).AppendLine();
        sb.Append(pad).Append("/// <summary>The ").Append(container.TypeName)
            .AppendLine(" this entry opens when no other step is taken.</summary>");
        sb.Append(pad).Append("public static implicit operator ").Append(self).Append('(').Append(seedName)
            .AppendLine(" seed) => seed.Container();");

        foreach (var children in new[] { "params global::Rask.Core.Component?[]", "global::System.Collections.Generic.IEnumerable<global::Rask.Core.Component?>", "params object?[]" })
        {
            sb.Append(pad).AppendLine();
            sb.Append(pad).Append("/// <summary>The ").Append(container.TypeName).AppendLine(" with these children.</summary>");
            sb.Append(pad).Append("public global::Rask.Core.Component this[").Append(children)
                .AppendLine(" children] => Container()[children];");
        }

        foreach (var p in ForwardedProps(container))
        {
            EmitForwardedSetter(sb, p, self, setters, pad);
        }

        foreach (var (member, _, _) in EnumSteps(OwnSetterProps(container)))
        {
            sb.Append(pad).AppendLine();
            sb.Append(pad).Append("/// <summary>The ").Append(container.TypeName).Append(", <c>").Append(member)
                .AppendLine("</c>.</summary>");
            sb.Append(pad).Append("public ").Append(self).Append(' ').Append(CSharpNames.Escape(member))
                .Append(" => Container().").Append(CSharpNames.Escape(member)).AppendLine(";");
        }
    }

    // The step as the container's own setter takes it, plus the two shorthands that setter has: `.Flag()` for a
    // bool and `.Class("a", b ? "c" : null)` for the classes.
    private static void EmitForwardedSetter(StringBuilder sb, PropInfo p, string self, string setters, string pad)
    {
        var name = CSharpNames.Escape(p.Name);
        var (paramType, _) = SetterParameter(p.TypeFqn, wrap: false);
        sb.Append(pad).AppendLine();
        EmitDocComment(sb, p.Summary, pad);
        sb.Append(pad).Append("public ").Append(self).Append(' ').Append(name).Append('(').Append(paramType)
            .Append(" value) => ").Append(setters).Append(name).AppendLine("(Container(), value);");

        if (p.TypeFqn is "bool" or "bool?")
        {
            sb.Append(pad).Append("/// <summary>Sets <c>").Append(p.Name).AppendLine("</c> to <c>true</c>.</summary>");
            sb.Append(pad).Append("public ").Append(self).Append(' ').Append(name).Append("() => ").Append(setters)
                .Append(name).AppendLine("(Container(), true);");
        }

        if (string.Equals(p.TypeFqn, "string?", StringComparison.Ordinal) && string.Equals(p.Name, "Class", StringComparison.Ordinal))
        {
            sb.Append(pad).AppendLine("/// <summary>Sets <c>Class</c> to these names joined by one space; null and blank ones are left out.</summary>");
            sb.Append(pad).Append("public ").Append(self)
                .Append(" Class(params global::System.ReadOnlySpan<string?> parts) => ").Append(setters)
                .AppendLine("Class(Container(), global::Rask.Core.BuilderRuntime.JoinClasses(parts));");
        }
    }
}
