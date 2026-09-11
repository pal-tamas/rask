using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Generators.External.PackageIslands;

/// <summary>A property the author declared on a package island by hand.</summary>
internal sealed record UserProp(string ClrName, string WireName);

/// <summary>
///     What both generators need to know about an island class to pair it with a snapshot. Symbol-free,
///     so it can ride on the factory generator's cached candidate.
/// </summary>
/// <param name="Name">The class's simple name — the snapshot is <c>{Name}.props.json</c>.</param>
/// <param name="Namespace">Its namespace, or null for the global namespace.</param>
/// <param name="Runtime">The runtime its base class declares.</param>
/// <param name="Module">Its constant <c>Module</c> override, or null when it declares none.</param>
/// <param name="IsPublic">Whether generated types beside it must be public to match it.</param>
/// <param name="Directories">The normalised directories its declarations live in.</param>
/// <param name="UserProps">The props declared on the class itself.</param>
/// <param name="Reserved">
///     Names a generated prop must not take: Rask's own members, every member the class declares itself, and
///     the class's name.
/// </param>
/// <param name="TypeNames">
///     The types already declared in the island's namespace. A generated enum or record lands in that namespace,
///     so it must not take one of these names.
/// </param>
internal sealed record IslandFacts(
    string Name,
    string? Namespace,
    string Runtime,
    string? Module,
    bool IsPublic,
    EquatableArray<string> Directories,
    EquatableArray<UserProp> UserProps,
    EquatableArray<string> Reserved,
    EquatableArray<string> TypeNames)
{
    /// <summary>Whether <see cref="Module" /> names a package rather than a file beside the class.</summary>
    public bool IsPackage => Module is not null && PackageSpecifier.IsBare(Module);
}

/// <summary>Whether a snapshot can be used for the island it sits beside.</summary>
internal enum PackageVerdict
{
    Usable,
    Unreadable,
    RuntimeMismatch,
    ModuleMismatch,
}

/// <summary>A C# type a snapshot type maps to, as a tree the emitter walks.</summary>
/// <param name="Kind"><c>string</c>, <c>number</c>, <c>boolean</c>, <c>date</c>, <c>enum</c>, <c>union</c>, <c>record</c>, <c>list</c> or <c>map</c>.</param>
/// <param name="Fqn">The non-nullable type, fully qualified.</param>
/// <param name="IsValueType">Whether <c>?</c> makes it a <c>Nullable&lt;T&gt;</c>.</param>
/// <param name="Element">A list's element or a map's value.</param>
/// <param name="ElementNullable">Whether that element may be null.</param>
internal sealed record CsType(string Kind, string Fqn, bool IsValueType, CsType? Element, bool ElementNullable)
{
    /// <summary>The type spelled as a nullable one.</summary>
    public string NullableFqn => Fqn + "?";
}

/// <summary>A literal of a generated enum, with the member name it got.</summary>
internal sealed record GeneratedEnumMember(string Member, SnapshotLiteral Literal);

/// <summary>A member of a generated record.</summary>
internal sealed record GeneratedRecordMember(
    string ClrName,
    string Wire,
    CsType Type,
    bool Required,
    bool Nullable,
    string? Doc);

/// <summary>A type generated beside the island: an enum, a record, or a string-or-number union.</summary>
internal sealed record GeneratedType(
    string Kind,
    string Name,
    string Summary,
    EquatableArray<GeneratedEnumMember> EnumMembers,
    EquatableArray<GeneratedRecordMember> RecordMembers);

/// <summary>How a callback prop's argument crosses back into C#.</summary>
/// <param name="ArgIndex">The position of the forwarded argument, or -1 when none is forwarded.</param>
/// <param name="ArgType">The forwarded argument's C# type, or null for an argless callback.</param>
/// <param name="ArgNullable">Whether the forwarded argument may be null.</param>
internal sealed record CallbackInfo(int ArgIndex, CsType? ArgType, bool ArgNullable);

/// <summary>One package prop, ready for both generators.</summary>
/// <param name="Name">The prop's name in the package.</param>
/// <param name="Wire">The JSON key it is written under.</param>
/// <param name="ClrName">The C# property and chain step.</param>
/// <param name="ChainTypeFqn">The property's type, nullable unless required.</param>
/// <param name="IsRequired">Whether the chain requires it.</param>
/// <param name="Nullable">Whether null is a legal value for the package.</param>
/// <param name="DeclaredByUser">Whether the author declared it by hand, so it is written but not declared.</param>
/// <param name="Type">The mapped value type, or null for a callback.</param>
/// <param name="Callback">The callback shape, or null for a value.</param>
/// <param name="Summary">The XML text of its summary, escaped and on one line.</param>
/// <param name="Doc">The package's documentation, for the multi-line doc comment.</param>
/// <param name="Default">The package's documented default.</param>
internal sealed record PackageProp(
    string Name,
    string Wire,
    string ClrName,
    string ChainTypeFqn,
    bool IsRequired,
    bool Nullable,
    bool DeclaredByUser,
    CsType? Type,
    CallbackInfo? Callback,
    string Summary,
    string? Doc,
    string? Default);

/// <summary>A prop that could not be generated, and why.</summary>
internal sealed record PropProblem(string PropName, string Reason, int Line, int Column);

/// <summary>What a snapshot resolves to for one island.</summary>
internal sealed record PackageIsland(
    PackageVerdict Verdict,
    string? VerdictDetail,
    EquatableArray<PackageProp> Props,
    EquatableArray<GeneratedType> Types,
    EquatableArray<PropProblem> Problems);

/// <summary>Reading and splitting a package module specifier.</summary>
internal static class PackageSpecifier
{
    /// <summary>
    ///     Whether <paramref name="module" /> names a package (<c>@mui/material/Button</c>) rather than a
    ///     file (<c>./Chart.tsx</c>), a root path, a Node subpath import (<c>#internal</c>) or a URL.
    /// </summary>
    public static bool IsBare(string module)
    {
        if (module.Length == 0)
        {
            return false;
        }

        var first = module[0];
        if (first is '.' or '/' or '\\' or '#')
        {
            return false;
        }

        if (module.IndexOf("://", StringComparison.Ordinal) >= 0 || (module.Length > 1 && module[1] == ':'))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     The specifier and the export: <c>"@mui/material#Button"</c> is <c>(@mui/material, Button)</c>, and
    ///     a specifier without a <c>#</c> names the default export.
    /// </summary>
    public static (string Specifier, string Export) Split(string module)
    {
        var hash = module.LastIndexOf('#');
        return hash > 0 && hash < module.Length - 1
            ? (module.Substring(0, hash), module.Substring(hash + 1))
            : (module, "default");
    }
}

/// <summary>
///     Reads a package island's committed props snapshot and resolves it into the props, types and
///     problems both generators act on.
/// </summary>
/// <remarks>
///     <para>
///         Shared for the same reason <c>BlazorParameters</c> is: <c>ExternalGenerator</c> declares the
///         properties and writes them, <c>ComponentFactoryGenerator</c> emits their chain steps, and no
///         source generator can see another's output. Computed twice, they would disagree the first time a
///         naming rule changed — and a step for a property that does not exist is a compile error in code
///         the author never wrote.
///     </para>
///     <para>
///         <see cref="ResolveAll" /> is deterministic and symbol-free: the same islands and snapshots always
///         give the same names in the same order, whichever generator asks. Both generators resolve every
///         package island of the compilation together, because a generated type lands at namespace level and
///         two islands can produce the same name for one.
///     </para>
/// </remarks>
internal static class PackageIslandProps
{
    /// <summary>The file name suffix a snapshot carries.</summary>
    public const string SnapshotSuffix = ".props.json";

    private const string SkipFactoryName = "Rask.Core.SkipFactoryAttribute";
    private const string RaskMarkupName = "Rask.Core.RaskMarkup";

    /// <summary>How deep an inline object may nest before it is refused rather than generated.</summary>
    private const int MaxDepth = 4;

    /// <summary>Every props snapshot in the compilation, parsed once for both generators.</summary>
    public static IncrementalValueProvider<EquatableArray<PropsSnapshot>> Snapshots(
        IncrementalGeneratorInitializationContext context) =>
        context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(SnapshotSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(static (text, ct) => PropsSnapshotReader.Read(
                text.Path,
                text.GetText(ct)?.ToString() ?? string.Empty))
            .Collect()
            .Select(static (all, _) => Dedupe(all));

    /// <summary>
    ///     The facts about <paramref name="type" /> as an island, or null when it is not one both generators
    ///     act on.
    /// </summary>
    /// <remarks>
    ///     The eligibility here mirrors what makes a class a factory candidate — concrete, public or internal,
    ///     not <c>[SkipFactory]</c> — so the island generator and the factory generator resolve exactly the same
    ///     set of islands. They have to: type names are allocated across that set, and an island one of them
    ///     skipped would shift the other's names.
    /// </remarks>
    public static IslandFacts? Facts(INamedTypeSymbol type)
    {
        if (type.IsAbstract
            || type.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)
            || HasSkipFactory(type)
            || ExternalRuntimes.RuntimeOf(type) is not { } runtime)
        {
            return null;
        }

        var module = ModuleLiteral.Read(type);

        var directories = type.DeclaringSyntaxReferences
            .Select(static r => AssetPairing.NormalizeDirectory(r.SyntaxTree.FilePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static d => d, StringComparer.Ordinal)
            .ToArray();

        var userProps = new List<UserProp>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer || property.SetMethod is null
                || property.DeclaredAccessibility != Accessibility.Public
                || HasSkipFactory(property))
            {
                continue;
            }

            userProps.Add(new UserProp(property.Name, WireShape.WireName(property)));
        }

        // Names a generated prop must never take. Rask's own members first: hiding Key would silently steal
        // reconciliation identity (#950 is that bug, for Blazor), and hiding Hydration or Module would detach
        // the island from what mounts it. RaskMarkup is left out: its members are the static chain entries,
        // which a prop may shadow with `new` exactly as Element's Title does. Then everything the class itself
        // declares — a [SkipFactory] property, a field, a method, a nested type — because a second declaration
        // of any of those names in the generated half is CS0102.
        //
        // From GetMembers(), never from MemberNames. On a type read from METADATA — Component and ExternalComponent,
        // in every app — MemberNames is not a stable answer: before anything has asked for the type's members it
        // lists the raw metadata names (private fields, backing fields, internal helpers), and once GetMembers()
        // has run it lists only the members that were loaded. So the same island yielded two different sets of
        // facts depending on what some earlier code had touched, the facts stopped comparing equal, and the island
        // silently generated nothing. GetMembers() loads the members itself, so it answers the same every time.
        var reserved = new SortedSet<string>(StringComparer.Ordinal) { type.Name };
        foreach (var member in type.GetMembers())
        {
            reserved.Add(member.Name);
        }

        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.OriginalDefinition.ToDisplayString() == RaskMarkupName)
            {
                continue;
            }

            foreach (var member in t.GetMembers())
            {
                // A private base member cannot collide with a declaration in a derived class.
                if (member.DeclaredAccessibility != Accessibility.Private)
                {
                    reserved.Add(member.Name);
                }
            }
        }

        var typeNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var existing in type.ContainingNamespace.GetTypeMembers())
        {
            typeNames.Add(existing.Name);
        }

        return new IslandFacts(
            type.Name,
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            runtime,
            module.Failed ? null : module.Value,
            IsExternallyVisible(type),
            new EquatableArray<string>(directories),
            new EquatableArray<UserProp>(userProps),
            new EquatableArray<string>(reserved),
            new EquatableArray<string>(typeNames));
    }

    /// <summary>The snapshot beside <paramref name="facts" />'s class, or null when there is none.</summary>
    public static PropsSnapshot? Find(EquatableArray<PropsSnapshot> snapshots, IslandFacts facts)
    {
        if (!facts.IsPackage || snapshots.Count == 0)
        {
            return null;
        }

        var fileName = facts.Name + SnapshotSuffix;
        foreach (var snapshot in snapshots)
        {
            var path = snapshot.Path.Replace('\\', '/');
            var slash = path.LastIndexOf('/');
            var name = slash < 0 ? path : path.Substring(slash + 1);
            if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var directory = AssetPairing.NormalizeDirectory(snapshot.Path);
            foreach (var candidate in facts.Directories)
            {
                if (string.Equals(candidate, directory, StringComparison.OrdinalIgnoreCase))
                {
                    return snapshot;
                }
            }
        }

        return null;
    }

    /// <summary>
    ///     Resolves every paired package island of one compilation together, keyed by its facts.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Together, because generated enums, records and unions are declared at namespace level: island
    ///         <c>Data</c> with a <c>gridMode</c> prop and island <c>DataGrid</c> with a <c>mode</c> prop both
    ///         want <c>DataGridMode</c>. Names are therefore allocated from one table per namespace, seeded with
    ///         the types the namespace already declares, in a fixed order — so a clash takes a numeric suffix
    ///         rather than failing the build, and both generators allocate the same suffix.
    ///     </para>
    ///     <para>
    ///         A partial class reaches the factory generator once per declaration, so the same facts can arrive
    ///         more than once; each island is resolved once, or its own names would push its second resolution
    ///         onto suffixes.
    ///     </para>
    /// </remarks>
    public static Dictionary<IslandFacts, PackageIsland> ResolveAll(
        IEnumerable<(IslandFacts Facts, PropsSnapshot Snapshot)> islands)
    {
        var result = new Dictionary<IslandFacts, PackageIsland>();
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var ordered = islands
            .OrderBy(static i => i.Facts.Namespace ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(static i => i.Facts.Name, StringComparer.Ordinal)
            .ThenBy(static i => i.Snapshot.Path, StringComparer.Ordinal);

        foreach (var (facts, snapshot) in ordered)
        {
            if (result.ContainsKey(facts))
            {
                continue;
            }

            var ns = facts.Namespace ?? string.Empty;
            if (!tables.TryGetValue(ns, out var taken))
            {
                taken = new HashSet<string>(StringComparer.Ordinal);
                tables[ns] = taken;
            }

            foreach (var name in facts.TypeNames)
            {
                taken.Add(name);
            }

            result[facts] = Resolve(snapshot, facts, taken);
        }

        return result;
    }

    /// <summary>The chain step names a usable snapshot adds to its island's class.</summary>
    public static IEnumerable<string> StepNames(PackageIsland island) =>
        island.Props.Where(static p => !p.DeclaredByUser).Select(static p => p.ClrName);

    /// <summary>
    ///     The position of the first argument of a callback that is not an event, or -1 when every argument
    ///     is one.
    /// </summary>
    /// <remarks>
    ///     Which argument the client forwards to C#. An event object never crosses: it holds DOM nodes and
    ///     <c>view: window</c>, so serializing it throws and the call is lost.
    /// </remarks>
    public static int ForwardedArgIndex(SnapshotType callback)
    {
        for (var i = 0; i < callback.Args.Count; i++)
        {
            if (!IsEvent(callback.Args[i].Type))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Whether an argument is a DOM or synthetic event rather than a value.</summary>
    internal static bool IsEvent(SnapshotType type) =>
        type.Kind == "event"
        || (type.Kind is "object" or "ref" && type.Name is { } name && name.EndsWith("Event", StringComparison.Ordinal));

    private static PackageIsland Resolve(PropsSnapshot snapshot, IslandFacts facts, HashSet<string> takenTypeNames)
    {
        if (snapshot.Defect is not null)
        {
            return Verdict(PackageVerdict.Unreadable, snapshot.Defect);
        }

        if (!string.Equals(snapshot.Runtime, facts.Runtime, StringComparison.Ordinal))
        {
            return Verdict(
                PackageVerdict.RuntimeMismatch,
                $"the '{snapshot.Runtime}' runtime, but the class is a '{facts.Runtime}' island");
        }

        var (specifier, export) = PackageSpecifier.Split(facts.Module!);
        if (!string.Equals(snapshot.Module, specifier, StringComparison.Ordinal)
            || !string.Equals(snapshot.Export, export, StringComparison.Ordinal))
        {
            return Verdict(
                PackageVerdict.ModuleMismatch,
                $"'{Describe(snapshot.Module, snapshot.Export)}', but the class names '{facts.Module}'");
        }

        return new Resolver(snapshot, facts, takenTypeNames).Run();
    }

    private static string Describe(string module, string export) =>
        string.Equals(export, "default", StringComparison.Ordinal) ? module : module + "#" + export;

    private static PackageIsland Verdict(PackageVerdict verdict, string detail) =>
        new(verdict, detail, default, default, default);

    private static EquatableArray<PropsSnapshot> Dedupe(ImmutableArray<PropsSnapshot> all)
    {
        // The same file can reach the compiler twice — a project that lists it explicitly on top of the
        // build's glob — and two identical snapshots would otherwise read as two candidates for one island.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<PropsSnapshot>(all.Length);
        foreach (var snapshot in all.OrderBy(static s => s.Path, StringComparer.Ordinal))
        {
            if (seen.Add(snapshot.Path.Replace('\\', '/')))
            {
                unique.Add(snapshot);
            }
        }

        return new EquatableArray<PropsSnapshot>(unique);
    }

    private static bool HasSkipFactory(ISymbol symbol) =>
        symbol.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == SkipFactoryName);

    private static bool IsExternallyVisible(INamedTypeSymbol type)
    {
        for (ISymbol? s = type; s is INamedTypeSymbol t; s = t.ContainingType)
        {
            if (t.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>One resolution: carries the name tables so every generated name is unique and stable.</summary>
    private sealed class Resolver
    {
        private readonly PropsSnapshot _snapshot;
        private readonly IslandFacts _facts;
        private readonly List<PackageProp> _props = new();
        private readonly List<GeneratedType> _types = new();
        private readonly List<PropProblem> _problems = new();
        private readonly HashSet<string> _propNames = new(StringComparer.Ordinal);
        private readonly HashSet<string> _typeNames;
        private readonly Dictionary<string, CsType> _refs = new(StringComparer.Ordinal);
        private readonly HashSet<string> _reserved;
        private readonly string _typePrefix;

        public Resolver(PropsSnapshot snapshot, IslandFacts facts, HashSet<string> takenTypeNames)
        {
            _snapshot = snapshot;
            _facts = facts;
            _typeNames = takenTypeNames;
            _typeNames.Add(facts.Name);
            _reserved = new HashSet<string>(facts.Reserved, StringComparer.Ordinal);
            _typePrefix = facts.Namespace is null ? "global::" : "global::" + facts.Namespace + ".";
        }

        public PackageIsland Run()
        {
            // Names the author already owns are taken first, so a generated prop can never collide with one.
            foreach (var user in _facts.UserProps)
            {
                _propNames.Add(user.ClrName);
            }

            for (var i = 0; i < _snapshot.Props.Count; i++)
            {
                ResolveProp(_snapshot.Props[i], i);
            }

            return new PackageIsland(
                PackageVerdict.Usable,
                null,
                new EquatableArray<PackageProp>(_props),
                new EquatableArray<GeneratedType>(_types),
                new EquatableArray<PropProblem>(_problems));
        }

        private void ResolveProp(SnapshotProp prop, int index)
        {
            // `$h`, `$a`, `$d` and `$c` are how the wire carries handlers, arguments, dates and children. A
            // package prop spelled like one would be revived as one on the client.
            if (prop.Wire.StartsWith("$", StringComparison.Ordinal))
            {
                Problem(prop, "its name starts with '$', which the props wire reserves for its own markers");
                return;
            }

            var user = _facts.UserProps.FirstOrDefault(u =>
                string.Equals(u.WireName, prop.Wire, StringComparison.Ordinal)
                || string.Equals(u.ClrName, PackageIslandNaming.Identifier(prop.Name, string.Empty), StringComparison.Ordinal));

            if (user is not null)
            {
                // The author's declaration IS the mapping: it keeps its own type, and nothing is generated.
                _props.Add(new PackageProp(
                    prop.Name, prop.Wire, user.ClrName, string.Empty, prop.Required, prop.Type.Nullable,
                    true, null, null, string.Empty, prop.Doc, prop.Default));
                return;
            }

            var clrName = PackageIslandNaming.Identifier(prop.Name, "Prop" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (_reserved.Contains(clrName))
            {
                clrName += "Prop";
            }

            if (_reserved.Contains(clrName) || !_propNames.Add(clrName))
            {
                Problem(prop, $"it maps to the C# name '{clrName}', which another member of the island already has");
                return;
            }

            var summary = PackageIslandNaming.SummaryText(prop.Doc, $"Sent to the package as `{prop.Wire}`.");

            if (prop.Type.Kind == "callback")
            {
                if (ResolveCallback(prop) is not { } callback)
                {
                    _propNames.Remove(clrName);
                    return;
                }

                var fqn = callback.ArgType is null
                    ? "global::Rask.Core.Callback?"
                    : $"global::Rask.Core.Callback<{Spell(callback.ArgType, callback.ArgNullable)}>?";

                // Never required, even when the package's type says so: an unwired callback is simply not
                // wired, and a chain cannot usefully insist that an event be handled — the Blazor precedent.
                _props.Add(new PackageProp(
                    prop.Name, prop.Wire, clrName, fqn, false, false, false, null, callback,
                    summary, prop.Doc, prop.Default));
                return;
            }

            var mapped = Map(prop.Type, _facts.Name + clrName, 0, out var reason);
            if (mapped is null)
            {
                _propNames.Remove(clrName);
                Problem(prop, reason!);
                return;
            }

            var nullable = prop.Type.Nullable;
            var chainType = prop.Required ? Spell(mapped, nullable) : mapped.NullableFqn;

            _props.Add(new PackageProp(
                prop.Name, prop.Wire, clrName, chainType, prop.Required, nullable, false, mapped, null,
                summary, prop.Doc, prop.Default));
        }

        private CallbackInfo? ResolveCallback(SnapshotProp prop)
        {
            if (prop.Type.Returns)
            {
                Problem(prop, "the package needs a value back from it synchronously, and a call into C# is asynchronous");
                return null;
            }

            var index = ForwardedArgIndex(prop.Type);
            if (index < 0)
            {
                return new CallbackInfo(-1, null, false);
            }

            // The first argument that is not an event decides the shape. A scalar or an enum crosses; any other
            // argument is left in the browser and the callback is argless — still wired, and still useful,
            // which beats refusing the prop outright.
            var arg = prop.Type.Args[index];
            if (Map(arg.Type, _facts.Name + PackageIslandNaming.Identifier(prop.Name, "Callback") + "Arg", 0, out _) is { } mapped
                && mapped.Kind is "string" or "number" or "boolean" or "enum")
            {
                return new CallbackInfo(index, mapped, arg.Type.Nullable || arg.Optional);
            }

            return new CallbackInfo(-1, null, false);
        }

        private CsType? Map(SnapshotType type, string name, int depth, out string? reason)
        {
            reason = null;
            if (depth > MaxDepth)
            {
                reason = $"its type nests more than {MaxDepth} objects deep";
                return null;
            }

            switch (type.Kind)
            {
                case "string":
                    return new CsType("string", "string", false, null, false);

                case "number":
                    // TypeScript's number is an IEEE double with no integer signal, so an int would refuse
                    // the 0.5 a package accepts — while .Elevation(2) still binds through int→double.
                    return new CsType("number", "double", true, null, false);

                case "boolean":
                    return new CsType("boolean", "bool", true, null, false);

                case "date":
                    return new CsType("date", "global::System.DateTimeOffset", true, null, false);

                case "enum":
                    return MapEnum(type, name, out reason);

                case "union":
                    return MapUnion(type, name, out reason);

                case "array":
                {
                    if (type.Element is null)
                    {
                        reason = "it is an array whose element type the snapshot does not describe";
                        return null;
                    }

                    var element = Map(type.Element, name + "Item", depth, out reason);
                    return element is null
                        ? null
                        : new CsType(
                            "list",
                            $"global::System.Collections.Generic.IReadOnlyList<{Spell(element, type.Element.Nullable)}>",
                            false, element, type.Element.Nullable);
                }

                case "record":
                {
                    if (type.Element is null)
                    {
                        reason = "it is a map whose value type the snapshot does not describe";
                        return null;
                    }

                    var value = Map(type.Element, name + "Value", depth, out reason);
                    return value is null
                        ? null
                        : new CsType(
                            "map",
                            $"global::System.Collections.Generic.IReadOnlyDictionary<string, {Spell(value, type.Element.Nullable)}>",
                            false, value, type.Element.Nullable);
                }

                case "object":
                    return MapObject(type.Members, name, depth, out reason);

                case "ref":
                    return MapRef(type, depth, out reason);

                case "callback":
                    reason = "a function nested inside another value cannot be called back into C#";
                    return null;

                case "event":
                case "unknown":
                    reason = "its type has no JSON encoding";
                    return null;

                default:
                    reason = $"the snapshot uses a kind this Rask.External does not know ('{type.Kind}') — update Rask.External";
                    return null;
            }
        }

        private CsType? MapEnum(SnapshotType type, string name, out string? reason)
        {
            reason = null;
            if (type.Values.Count == 0)
            {
                reason = "it is a union with no literal values";
                return null;
            }

            var typeName = PackageIslandNaming.Unique(name, _typeNames);
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var members = new List<GeneratedEnumMember>();
            foreach (var literal in type.Values)
            {
                var member = literal.IsBoolean
                    ? (literal.Text == "true" ? "True" : "False")
                    : PackageIslandNaming.EnumMember(literal.Text, literal.IsNumber, "Value");
                members.Add(new GeneratedEnumMember(PackageIslandNaming.Unique(member, taken), literal));
            }

            var summary = type.Open
                ? $"The values `{_facts.Name}` accepts here. The package also accepts other strings; declare the prop as `string?` on the island to pass one."
                : $"The values `{_facts.Name}` accepts here, each sent as the package's own literal rather than as a number.";

            _types.Add(new GeneratedType(
                "enum", typeName, PackageIslandNaming.SummaryText(null, summary),
                new EquatableArray<GeneratedEnumMember>(members), default));

            return new CsType("enum", _typePrefix + typeName, true, null, false);
        }

        private CsType? MapUnion(SnapshotType type, string name, out string? reason)
        {
            reason = null;
            var kinds = type.Of.Select(static t => t.Kind).Distinct(StringComparer.Ordinal).OrderBy(static k => k, StringComparer.Ordinal).ToArray();
            if (kinds.Length == 2 && kinds[0] == "number" && kinds[1] == "string")
            {
                var typeName = PackageIslandNaming.Unique(name, _typeNames);
                _types.Add(new GeneratedType(
                    "union", typeName,
                    PackageIslandNaming.SummaryText(null, "A string or a number, as the package accepts either."),
                    default, default));
                return new CsType("union", _typePrefix + typeName, true, null, false);
            }

            reason = $"it is a union of {string.Join(", ", kinds)}, which has no single C# type — declare it on the island in C#";
            return null;
        }

        private CsType? MapObject(EquatableArray<SnapshotMember> members, string name, int depth, out string? reason)
        {
            reason = null;
            if (members.Count == 0)
            {
                reason = "it is an object type the snapshot declares no members for";
                return null;
            }

            var typeName = PackageIslandNaming.Unique(name, _typeNames);
            var taken = new HashSet<string>(StringComparer.Ordinal) { typeName };
            var generated = new List<GeneratedRecordMember>();

            foreach (var member in members)
            {
                var clr = PackageIslandNaming.Unique(PackageIslandNaming.Identifier(member.Name, "Member"), taken);
                var mapped = member.Type.Kind == "callback"
                    ? null
                    : Map(member.Type, typeName + clr, depth + 1, out _);

                // A member that cannot be expressed is dropped from the record rather than failing the whole
                // prop: the package still receives every member that can be sent, and treats the rest as
                // unset — which is what an optional member means to it.
                if (mapped is null)
                {
                    if (member.Required)
                    {
                        reason = $"its required member '{member.Name}' has no JSON encoding";
                        return null;
                    }

                    continue;
                }

                generated.Add(new GeneratedRecordMember(clr, member.Name, mapped, member.Required, member.Type.Nullable, member.Doc));
            }

            _types.Add(new GeneratedType(
                "record", typeName,
                PackageIslandNaming.SummaryText(null, "A value the package accepts for this prop."),
                default, new EquatableArray<GeneratedRecordMember>(generated)));

            return new CsType("record", _typePrefix + typeName, false, null, false);
        }

        private CsType? MapRef(SnapshotType type, int depth, out string? reason)
        {
            reason = null;
            if (type.Name is not { Length: > 0 } refName || _snapshot.NamedType(refName) is not { } target)
            {
                reason = $"it refers to a type the snapshot does not declare ('{type.Name}')";
                return null;
            }

            if (_refs.TryGetValue(refName, out var existing))
            {
                return existing;
            }

            var mapped = target.Kind == "object"
                ? MapObject(target.Members, _facts.Name + PackageIslandNaming.Identifier(refName, "Type"), depth + 1, out reason)
                : Map(target, _facts.Name + PackageIslandNaming.Identifier(refName, "Type"), depth + 1, out reason);

            if (mapped is not null)
            {
                _refs[refName] = mapped;
            }

            return mapped;
        }

        private void Problem(SnapshotProp prop, string reason) =>
            _problems.Add(new PropProblem(prop.Name, reason, prop.Line, prop.Column));

        private static string Spell(CsType type, bool nullable) => nullable ? type.NullableFqn : type.Fqn;
    }
}
