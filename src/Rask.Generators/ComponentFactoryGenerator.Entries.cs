using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using static Rask.Generators.ComponentSymbols;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    // The names already declared on Rask.Core.Component. In the compilation that DECLARES it, an entry
    // whose name matches one would be CS0102 ("already contains a definition") — `Head` is the real case:
    // Component.HeadAssets is the head-asset contribution. In a CONSUMER the same names, now including every
    // tag entry Rask.Core emitted, are what a referenced library's entry would hide (CS0108).
    private static ComponentHost GetComponentHost(Compilation compilation)
    {
        var assembly = SanitizeIdentifier(compilation.AssemblyName ?? "Rask");
        // Resolved through the whole compilation, not just its own assembly, so a CONSUMER gets the
        // names too — including the tag entries Rask.Core's own emission added, which are members of the
        // Component it references. A referenced library's entry named after one of them would hide it
        // (CS0108, an error under warnings-as-errors), so that is what the external filter tests against.
        var component = compilation.GetTypeByMetadataName(ComponentFullName);
        if (component is null)
        {
            return new ComponentHost(false, assembly, new EquatableArray<string>(Array.Empty<string>()));
        }

        var declaresComponent =
            SymbolEqualityComparer.Default.Equals(component.ContainingAssembly, compilation.Assembly);

        // A friend sees the internal half too, so it must spell an override of a `protected internal` member with
        // both words — exactly as Core does. Core names about twenty of them (the hosts, the islands, Rask.Testing
        // and their test projects), and writing `protected` alone in any of them is CS0507.
        var seesComponentInternals =
            declaresComponent || component.ContainingAssembly.GivesAccessTo(compilation.Assembly);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        for (var t = component; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers().Where(m => !string.IsNullOrEmpty(m.Name)))
            {
                names.Add(m.Name);
            }
        }

        return new ComponentHost(declaresComponent, assembly, new EquatableArray<string>(names.ToArray()),
            seesComponentInternals);
    }

    // Every tag a [Tag] element renders, as RaskTags: id -> name and void-ness, which Element reads by the id
    // an entry or constructor stamped. A type with one tag stamps it in the constructor emitted here, so
    // `new Br()` renders <br> without an entry in sight.
    private static void EmitTagTable(SourceProductionContext spc, ImmutableArray<Candidate> candidates, EquatableArray<string> voidTags)
    {
        var tagged = DistinctByType(candidates).Where(static c => c.Tags.Count > 0).ToList();
        if (tagged.Count == 0)
        {
            return;
        }

        var names = tagged.SelectMany(static c => c.Tags).Select(static t => t.Name)
            .Distinct(StringComparer.Ordinal).OrderBy(static n => n, StringComparer.Ordinal).ToList();
        var voids = new HashSet<string>(voidTags, StringComparer.Ordinal);

        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core");
        sb.AppendLine("{");
        sb.AppendLine("    internal static class RaskTags");
        sb.AppendLine("    {");
        for (var i = 0; i < names.Count; i++)
        {
            sb.Append("        internal const ushort ").Append(TagConstant(names[i])).Append(" = ").Append(i + 1).AppendLine(";");
        }

        sb.AppendLine();
        sb.Append("        internal static readonly string?[] Names = [null");
        foreach (var name in names)
        {
            sb.Append(", \"").Append(name).Append('"');
        }

        sb.AppendLine("];");
        sb.Append("        internal static readonly bool[] Void = [false");
        foreach (var name in names)
        {
            sb.Append(voids.Contains(name) ? ", true" : ", false");
        }

        sb.AppendLine("];");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        foreach (var c in tagged.Where(static c => c.Tags.Count == 1 && !c.IsNested && c.Namespace.Length > 0))
        {
            sb.AppendLine();
            sb.Append("namespace ").AppendLine(c.Namespace);
            sb.AppendLine("{");
            sb.Append("    partial class ").Append(c.TypeName).AppendLine(c.TypeParameters);
            sb.AppendLine("    {");
            sb.Append("        public ").Append(c.TypeName).Append("() => TagId = global::Rask.Core.RaskTags.")
                .Append(TagConstant(c.Tags[0].Name)).AppendLine(";");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }

        spc.AddSource("RaskTags.g.cs", sb.ToString());
    }

    private static void EmitBuilderEntries(
        SourceProductionContext spc,
        ImmutableArray<Candidate> candidates,
        ComponentHost host)
    {
        if (!host.DeclaresComponent || candidates.IsDefaultOrEmpty)
        {
            return;
        }

        const string runtime = "global::Rask.Core.BuilderRuntime.";
        var taken = new HashSet<string>(host.MemberNames, StringComparer.Ordinal);
        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core;");
        sb.AppendLine();
        sb.AppendLine("public abstract partial class RaskMarkup");
        sb.AppendLine("{");

        var shared = OpenEntryHost(host.AssemblyName);

        var html = OpenMarkupEntries();

        var entries = EntryCandidates(spc, candidates, taken);
        var seeded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in entries)
        {
            // A grouped entry — Trigger.Fullscreen — is a member of its group only (EmitGroupedEntries).
            if (c.Group is not null)
            {
                continue;
            }

            // The entry hands back a SEED whenever the chain has something to demand first — a type
            // argument to pin, or a required property. One property per component NAME.
            if (NeedsSeed(c))
            {
                if (seeded.Add(c.EntryName))
                {
                    EmitSeedEntry(sb, shared, html, c);
                }

                continue;
            }

            EmitComponentEntry(sb, shared, html, c, host.AssemblyName);
        }

        sb.AppendLine("}");
        spc.AddSource("RaskBuilderEntries.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

        html.AppendLine("}");
        spc.AddSource("RaskMarkup.Entries.g.cs", SourceText.From(html.ToString(), Encoding.UTF8));
        EmitGroupedEntries(spc, entries, host.AssemblyName);

        shared.AppendLine("}");
        EmitSeeds(shared, entries, host.AssemblyName, runtime);
        spc.AddSource("RaskBuilderEntryHost.g.cs", SourceText.From(shared.ToString(), Encoding.UTF8));
    }

    private static StringBuilder OpenEntryHost(string assemblyName)
    {
        // …and the same entries a second time, as the public `RaskEntriesRaskCore` class every other
        // assembly's own components already publish. Inheritance is how a host normally reaches the
        // framework tags, and it is the cheap way — but a `[RaskMarkup]` host whose base slot is taken
        // (or that is `static`) cannot inherit anything, so its entries have to be injected as members,
        // and a member has to forward to something nameable from another assembly. `protected` is not
        // that; this is. Written from the SAME EntryCandidates list, in the same loop, so the two cannot
        // disagree about which components have an entry or about what it resets.
        var shared = new StringBuilder();
        EmitGeneratedFileHeader(shared);
        shared.AppendLine();
        shared.AppendLine("/// <summary>");
        shared.AppendLine("///     Rask.Core's chain entries, one per framework component, in the form a");
        shared.AppendLine("///     REFERENCING assembly can name. Almost every host reaches these by inheriting");
        shared.AppendLine("///     'Rask.Core.RaskMarkup' instead; this is for the hosts that cannot inherit.");
        shared.AppendLine("/// </summary>");
        shared.Append("public static class ").AppendLine(EntryHostName(assemblyName));
        shared.AppendLine("{");
        return shared;
    }

    private static StringBuilder OpenMarkupEntries()
    {
        // …and a third time, as Rask.Markup: the same entries under a name an author writes, so
        // `global using static Rask.Markup;` makes them bare outside a component, and `Markup.Footer` reaches
        // the element where a member of that name hides it. The same loop again, so it cannot drift.
        var html = new StringBuilder();
        EmitGeneratedFileHeader(html);
        html.AppendLine();
        html.AppendLine("namespace Rask;");
        html.AppendLine();
        html.AppendLine("public static partial class Markup");
        html.AppendLine("{");
        return html;
    }

    private static void EmitComponentEntry(
        StringBuilder sb, StringBuilder shared, StringBuilder html, Candidate c, string assemblyName)
    {
        // An internal component cannot surface through a `protected` member of the public
        // Component (CS0053); `private protected` keeps it to derived types in this assembly.
        //
        // The entry opens a chain and hands back the component itself: the steps after it are
        // extension methods on the component, and a carrier-typed property (not a delegate) is what
        // keeps one from swallowing its own setter (see Rask.Core.Callback).
        foreach (var (name, tag) in EntriesOf(c))
        {
            var body = EntryBody(c, tag, assemblyName);
            EmitEntryDoc(sb, c);
            sb.Append(c.IsPublic ? "    protected static " : "    private protected static ")
                .Append(c.FullyQualifiedName).Append(' ')
                .Append(EscapeIdentifier(name)).Append(" => ").Append(body).AppendLine(";");

            foreach (var host2 in new[] { shared, html })
            {
                EmitEntryDoc(host2, c);
                host2.Append(c.IsPublic ? "    public static " : "    internal static ")
                    .Append(c.FullyQualifiedName).Append(' ')
                    .Append(EscapeIdentifier(name)).Append(" => ").Append(body).AppendLine(";");
            }
        }
    }

    private static void EmitSeedEntry(StringBuilder sb, StringBuilder shared, StringBuilder html, Candidate c)
    {
        EmitEntryDoc(sb, c);
        sb.Append(c.IsPublic ? "    protected static " : "    private protected static ")
            .Append(SeedFqn(c)).Append(' ').Append(EscapeIdentifier(c.EntryName))
            .AppendLine(" = default;");

        foreach (var host2 in new[] { shared, html })
        {
            EmitEntryDoc(host2, c);
            host2.Append(c.IsPublic ? "    public static " : "    internal static ")
                .Append(SeedFqn(c)).Append(' ').Append(EscapeIdentifier(c.EntryName))
                .AppendLine(" => default;");
        }
    }

    // Which components get a builder entry, and the single place that decides it. Both emissions
    // (Component's own, and the per-consumer partials) ask this, and the RESET emission is keyed off
    // the same candidate identity — when the two disagreed, a component could be handed the reset
    // generated for a DIFFERENT type of the same name.
    //
    // An entry is a no-argument member whose name IS the component's type, so anything the caller must
    // supply at construction rules it out:
    //
    //  * no usable constructor at all;
    //  * a `required` member — the entry's `new T()` cannot even compile (CS9040).
    //
    // A RASK001-required prop (non-nullable, no member initializer) used to be the third, and no longer
    // is. It cost two things, and each now has its own answer rather than one shared veto:
    //
    //  * nothing enforced it at the call site — a factory makes it a required PARAMETER and the language
    //    reports an omitted one, while a chain just doesn't call that setter. RASK038 walks the chain
    //    and reports it now, including for a REFERENCED library's component, whose requiredness the
    //    owning assembly publishes as [assembly: RaskRequiredProperties] because metadata destroys it
    //    (CrossAssemblyRequiredPropertyTests),
    //  * and nothing put it back — a factory re-assigns every parameter each render, an entry hands back
    //    the same instance, so `Widget.Title("x")` then a bare `Widget` still had the title. The reset
    //    covers required props now, writing `default!` (IsResettableProp / ResetLiteralFor). That is the
    //    half a call-site analyzer cannot reach, and it is why the two had to land together.
    //
    // `required` used to withhold an entry outright, because it cannot ride on Entry<T> — constrained
    // `where T : Component, new()`, and a type with a required member does not satisfy `new()` (CS9040).
    // That is a CONSTRUCTION problem and it has a construction answer: requiredness is a compile-time
    // check, so ActivatorUtilities is allowed to build what `new T()` may not, and EntryDi / EntryBoundDi
    // (neither constrained `new()`) are the paths that do it. What enforces the value afterwards is
    // RASK038 at the chain — the same trade already made for a RASK001-required property.
    //
    // A `required` RAW DELEGATE used to block as well, and that one was not about construction either:
    // the prop was invocable, so a same-named setter could never be reached and the component would have
    // been constructible and permanently incomplete. A REQUIRED property is a step on the seed or a
    // pending stage — never on the component — so it is not on the receiver and cannot swallow its step
    // whatever its type; Validation.Message, Validation.Summary, Validation.Indicator, ToastOutlet, Shareable, the
    // Trigger.Gesture family and Ui.DataGrid's RowKey simply have entries.
    //
    // …and a name Component already declares (`Head`) still blocks too, which would be CS0102.
    // The entries an unseeded component is reached by: its own name, or one per tag of a [Tag] type several
    // tags share, whose body then stamps which tag it built (a type with one tag stamps it in its constructor).
    private static IEnumerable<(string Name, string? Tag)> EntriesOf(Candidate c)
    {
        if (c.Tags.Count > 1)
        {
            return c.Tags.Select(static t => (t.Entry, (string?)t.Name));
        }

        var name = c.Tags.Count == 1 ? c.EntryName : c.TypeName;
        return [(name, null)];
    }

    private static string EntryBody(Candidate c, string? tag, string assemblyName)
    {
        var sb = new StringBuilder();
        if (tag is not null)
        {
            sb.Append("global::Rask.Core.BuilderRuntime.Tag(");
        }

        sb.Append("global::Rask.Core.BuilderRuntime.").Append(EntryMethod(c)).Append(c.FullyQualifiedName).Append(">(");
        EmitResetArguments(sb, c, assemblyName);
        sb.Append(')');
        if (tag is not null)
        {
            sb.Append(", global::Rask.Core.RaskTags.").Append(TagConstant(tag)).Append(')');
        }

        return sb.ToString();
    }

    private static string TagConstant(string tag) => "Id_" + tag.Replace('-', '_');

    private static bool CanHaveEntry(Candidate c, HashSet<string> taken) =>
        (c.TypeParameters.Length == 0 ? c.HasParameterlessCtor || c.HasDIConstructor : HasGenericEntryShape(c))
        && !taken.Contains(c.TypeName) && !taken.Contains(c.EntryName);

    /// <summary>
    ///     Whether a <i>generic</i> component can have an entry: it must be constructible, and it must
    ///     have something to infer its type argument from.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A <c>required</c> member used to exclude it as well, and that exclusion was never about
    ///         construction (<c>EntryRequired</c> had already solved that) nor about generics. It was that
    ///         a generic component's entry is a <b>method</b>, and a method entry hides its same-named
    ///         factory inside a component body — so handing one to <c>BsMultiSelect</c>,
    ///         <c>BsRadioGroup</c> or <c>BsCheckboxGroup</c> breaks their multi-argument factory call
    ///         sites on the spot (CS1501/CS1739). It was deferred while that was an unscheduled
    ///         migration; the sites move in this pass, so it is lifted.
    ///     </para>
    ///     <para>
    ///         What enforces the required value afterwards is RASK038 at the chain — the same trade every
    ///         other <c>EntryRequired</c> component already makes.
    ///     </para>
    /// </remarks>
    private static bool HasGenericEntryShape(Candidate c) =>
        c.HasParameterlessCtor && PinSets(c).Count != 0;

    /// <summary>
    ///     The seed a generic component's entry hands back — the receiver its pins extend.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A property may not be generic, so a generic component cannot have a property entry that
    ///         yields the component itself. It can have one that yields an empty struct, with the type
    ///         arguments pinned by an extension method on that struct — which is what removes both the
    ///         empty <c>()</c> and the written type argument from every call site
    ///         (<c>Input.Bind(() =&gt; m.Name)</c> rather than <c>Input(() =&gt; m.Name)</c>, and
    ///         <c>BsRadioGroup.Options(all)</c> rather than <c>BsRadioGroup&lt;Plan&gt;()</c>).
    ///     </para>
    ///     <para>
    ///         One seed per component NAME, not per arity: the two arities of <c>BsSelect</c> share the
    ///         one entry member, so they share its type, and their pins are overloads on it. They no
    ///         longer collide, because a pin that has to account for more type parameters takes more
    ///         arguments — which is a different signature, not a return-type-only difference (CS0111).
    ///     </para>
    /// </remarks>
    private static string SeedFqn(Candidate c) =>
        (c.Namespace.Length == 0 ? "global::" : "global::" + c.Namespace + ".") + SeedName(c);

    // After the ENTRY, not the type: two components joined by [RaskChainEntry] are reached through one
    // seed, which is what makes them one name at the call site. Their states and stages keep their own
    // type names, so nothing else collides.
    private static string SeedName(Candidate c) => "RaskSeed_" + c.EntryName;

    // Named after the step that OPENS it, because two ways in carry different things: `Bind` parks an
    // expression and `Value` parks a value, so one stage type cannot serve both — its constructor would
    // have to take either.
    private static string StageName(Candidate c, EntryInference opening) =>
        "RaskStage_" + c.TypeName + "_" + opening.ParamName;

    private static string StageFqn(Candidate c, EntryInference opening, string typeParameters) =>
        (c.Namespace.Length == 0 ? "global::" : "global::" + c.Namespace + ".")
        + StageName(c, opening) + typeParameters;

    // Lift, when set, is the method the argument goes through before it is assigned: the nullable `Bind`
    // overload hands its `Expression<Func<T?>>` to ExpressionAccessor.NonNullable to become the property's
    // `Expression<Func<T>>`.
    private readonly record struct EntryInference(
        string ParamName,
        string ParamTypeFqn,
        string PropertyName,
        bool Track,
        int PendingBit,
        string Summary = "",
        string? Lift = null);

    // Construction that cannot be `new T()`: no parameterless constructor, or a required member the
    // language will not let `new()` satisfy.
    private static bool HasRequiredMember(Candidate c) => c.Properties.Any(static p => p.UserMarkedRequired);

    // The entries to emit, with same-name collisions removed and reported.
    //
    // Entries are all flattened onto ONE type — Rask.Core.Component, or each consumer component — and
    // keyed by SIMPLE NAME, while factories live in a per-namespace `Generated` class. So
    // `Features.Products.Card` and `Features.Orders.Card` both have a factory and cannot both have an
    // entry. Dropping the loser silently is the worst of the options: it compiles, and whichever one
    // the sort happened to put second simply has no entry (and, once the factory is deleted, no way to
    // be built at all). A collision between two types is not resolvable here — it is the author's to
    // resolve — so neither gets an entry and RASK040 says why.
    private static List<Candidate> EntryCandidates(
        SourceProductionContext spc, ImmutableArray<Candidate> candidates, HashSet<string> taken)
    {
        var result = new List<Candidate>();
        foreach (var group in DistinctByType(candidates).Where(c => CanHaveEntry(c, taken))
                     .GroupBy(static c => c.EntryName, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                result.Add(members[0]);
                continue;
            }

            // Joined on purpose: [RaskChainEntry] says these components share an entry, so the openings
            // sit side by side on one seed and the argument types tell them apart. Not a collision.
            if (members.Any(static c => !string.Equals(c.EntryName, c.TypeName, StringComparison.Ordinal)))
            {
                result.AddRange(members);
                continue;
            }

            // A generic component's entry is a METHOD, so same-named generic components of different
            // arity coexist as overloads (BsSelect<TItem> and BsSelect<TValue, TItem>). A non-generic
            // one's entry is a PROPERTY, which shares its name with nothing at all — not even a
            // generic method (CS0102) — so one of those in the group makes the whole group collide.
            if (members.Any(static c => c.TypeParameters.Length == 0))
            {
                ReportEntryCollision(spc, members);
                continue;
            }

            foreach (var byArity in members.GroupBy(static c => c.TypeParameters.Count(ch => ch == ',')))
            {
                var overloads = byArity.ToList();
                if (overloads.Count == 1)
                {
                    result.Add(overloads[0]);
                }
                else
                {
                    ReportEntryCollision(spc, overloads);
                }
            }
        }

        return result;
    }

    // The generic form control's method entry. It takes ONE parameter — the bind expression — because
    // that is what infers the value type (`Input.Bind(() => model.Age)` → `Input<int>`); the validator and the
    // post-bind hooks that force the factory's none/sync/async overload fan-out are setters instead.
    // When the control has more type parameters than the value type mentions (BsSelect<TValue, TItem>),
    // inference falls back to the caller writing them out — the entry still compiles and still works.
    // The three arguments every entry hands to Entry/EntryDi/EntryBound: how to put the non-folding
    // props back now, how to put the folding ones back at the end of the parent's Render(), and which
    // of the latter to consider. A component that adds nothing to the shared surface points straight at
    // BuilderRuntime's shared routines, so the 140 plain HTML tags need no per-tag reset at all.
    private static void EmitResetArguments(StringBuilder sb, Candidate c, string assemblyName,
        string typeArguments = "")
    {
        var shared = c.IsElement ? "Element" : "Component";
        if (NeedsOwnReset(c))
        {
            var setters = "global::RaskBuilderSetters" + assemblyName + ".";
            var args = typeArguments.Length != 0 ? typeArguments : c.TypeParameters;
            sb.Append(setters).Append(EagerResetName(c)).Append(args).Append(", ")
                .Append(setters).Append(PendingResetName(c)).Append(args).Append(", ");
        }
        else
        {
            sb.Append("global::Rask.Core.BuilderRuntime.Reset").Append(shared).Append("Eager, ")
                .Append("global::Rask.Core.BuilderRuntime.Reset").Append(shared).Append("Pending, ");
        }

        sb.Append("global::Rask.Core.BuilderRuntime.Shared").Append(shared).Append("Pending");
        var own = OwnPendingBits(c);
        if (own.Count != 0)
        {
            sb.Append(" | ").Append(MaskLiteral(own.Values));
        }

        // The fourth argument: does this component have a lifecycle to run when the parent's deferred
        // commit reaches it. Only the `false` is written — the runtime defaults to true, so generated
        // code from an older version stays correct rather than fast.
        if (!c.HasLifecycle)
        {
            sb.Append(", false");
        }

        // What replays the steps written before a Key step onto the instance the key claims (#1118). Named,
        // so it follows the optional lifecycle flag above whether or not that was written.
        if (!c.IsElement)
        {
            sb.Append(", copy: ");
            if (NeedsOwnReset(c))
            {
                var args = typeArguments.Length != 0 ? typeArguments : c.TypeParameters;
                sb.Append("global::RaskBuilderSetters").Append(assemblyName).Append('.').Append(CopyWrittenName(c))
                    .Append(args);
            }
            else
            {
                sb.Append("global::Rask.Core.BuilderRuntime.CopyComponentWritten");
            }
        }
    }
}
