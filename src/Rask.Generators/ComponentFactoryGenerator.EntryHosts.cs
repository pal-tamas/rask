using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Rask.Generators.ComponentSymbols;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    // A consumer's own components cannot ride on Component: a generator may not add members to a type
    // in a referenced assembly, and delivering entries via `using static` does not work either — a
    // static-imported property loses to a same-named type in scope (CS0119). So each component gets the
    // entries injected into its OWN partial, where a member of the enclosing type wins outright.
    //
    // That injection is per-HOST, so it is quadratic: N components in the assembly produce N×(N+M)
    // members, where M is everything reachable from referenced libraries. So none of it carries the
    // entry's actual body. Each assembly emits ONE canonical entry per component into a public
    // `RaskEntries{Assembly}` class (EmitEntryHost below), and every injected member is a one-line
    // forwarder onto it. That is what keeps the quadratic term a name and a delegation instead of a
    // reset triple, and it is the same member a REFERENCED assembly's components are reached through —
    // the two problems have one answer.
    private static void EmitConsumerEntries(
        SourceProductionContext spc,
        ImmutableArray<Candidate> candidates,
        bool injectEntries,
        ComponentHost host,
        ExternalEntrySet external,
        ImmutableArray<EntryHostDecl> extraHosts)
    {
        // A test project is the shape that needs the second half of this condition: it may declare no
        // component of its own at all and still have markup hosts that need the component library it is
        // testing injected into them.
        if (host.DeclaresComponent || (candidates.IsDefaultOrEmpty && extraHosts.IsDefaultOrEmpty))
        {
            return;
        }

        var entries = EntryCandidates(spc, candidates, EmptyNames);
        EmitEntryHost(spc, entries, host);
        EmitGroupedEntries(spc, entries, host.AssemblyName);

        // Publishing the entry host above is unconditional; injecting them into this assembly's own hosts
        // is not. A component LIBRARY opts the injection out (RaskBuilderEntryInjection=false) and keeps
        // the publication, so its consumers are unaffected — they still read `RaskEntries{Assembly}` and
        // still write `Div.Class("x")`. Inside the library itself the chain's own entries are simply not
        // offered — a tag component composes nothing, it renders itself from TagName and WriteAttributes.
        if (!injectEntries)
        {
            return;
        }

        var refs = OwnEntryRefs(entries, "global::" + EntryHostName(host.AssemblyName));
        refs.AddRange(UsableExternalEntries(spc, external.Libraries, refs, host));

        var hosts = EntryHostDecls(candidates, extraHosts);
        // The framework tags, for the one host shape that cannot inherit them. Read off Rask.Core's own
        // `RaskEntriesRaskCore` rather than re-derived, exactly as a referenced library's are — and only
        // materialised when some host actually needs them, because for everyone else this list is not
        // merely unnecessary but wrong (forwarding to a name you inherit is CS0108).
        var frameworkRefs = hosts.Any(static h => h.Delivery == Delivery.Injected)
            ? FrameworkEntriesFor(external.Framework, refs)
            : new List<EntryRef>();

        // The framework entry names, for the hosts that INHERIT them — every component, and every
        // `[RaskMarkup]` host whose generated partial writes `: RaskMarkup`. An own or referenced entry
        // of the same name is emitted with `new` rather than skipped, so the nearer component keeps the
        // simple name. Read off the entry set rather than off the host symbol on purpose: a Delivery.Base
        // host does not derive from RaskMarkup yet — the partial being generated is what adds it — so the
        // symbol would answer "inherits nothing" for exactly the case that needs the modifier most.
        var frameworkNames = new HashSet<string>(external.Framework.Select(static e => e.Name), StringComparer.Ordinal);
        if (refs.Count == 0 && frameworkRefs.Count == 0
                            && !hosts.Any(static h => h.Delivery == Delivery.Base))
        {
            return;
        }

        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);

        foreach (var host2 in hosts.Where(h => !ReportedUninjectable(spc, h)))
        {
            EmitHostPartial(sb, host2, refs, frameworkRefs, frameworkNames);
        }

        spc.AddSource("RaskBuilderConsumerEntries.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitHostPartial(
        StringBuilder sb, EntryHostDecl host2, List<EntryRef> refs, List<EntryRef> frameworkRefs, HashSet<string> frameworkNames)
    {
        sb.AppendLine();
        // Block-scoped: one file carries every host component, and a file may hold only one
        // file-scoped namespace declaration (CS8954).
        var hasNs = !string.IsNullOrEmpty(host2.Namespace);
        if (hasNs)
        {
            sb.Append("namespace ").AppendLine(host2.Namespace);
            sb.AppendLine("{");
        }

        // Re-open the enclosing types, outermost first. Each header carries the accessibility and
        // `static` the original declared: partial declarations may omit `sealed`/`abstract`, but
        // conflicting accessibility is CS0262 and a missing `static` is CS0261.
        foreach (var enclosing in host2.EnclosingTypes)
        {
            sb.AppendLine(enclosing);
            sb.AppendLine("{");
        }

        // A partial declaration may name the base class as long as only one of them does — so an
        // attributed type with a free base slot gets `: RaskMarkup` written here and inherits the
        // framework entries, which is the cheap delivery. The author never had to choose.
        sb.Append(host2.IsStatic ? "static partial class " : "partial class ")
            .Append(host2.TypeName).Append(host2.TypeParameters)
            .AppendLine(host2.Delivery == Delivery.Base ? " : global::Rask.Core.RaskMarkup" : string.Empty);
        sb.AppendLine("{");
        EmitHostForwarders(sb, host2, refs, frameworkRefs, frameworkNames);

        sb.AppendLine("}");
        for (var i = 0; i < host2.EnclosingTypes.Count; i++)
        {
            sb.AppendLine("}");
        }

        if (hasNs)
        {
            sb.AppendLine("}");
        }
    }

    private static void EmitHostForwarders(
        StringBuilder sb, EntryHostDecl host2, List<EntryRef> refs, List<EntryRef> frameworkRefs, HashSet<string> frameworkNames)
    {
        var declared = new HashSet<string>(host2.MemberNames, StringComparer.Ordinal);
        // An Injected host inherits nothing: its framework entries are emitted as SIBLINGS just above,
        // and FrameworkEntriesFor has already dropped the ones an own entry claims.
        var inherits = host2.Delivery != Delivery.Injected;
        if (host2.Delivery == Delivery.Injected)
        {
            foreach (var e in frameworkRefs)
            {
                if (string.Equals(e.Name, host2.TypeName, StringComparison.Ordinal)
                    || declared.Contains(e.Name))
                {
                    continue;
                }

                EmitEntryForwarder(sb, e, host2.TypeParameters);
            }
        }

        foreach (var e in refs)
        {
            // A member may not share its enclosing type's name (CS0542) — and a component never
            // needs an entry for itself anyway.
            if (string.Equals(e.Name, host2.TypeName, StringComparison.Ordinal)
                || declared.Contains(e.Name))
            {
                continue;
            }

            EmitEntryForwarder(sb, e, host2.TypeParameters, inherits && frameworkNames.Contains(e.Name));
        }
    }

    // Rask.Core's entries, minus every name this compilation already spends on one of its own or a
    // referenced library's component. A framework tag and a local component cannot both be the member
    // named `Card` (CS0102), and the local one is the name the author wrote.
    private static List<EntryRef> FrameworkEntriesFor(EquatableArray<EntryRef> framework, List<EntryRef> refs)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in refs)
        {
            taken.Add(e.Name);
        }

        var result = new List<EntryRef>();
        foreach (var e in framework.Where(e => !taken.Contains(e.Name)))
        {
            result.Add(e);
        }

        return result;
    }

    // The one canonical entry per component in this assembly, public so a REFERENCING assembly's
    // components can forward to it. Rask.Core needs no such class: its entries are members of Component
    // itself, which every component everywhere inherits.
    private static void EmitEntryHost(
        SourceProductionContext spc, List<Candidate> entries, ComponentHost host)
    {
        if (entries.Count == 0)
        {
            return;
        }

        const string runtime = "global::Rask.Core.BuilderRuntime.";
        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("///     This assembly's chain entries, one per component. Every component's own entry");
        sb.AppendLine("///     member — here and in any assembly that references this one — forwards to these, so");
        sb.AppendLine("///     the per-component injection stays one line. Global namespace, like the setters:");
        sb.AppendLine("///     a referencing assembly must be able to name it with no `using`.");
        sb.AppendLine("/// </summary>");
        sb.Append("public static class ").AppendLine(EntryHostName(host.AssemblyName));
        sb.AppendLine("{");

        var seeded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in entries)
        {
            // Its entry is a member of its group (EmitGroupedEntries); its seed and pins are still emitted below.
            if (c.Group is not null)
            {
                continue;
            }

            var visibility = c.IsPublic ? "public" : "internal";
            if (NeedsSeed(c))
            {
                // One seed property per component NAME; the steps that turn it into a component are
                // emitted below, as extensions.
                if (seeded.Add(c.EntryName))
                {
                    EmitEntryDoc(sb, c);
                    sb.Append("    ").Append(visibility).Append(" static ").Append(SeedFqn(c)).Append(' ')
                        .Append(EscapeIdentifier(c.EntryName)).AppendLine(" => default;");
                }

                continue;
            }

            // The entry opens a chain and hands back the component itself: the steps that follow are
            // extension methods on the component, and a carrier-typed property is what keeps one from
            // swallowing its own setter (see Rask.Core.Callback).
            foreach (var (name, tag) in EntriesOf(c))
            {
                EmitEntryDoc(sb, c);
                sb.Append("    ").Append(visibility).Append(" static ").Append(c.FullyQualifiedName)
                    .Append(' ').Append(EscapeIdentifier(name)).Append(" => ")
                    .Append(EntryBody(c, tag, host.AssemblyName)).AppendLine(";");
            }
        }

        sb.AppendLine("}");
        EmitSeeds(sb, entries, host.AssemblyName, runtime);
        spc.AddSource("RaskBuilderEntryHost.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string EntryHostName(string sanitizedAssemblyName) => "RaskEntries" + sanitizedAssemblyName;

    /// <summary>
    ///     The entries that live on a group class — <c>Ui.Button</c>, <c>Mui.Button</c> — one partial per group.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same member an entry host carries (see <see cref="EmitEntryHost" />), only declared on the group
    ///         and under the group's member name. Nothing is injected for it anywhere: the group is an ordinary
    ///         public class, so every assembly that can see the component reaches it by the group's name, and a
    ///         bare name the grouped component would have taken — <c>Button</c> — stays with the HTML tag.
    ///     </para>
    ///     <para>
    ///         Two components that land on one member are RASK040, like two components sharing a bare name; a group
    ///         that cannot be re-opened because it (or a type around it) is not partial is RASK036.
    ///     </para>
    /// </remarks>
    private static void EmitGroupedEntries(SourceProductionContext spc, List<Candidate> entries, string assemblyName)
    {
        const string runtime = "global::Rask.Core.BuilderRuntime.";
        var groups = entries.Where(static c => c.Group is not null)
            .GroupBy(static c => c.Group!.Fqn, StringComparer.Ordinal)
            .OrderBy(static g => g.Key, StringComparer.Ordinal)
            .ToList();
        if (groups.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);

        foreach (var group in groups)
        {
            var first = group.First().Group!;
            if (!first.IsPartial)
            {
                foreach (var c in group)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Rask036,
                        MakeDeclLocation(c),
                        $"'{first.Fqn.Replace("global::", string.Empty)}', the group '{c.TypeName}' is reached through, "
                        + "is not declared 'partial'",
                        $"its entry, '{first.Member}'"));
                }

                continue;
            }

            EmitGroup(spc, sb, group, first, assemblyName, runtime);
        }

        spc.AddSource("RaskBuilderGroupedEntries.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitGroup(
        SourceProductionContext spc, StringBuilder sb, IGrouping<string, Candidate> group, ChainGroup first, string assemblyName,
        string runtime)
    {
        sb.AppendLine();
        var hasNs = first.Namespace.Length != 0;
        if (hasNs)
        {
            sb.Append("namespace ").AppendLine(first.Namespace);
            sb.AppendLine("{");
        }

        foreach (var header in first.Headers)
        {
            sb.AppendLine(header);
            sb.AppendLine("{");
        }

        foreach (var byMember in group.GroupBy(static c => c.Group!.Member, StringComparer.Ordinal))
        {
            // Components joined by [RaskChainEntry] share one seed on purpose; anything else on one member is
            // two components claiming one name.
            var members = byMember.ToList();
            if (members.Select(static c => c.EntryName).Distinct(StringComparer.Ordinal).Count() > 1
                || (members.Count > 1 && members.Any(static c => !NeedsSeed(c))))
            {
                ReportEntryCollision(spc, members);
                continue;
            }

            var c = members[0];
            var visibility = c.IsPublic ? "public" : "internal";
            EmitEntryDoc(sb, c);
            if (NeedsSeed(c))
            {
                sb.Append("    ").Append(visibility).Append(" static ").Append(SeedFqn(c)).Append(' ')
                    .Append(EscapeIdentifier(byMember.Key)).AppendLine(" => default;");
                continue;
            }

            sb.Append("    ").Append(visibility).Append(" static ").Append(c.FullyQualifiedName)
                .Append(' ').Append(EscapeIdentifier(byMember.Key)).Append(" => ").Append(runtime)
                .Append(EntryMethod(c)).Append(c.FullyQualifiedName).Append(">(");
            EmitResetArguments(sb, c, assemblyName);
            sb.AppendLine(");");
        }

        for (var i = 0; i < first.Headers.Count; i++)
        {
            sb.AppendLine("}");
        }

        if (hasNs)
        {
            sb.AppendLine("}");
        }
    }

    // This assembly's own entries, described the way a forwarder needs them.
    //
    // A generic component forwards exactly like a non-generic one — one property, no type parameters,
    // no argument list — because its entry IS a property now: it hands back a seed, and the pins that
    // turn the seed into the component are extension methods in the global namespace, which a
    // referencing assembly already reaches without anything being forwarded to it. One per component
    // NAME, since both arities of a two-arity component share the member.
    private static List<EntryRef> OwnEntryRefs(List<Candidate> entries, string hostFqn)
    {
        var refs = new List<EntryRef>();
        var seeded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in entries)
        {
            // A grouped entry is a member of its group, never forwarded into a host under a bare name.
            if (c.Group is not null || (NeedsSeed(c) && !seeded.Add(c.EntryName)))
            {
                continue;
            }

            foreach (var (name, _) in NeedsSeed(c) ? [(c.EntryName, null)] : EntriesOf(c))
            {
                refs.Add(new EntryRef(
                    hostFqn,
                    name,
                    NeedsSeed(c) ? SeedFqn(c) : c.FullyQualifiedName,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty));
            }
        }

        return refs;
    }

    // Which referenced-assembly entries this compilation can actually inject.
    //
    //  * a name Component already carries (its own members, plus every tag entry Rask.Core emitted) would
    //    HIDE that member — CS0108, an error here — and the inherited entry is the better one anyway,
    //  * a name one of this assembly's own components already claims stays with the local component: it
    //    is the one the author wrote, and two members cannot share a name (CS0102),
    //  * the same name from two different libraries is not resolvable here at all, so neither is used and
    //    RASK040 says which types collided — the same answer two same-named local components get.
    private static List<EntryRef> UsableExternalEntries(
        SourceProductionContext spc, EquatableArray<EntryRef> external, List<EntryRef> own, ComponentHost host)
    {
        var result = new List<EntryRef>();
        if (external.Count == 0)
        {
            return result;
        }

        var taken = new HashSet<string>(host.MemberNames, StringComparer.Ordinal);
        foreach (var e in own)
        {
            taken.Add(e.Name);
        }

        var byName = new Dictionary<string, List<EntryRef>>(StringComparer.Ordinal);
        foreach (var e in external)
        {
            if (taken.Contains(e.Name))
            {
                continue;
            }

            if (!byName.TryGetValue(e.Name, out var list))
            {
                byName[e.Name] = list = new List<EntryRef>();
            }

            list.Add(e);
        }

        foreach (var pair in byName.OrderBy(static p => p.Key, StringComparer.Ordinal))
        {
            var hosts = new HashSet<string>(pair.Value.Select(static e => e.HostFqn), StringComparer.Ordinal);
            if (hosts.Count > 1)
            {
                var names = string.Join("', '",
                    pair.Value.Select(static e => e.ReturnTypeFqn).Distinct(StringComparer.Ordinal)
                        .OrderBy(static n => n, StringComparer.Ordinal));
                spc.ReportDiagnostic(Diagnostic.Create(Rask040, Location.None, pair.Key, names));
                continue;
            }

            result.AddRange(pair.Value);
        }

        return result;
    }

    // One injected member: the entry's own signature, delegating to the canonical one.
    private static void EmitEntryForwarder(
        StringBuilder sb, EntryRef e, string hostTypeParameters, bool hidesInheritedEntry = false)
    {
        var typeParameters = e.TypeParameters;
        var constraints = e.Constraints;
        var returnType = e.ReturnTypeFqn;
        var parameters = e.Parameters;
        // A method's type parameter may not reuse an enclosing type's name (CS0693), and these are
        // injected INTO components, generic ones included.
        var reserved = typeParameters.Length == 0 ? EmptyNames : ParseTypeParameters(hostTypeParameters);
        foreach (var name in reserved.Count == 0 ? EmptyNames : ParseTypeParameters(typeParameters))
        {
            if (!reserved.Contains(name))
            {
                continue;
            }

            var underscores = 1;
            while (reserved.Contains(name + new string('_', underscores)))
            {
                underscores++;
            }

            var renamed = name + new string('_', underscores);

            typeParameters = RenameTypeParameter(typeParameters, name, renamed);
            constraints = RenameTypeParameter(constraints, name, renamed);
            returnType = RenameTypeParameter(returnType, name, renamed);
            parameters = RenameTypeParameter(parameters, name, renamed);
        }

        // Point at the entry this forwards to rather than copying its summary. The canonical entry lives
        // in another assembly, and an <inheritdoc/> is resolved by the IDE — which has the whole solution
        // — where the generator only has metadata. Copying text here would either duplicate it or, when
        // the metadata carries no docs, emit an empty comment that suppresses the tooltip entirely.
        sb.Append("    /// <inheritdoc cref=\"").Append(e.HostFqn).Append('.').Append(e.Name)
            .AppendLine("\"/>");
        // `new` when the name is already an INHERITED framework entry. Hiding is what the author asked
        // for by naming a component after a tag, and it is the precedence the chain has always had — the
        // nearer component wins. Without the modifier this is CS0108, an error under warnings-as-errors,
        // without the forwarder at all the tag would quietly win instead and the markup would render the
        // wrong element with a green build.
        sb.Append("    private static ").Append(hidesInheritedEntry ? "new " : string.Empty)
            .Append(returnType).Append(' ').Append(EscapeIdentifier(e.Name))
            .Append(typeParameters).Append(parameters).Append(constraints).Append(" => ").Append(e.HostFqn)
            .Append('.').Append(EscapeIdentifier(e.Name)).Append(typeParameters).Append(e.Arguments)
            .AppendLine(";");
    }

    // Every entry a referenced assembly publishes, read straight off its `RaskEntries{Assembly}` class.
    //
    // Reading the emitted MEMBERS rather than re-deriving entries from the referenced components is the
    // point: whether a component can have an entry at all depends on its constructors, its required
    // members and its RASK001 props, and that question was already answered — correctly, with the
    // diagnostics reported — by the compilation that owns it. Re-asking it here from metadata would be a
    // second, silently divergent copy of CanHaveEntry.
    //
    // Rask.Core's own entries come back in a SEPARATE list, because almost nothing wants them: a
    // component, and any host that derives from RaskMarkup, already has them by inheritance, and
    // forwarding to them as well would hide the inherited member (CS0108). Only a `[RaskMarkup]` host
    // that cannot inherit — its base slot spent, or it is `static` — needs them injected.
    private static ExternalEntrySet ScanExternalEntries(Compilation compilation)
    {
        var component = compilation.GetTypeByMetadataName(ComponentFullName);
        if (component is null)
        {
            return default;
        }

        var declaring = component.ContainingAssembly;
        var libraries = new List<EntryRef>();
        var framework = new List<EntryRef>();
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            var hostType = assembly.GetTypeByMetadataName(EntryHostName(SanitizeIdentifier(assembly.Name)));
            if (hostType is null)
            {
                continue;
            }

            // An INTERNAL component publishes an `internal static` entry (EmitEntryHost), so taking public
            // members only would tell a friend assembly about neither the component nor its entry — even
            // though it can see both. With the factory gone there is no second spelling to fall back on,
            // and the fully-qualified entry host is all that is left. `GivesAccessTo` is the same question
            // the compiler asks of `InternalsVisibleTo`.
            var friend = assembly.GivesAccessTo(compilation.Assembly);
            if (!VisibleToEntryScan(hostType.DeclaredAccessibility, friend))
            {
                continue;
            }

            var into = SymbolEqualityComparer.Default.Equals(assembly, declaring) ? framework : libraries;
            var hostFqn = hostType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            foreach (var member in hostType.GetMembers())
            {
                if (!member.IsStatic || !VisibleToEntryScan(member.DeclaredAccessibility, friend))
                {
                    continue;
                }

                switch (member)
                {
                    case IPropertySymbol { IsIndexer: false } p:
                        into.Add(new EntryRef(hostFqn, p.Name, DisplayTypeName(p.Type, FullyQualifiedNullable, compilation),
                            string.Empty, string.Empty, string.Empty, string.Empty));
                        break;
                    case IMethodSymbol { MethodKind: MethodKind.Ordinary } m:
                        into.Add(ExternalMethodEntry(hostFqn, m, compilation));
                        break;
                }
            }
        }

        return new ExternalEntrySet(SortedEntries(libraries), SortedEntries(framework));
    }

    // Public entries are visible everywhere; internal ones only across an `InternalsVisibleTo` grant. The
    // injected forwarder is `private static`, so an internal entry's type never leaks past the host.
    private static bool VisibleToEntryScan(Accessibility accessibility, bool friend) =>
        accessibility == Accessibility.Public || (friend && accessibility == Accessibility.Internal);

    // What a referenced assembly publishes, split by whether this compilation's hosts already inherit it.
    private readonly record struct ExternalEntrySet(
        EquatableArray<EntryRef> Libraries,
        EquatableArray<EntryRef> Framework);

    private static EquatableArray<EntryRef> SortedEntries(List<EntryRef> entries)
    {
        if (entries.Count == 0)
        {
            return default;
        }

        entries.Sort(static (a, b) =>
        {
            var byName = string.CompareOrdinal(a.Name, b.Name);
            if (byName != 0)
            {
                return byName;
            }

            var byHost = string.CompareOrdinal(a.HostFqn, b.HostFqn);
            return byHost != 0 ? byHost : string.CompareOrdinal(a.Parameters, b.Parameters);
        });
        return new EquatableArray<EntryRef>(entries.ToImmutableArray());
    }

    private static EntryRef ExternalMethodEntry(string hostFqn, IMethodSymbol m, Compilation compilation)
    {
        var typeParameters = m.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", m.TypeParameters.Select(static tp => tp.Name)) + ">";
        var parameters = new StringBuilder("(");
        var arguments = new StringBuilder("(");
        for (var i = 0; i < m.Parameters.Length; i++)
        {
            if (i != 0)
            {
                parameters.Append(", ");
                arguments.Append(", ");
            }

            var p = m.Parameters[i];
            parameters.Append(DisplayTypeName(p.Type, FullyQualifiedNullable, compilation)).Append(' ')
                .Append(EscapeIdentifier(p.Name));
            arguments.Append(EscapeIdentifier(p.Name));
        }

        return new EntryRef(
            hostFqn,
            m.Name,
            DisplayTypeName(m.ReturnType, FullyQualifiedNullable, compilation),
            typeParameters,
            BuildConstraintsClause(m.TypeParameters),
            parameters.Append(')').ToString(),
            arguments.Append(')').ToString());
    }

    // Nothing is "taken" inside a consumer's own partial: the CS0542 self-name case is filtered before
    // the entry is emitted, and a user member that collides is the RASK0xx `new` story, not this one.
    private static readonly HashSet<string> EmptyNames = new(StringComparer.Ordinal);

    // `new T()` needs a public parameterless ctor; anything else goes through ActivatorUtilities —
    // the same split the factory emission makes via canUseObjectInit.
    private static bool NeedsDiEntry(Candidate c) => !c.HasParameterlessCtor;

    /// <summary>
    ///     The <c>BuilderRuntime</c> construction helper an entry for <paramref name="c" /> routes through,
    ///     as a name ready to be followed by its type argument list.
    /// </summary>
    /// <remarks>
    ///     <c>new T()</c> is the cheap default. A component with no parameterless constructor needs the
    ///     service provider (<c>EntryDi</c>). One that has a parameterless constructor but declares a
    ///     <c>required</c> member needs a construction the LANGUAGE forbids to <c>new T()</c> (CS9040) and
    ///     which is legal reflectively, since requiredness carries no runtime enforcement
    ///     (<c>EntryRequired</c>) — that is the whole of what used to keep those components off the
    ///     builder surface.
    /// </remarks>
    private static string EntryMethod(Candidate c) =>
        (NeedsDiEntry(c), HasRequiredMember(c)) switch
        {
            (true, _) => "EntryDi<",
            (_, true) => "EntryRequired<",
            _ => "Entry<",
        };

    private static Location MakeDeclLocation(Candidate c) =>
        string.IsNullOrEmpty(c.DeclFilePath)
            ? Location.None
            : Location.Create(
                c.DeclFilePath,
                new TextSpan(c.DeclSpanStart, c.DeclSpanLength),
                new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));

    private static Location MakeDeclLocation(EntryHostDecl h) =>
        string.IsNullOrEmpty(h.DeclFilePath)
            ? Location.None
            : Location.Create(
                h.DeclFilePath,
                new TextSpan(h.DeclSpanStart, h.DeclSpanLength),
                new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));

    // Every type in this compilation that entries are injected INTO: the concrete components (which also
    // have an entry of their own) plus the abstract component bases (which never can). One per TYPE — a
    // partial class carrying a base list in two files reaches either syntax provider twice — and ordered
    // for a deterministic emission.
    //
    // The forwarders are `private static`, so a base and a derived class both receiving them is not
    // hiding: CS0108 only fires for an inherited member the derived type can SEE. That is also why
    // injecting into the base alone is not enough — a subclass cannot reach its base's private members,
    // so each class needs its own copy, exactly as the concrete-only emission already did.
    private static List<EntryHostDecl> EntryHostDecls(
        ImmutableArray<Candidate> candidates, ImmutableArray<EntryHostDecl> extraHosts)
    {
        var all = new List<EntryHostDecl>(candidates.Length + extraHosts.Length);
        foreach (var c in candidates)
        {
            all.Add(new EntryHostDecl(c.FullyQualifiedName, c.Namespace, c.TypeName, c.TypeParameters,
                c.IsPartial, c.IsNested, c.DeclFilePath, c.DeclSpanStart, c.DeclSpanLength,
                MemberNames: c.MemberNames,
                EnclosingTypes: c.EnclosingTypes,
                EnclosingAllPartial: c.EnclosingAllPartial));
        }

        if (!extraHosts.IsDefaultOrEmpty)
        {
            all.AddRange(extraHosts);
        }

        return all
            .GroupBy(static h => h.Key, StringComparer.Ordinal)
            .Select(static g => g.First())
            .OrderBy(static h => h.Key, StringComparer.Ordinal)
            .ToList();
    }

    // An injection host that is not a candidate, read purely as a host. Deliberately none of Candidate's
    // construction facts (constructors, props, form-control shape): nothing here is ever built.
    //
    // Two shapes qualify, and the difference is which of them can be CONSTRUCTED, not which can host:
    //
    //  * an abstract component — a concrete one is already a candidate and gets its host decl from there,
    //  * anything deriving from RaskMarkup that is not a Component, abstract or not. That is the opt-in
    //    for code outside a component: a test class writes `: RaskMarkup` and the framework entries come
    //    by inheritance, while its own assembly's and its referenced libraries' come from here,
    //  * anything carrying [RaskMarkup]. Same host, opted in by an attribute instead of a base — for the
    //    two shapes that cannot spend a base slot at all: one whose base belongs to someone else, and a
    //    `static class`. See MarkupDelivery for how the attribute picks between inheriting and injecting.
    private static EntryHostDecl? GetExtraHost(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (symbol.IsUnboundGenericType)
        {
            return null;
        }

        if (symbol.DeclaredAccessibility != Accessibility.Public &&
            symbol.DeclaredAccessibility != Accessibility.Internal)
        {
            return null;
        }

        if (IsInRaskCoreNamespace(symbol) || HasSkipFactoryAttribute(symbol))
        {
            return null;
        }

        // A concrete component is a candidate; only its abstract bases need collecting here. A markup
        // host is never a component, so `abstract` says nothing about it either way.
        var isComponent = InheritsFromComponent(symbol);
        var isMarkupHost = !isComponent && (DeclaresRaskMarkup(symbol) || HasRaskMarkupAttribute(symbol));
        if (!isMarkupHost && !(symbol.IsAbstract && isComponent))
        {
            return null;
        }

        var delivery = MarkupDelivery(symbol, isComponent);

        return new EntryHostDecl(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            symbol.IsGenericType
                ? "<" + string.Join(", ", symbol.TypeParameters.Select(static tp => tp.Name)) + ">"
                : string.Empty,
            classDecl.Modifiers.Any(SyntaxKind.PartialKeyword),
            symbol.ContainingType is not null,
            classDecl.Identifier.GetLocation().SourceTree?.FilePath ?? string.Empty,
            classDecl.Identifier.Span.Start,
            classDecl.Identifier.Span.Length,
            symbol.IsStatic,
            delivery,
            ReachableMemberNames(symbol),
            EnclosingTypeHeaders(symbol),
            AllEnclosingPartial(symbol));
    }

    // Every name an injected entry must leave alone: this type's own members and its whole base chain's.
    //
    // Collected for EVERY delivery, which it was not. The list used to be gathered only for the injected
    // delivery, on the reasoning that an inherited entry a member happens to shadow is merely hidden and
    // `new` says so. True — of the FRAMEWORK entries, which is the only half that arrives by inheritance.
    // A consuming assembly's own components, and a referenced library's, are injected as MEMBERS into
    // every host whatever its delivery, so a component nested inside the host is both a type it declares
    // and a member it is about to be given: CS0102, in generated source, out of a one-line opt-in. It cost
    // 190 test classes in Rask.Core.Tests their builder surface.
    //
    // An INJECTED entry has no out: against this type's own member it is a second member of the same name
    // (CS0102, which no modifier fixes), and against a BASE's it silently hides something belonging to a
    // type the author does not control (CS0108, an error under warnings-as-errors). Both answers are the
    // same one — the name stays with the member that is already there, and the entry is not injected.
    // The enclosing types of a nested host, outermost first, each already written as the partial header
    // the generated file re-opens it with. Accessibility and `static` are replicated because a partial
    // declaration may not conflict on either (CS0262 / CS0261); `sealed` and `abstract` may be omitted.
    private static EquatableArray<string> EnclosingTypeHeaders(INamedTypeSymbol symbol)
    {
        var headers = new List<string>();
        for (var t = symbol.ContainingType; t is not null; t = t.ContainingType)
        {
            var kind = t.TypeKind == TypeKind.Struct ? "struct" : "class";
            var typeParams = t.IsGenericType
                ? "<" + string.Join(", ", t.TypeParameters.Select(static p => p.Name)) + ">"
                : string.Empty;
            headers.Add(
                $"{AccessibilityKeyword(t)}{(t.IsStatic ? "static " : string.Empty)}partial {kind} {t.Name}{typeParams}");
        }

        headers.Reverse();
        return new EquatableArray<string>(headers.ToArray());
    }

    private static bool AllEnclosingPartial(INamedTypeSymbol symbol)
    {
        for (var t = symbol.ContainingType; t is not null; t = t.ContainingType)
        {
            var partial = false;
            foreach (var reference in t.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is TypeDeclarationSyntax decl
                    && decl.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    partial = true;
                    break;
                }
            }

            if (!partial)
            {
                return false;
            }
        }

        return true;
    }

    private static string AccessibilityKeyword(INamedTypeSymbol symbol) => symbol.DeclaredAccessibility switch
    {
        Accessibility.Public => "public ",
        Accessibility.Internal => "internal ",
        Accessibility.Private => "private ",
        Accessibility.Protected => "protected ",
        Accessibility.ProtectedOrInternal => "protected internal ",
        Accessibility.ProtectedAndInternal => "private protected ",
        _ => string.Empty
    };

    private static EquatableArray<string> ReachableMemberNames(INamedTypeSymbol symbol)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        for (var t = symbol; t is not null; t = t.BaseType)
        {
            // RaskMarkup's members are the framework entries, and EmitConsumerEntries answers for those
            // separately (it hides them with `new`). Counting them HERE would instead filter out the
            // host's own component entry of the same name and hand the simple name to the TAG —
            // silently, and only at render time.
            if (string.Equals(t.OriginalDefinition.ToDisplayString(), RaskMarkupFullName, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var name in t.MemberNames)
            {
                names.Add(name);
            }

            // Nested types are members too, and they are the ones that bite: an entry is named after its
            // component, so a component nested inside the host collides with its own entry. Taken from
            // GetTypeMembers rather than trusting MemberNames to have carried them.
            foreach (var nested in t.GetTypeMembers())
            {
                names.Add(nested.Name);
            }
        }

        // A Blazor island's properties come from the component it hosts, so they are not members yet —
        // they are generated into a second part of this same class. Without them here, a hosted
        // parameter named after a component (Label, Title, Form, Select — ordinary names for a UI
        // library) would land beside the entry injected under that same name, in the same class:
        // CS0102, and nothing able to hide it. Naming them now makes the injection skip them, through
        // the collision check that already exists for every other member.
        foreach (var step in Blazor.BlazorParameters.StepNames(symbol))
        {
            names.Add(step);
        }

        return new EquatableArray<string>(names.ToArray());
    }

    /// <summary>
    ///     How a host gets the <i>framework</i> entries — the 166 tags Rask.Core emits onto
    ///     <c>RaskMarkup</c>. Three answers, and the point of the attribute is that it picks the cheapest
    ///     one the type can actually use rather than making the author pick.
    /// </summary>
    private enum Delivery
    {
        /// <summary>
        ///     Already inherited: a component, an abstract component base, or a type that wrote
        ///     <c>: RaskMarkup</c> itself. Nothing to emit, and emitting anything would hide the
        ///     inherited member (CS0108).
        /// </summary>
        Inherited,

        /// <summary>
        ///     <c>[RaskMarkup]</c> on a type whose base slot is still free: the generated <c>partial</c>
        ///     writes <c>: RaskMarkup</c> for it. Identical to having typed it — a partial declaration may
        ///     name the base class as long as only one does — so the cost is the same ~77 forwarders any
        ///     other host pays, and the attribute is never the expensive choice when it does not have to be.
        /// </summary>
        Base,

        /// <summary>
        ///     <c>[RaskMarkup]</c> on a type that cannot inherit: the base slot is spent on someone else's
        ///     type, or the type is <c>static</c>. All 166 framework entries are injected as forwarders
        ///     onto <c>RaskEntriesRaskCore</c> — several times the generated source of the other two, which
        ///     is why this is the fallback and not the mechanism.
        /// </summary>
        Injected,
    }

    private static Delivery MarkupDelivery(INamedTypeSymbol symbol, bool isComponent)
    {
        if (isComponent || DeclaresRaskMarkup(symbol) || !HasRaskMarkupAttribute(symbol))
        {
            return Delivery.Inherited;
        }

        // A static class can derive from nothing; anything else with an untouched base slot can be given
        // one. `object` is what "untouched" looks like on the symbol, and interfaces do not spend it.
        return !symbol.IsStatic && symbol.BaseType is null or { SpecialType: SpecialType.System_Object }
            ? Delivery.Base
            : Delivery.Injected;
    }

    // A type that builder entries are injected into. Value-equatable, like Candidate, because it is an
    // incremental-generator input.
    private readonly record struct EntryHostDecl(
        string Key,
        string Namespace,
        string TypeName,
        string TypeParameters,
        bool IsPartial,
        bool IsNested,
        string DeclFilePath,
        int DeclSpanStart,
        int DeclSpanLength,
        bool IsStatic = false,
        Delivery Delivery = Delivery.Inherited,
        EquatableArray<string> MemberNames = default,
        // The enclosing types this host is nested in, outermost first, each already written as the partial
        // header to re-open it with ("internal static partial class Outer<T>"). Empty for a top-level host.
        EquatableArray<string> EnclosingTypes = default,
        // Whether every one of them is declared `partial` — the precondition for injecting at all.
        bool EnclosingAllPartial = false);

    private readonly record struct ComponentHost(
        bool DeclaresComponent,
        string AssemblyName,
        EquatableArray<string> MemberNames,
        // Whether the INTERNAL half of Component's `protected internal` members is visible here: true in Core
        // itself and in every assembly Core names as a friend. It is not the same question as DeclaresComponent,
        // and an override's modifier depends on this one.
        bool SeesComponentInternals = false);

    /// <summary>The generator's MSBuild switches, read once per compilation.</summary>
    /// <param name="InjectEntries">
    ///     RaskBuilderEntryInjection — whether this compilation's own entries are also injected into its own
    ///     host partials, and whether it re-emits the universal setter surface. Off for a component library,
    ///     which publishes both for consumers but does not hand them to itself a second time.
    /// </param>
    private readonly record struct BuilderOptions(bool InjectEntries);

    // One canonical entry, as a forwarder needs to restate it. Covers both shapes: a property
    // (TypeParameters/Parameters/Arguments all empty) and a generic form control's method overload
    // (Parameters "(… Bind)" or "()", Arguments "(Bind)" or "()"). Kept as strings rather than symbols
    // because it is an incremental-generator input — it must be value-equatable, and a symbol is not.
    private readonly record struct EntryRef(
        string HostFqn,
        string Name,
        string ReturnTypeFqn,
        string TypeParameters,
        string Constraints,
        string Parameters,
        string Arguments);
}
