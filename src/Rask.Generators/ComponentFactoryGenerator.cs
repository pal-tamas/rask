using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rask.Generators;

[Generator(LanguageNames.CSharp)]
public sealed partial class ComponentFactoryGenerator : IIncrementalGenerator
{
    private const string RaskMarkupFullName = "Rask.Core.RaskMarkup";
    private const string RaskMarkupAttributeFullName = "Rask.Core.RaskMarkupAttribute";
    private const string ElementFullName = "Rask.Core.Element";
    private const string SkipFactoryFullName = "Rask.Core.SkipFactoryAttribute";
    private const string FactoryGenericFullName = "Rask.Core.FactoryGenericAttribute";
    private const string ChainEntryFullName = "Rask.Core.RaskChainEntryAttribute";
    private const string TagFullName = "Rask.Core.TagAttribute";
    private const string ChainGroupFullName = "Rask.Core.RaskChainGroupAttribute";
    private const string FormControlOpenFullName = "Rask.Core.Forms.IFormControl<T>";

    // The IFormControl<T> members that belong to BOUND mode: excluded from the synthesized controlled
    // factory, and (for Bind/AfterBind) emitted as params on the synthesized bound factory.
    //
    // `AfterBindAsync` and `ValidateAsync` are gone from this list because they are gone from the
    // interface: the post-bind hook is one `Callback<T>` and the rule is one `Validator<T>`, each taking
    // either shape. The list is matched by NAME, so a stale entry here is not a compile error — it is a
    // member that quietly stops being mode-gated.
    private static readonly string[] BoundInterfaceMembers =
        { "Bind", "Validate", "AfterBind" };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var grouped = GroupedCandidates(context);
        var extraHosts = ExtraHosts(context);

        // RASK001 / RASK002 are reported here rather than beside an emission, because what they are
        // about — which properties a chain MUST name — is a property of the component, not of anything
        // generated from it.
        context.RegisterSourceOutput(grouped, static (spc, c) => ReportPropertyDiagnostics(spc, c));

        var builderEnabled = BuilderEnabled(context);
        // Whether this build carries the devtools. Opt-IN, unlike the switch above: absent means off, so a Release
        // build — and any build that never asked for the tools — emits no description of an app's own state.
        var devToolsOn = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            p.GlobalOptions.TryGetValue("build_property.RaskDevTools", out var on)
            && string.Equals(on, "true", StringComparison.OrdinalIgnoreCase));

        var componentHost = context.CompilationProvider.Select(static (c, _) => GetComponentHost(c));

        // The host is combined in because the override's own modifier depends on it: `protected internal` is what
        // Core and its friends write over its own virtual, and `protected` is what everyone else must write.
        context.RegisterSourceOutput(grouped.Combine(devToolsOn).Combine(componentHost),
            static (spc, t) => EmitPropsDescribers(spc, t.Left.Left, t.Left.Right, t.Right.SeesComponentInternals));

        context.RegisterSourceOutput(grouped.Combine(componentHost),
            static (spc, t) => EmitBuilderEntries(spc, t.Left, t.Right));

        context.RegisterSourceOutput(grouped.Combine(Dom.DomSnapshot.VoidTags(context)),
            static (spc, t) => EmitTagTable(spc, t.Left, t.Right));

        RegisterConsumerEntries(context, grouped, builderEnabled, componentHost, extraHosts);

        // Which of each component's properties a builder chain MUST set. Published as assembly attributes
        // because it is the one thing about a component that metadata destroys: a member initializer
        // compiles into the constructor, so from a referencing compilation `string Title` and
        // `string Title = ""` are the same symbol and RASK038 cannot tell an optional property from a
        // required one. This compilation can — it is the same rule RASK001 applies right here — so
        // it publishes the answer rather than leaving a consumer to re-derive one it cannot reach.
        context.RegisterSourceOutput(grouped,
            static (spc, c) => EmitPublishedRequiredProperties(spc, c));

        // Setters. Emitted into the GLOBAL namespace: an extension method is only found when its
        // containing namespace is in scope, and the global namespace encloses every namespace — so
        // this is what lets `Div.Class("panel")` bind with no `using` anywhere. The class name carries
        // the assembly name because several assemblies each contribute one.
        var setterHost = context.CompilationProvider.Select(static (c, _) => GetSetterHost(c));

        context.RegisterSourceOutput(grouped.Combine(builderEnabled).Combine(setterHost),
            static (spc, t) => EmitBuilderSetters(
                spc, t.Left.Left, t.Left.Right.InjectEntries, t.Right));
    }

    // Components in REFERENCED assemblies (Rask.Bootstrap's Bs*, any third-party component library)
    // are in neither of the two paths above: they are not Rask.Core's, so they cannot ride on
    // Component, and they are not in this compilation's syntax, so they are not consumer candidates.
    // Each assembly publishes its own entries as a public `RaskEntries{Assembly}` class, which is what
    // this finds. CompilationProvider yields a fresh Compilation per keystroke, so the result is
    // wrapped in an EquatableArray and the emission only re-runs when the entry SET changes.
    private static void RegisterConsumerEntries(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<Candidate>> grouped,
        IncrementalValueProvider<BuilderOptions> builderEnabled,
        IncrementalValueProvider<ComponentHost> componentHost,
        IncrementalValueProvider<ImmutableArray<EntryHostDecl>> extraHosts)
    {
        var externalEntries = context.CompilationProvider.Select(static (c, _) => ScanExternalEntries(c));

        context.RegisterSourceOutput(
            grouped.Combine(builderEnabled).Combine(componentHost).Combine(externalEntries)
                .Combine(extraHosts),
            static (spc, t) =>
                EmitConsumerEntries(spc, t.Left.Left.Left.Left, t.Left.Left.Left.Right.InjectEntries,
                    t.Left.Left.Right, t.Left.Right, t.Right));
    }

    private static IncrementalValueProvider<ImmutableArray<Candidate>> GroupedCandidates(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c && c.BaseList is { Types.Count: > 0 } &&
                                    !c.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                static (ctx, _) => GetCandidates(ctx))
            .SelectMany(static (c, _) => c);

        // A package island's props come from its committed snapshot — an additional file the syntax transform
        // above cannot read — so they are merged in after collection. See WithPackageProps.
        var grouped = candidates.Collect()
            .Combine(External.PackageIslands.PackageIslandProps.Snapshots(context))
            .Select(static (t, _) => WithPackageProps(t.Left, t.Right))
            .WithComparer(CandidateListComparer.Instance);

        return grouped;
    }

    private static IncrementalValueProvider<ImmutableArray<EntryHostDecl>> ExtraHosts(IncrementalGeneratorInitializationContext context)
    {
        // The two kinds of injection host that are NOT candidates — neither has an entry of its own, and
        // both need the surface injected into their own partial:
        //
        //  * an ABSTRACT component. Nothing can construct it, so it gets no factory and no entry, but it
        //    is still a component, and an abstract base that composes other components (UiElement,
        //    UiFormField<T>, PollingPanel) could otherwise name no entry at all.
        //  * a MARKUP host: a type deriving from Rask.Core.RaskMarkup, or carrying [RaskMarkup], that is
        //    not a Component. This is how the surface reaches code that is not inside a component — a
        //    test class, a fixture, a factory of demo components — which is a quarter of every call site
        //    in this repo. The attribute is the form for a type that cannot spend its base slot.
        var extraHosts = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c
                                    && (c.BaseList is { Types.Count: > 0 } || c.AttributeLists.Count > 0),
                static (ctx, _) => GetExtraHost(ctx))
            .Where(static h => h is not null)
            .Select(static (h, _) => h!.Value)
            .Collect();

        return extraHosts;
    }

    private static IncrementalValueProvider<BuilderOptions> BuilderEnabled(IncrementalGeneratorInitializationContext context)
    {
        // Only the assembly that DECLARES
        // Rask.Core.Component can add entry members to its hierarchy, so this emission is scoped to that
        // compilation; a consumer's own components are handled separately (they are injected into
        // the consumer's own partial class, since a generator cannot add members to a type in a
        // referenced assembly).
        //
        // The entries land on Rask.Core.RaskMarkup, which Component derives from, not on Component
        // itself. Same members, same inheritance, one extra link — and it is what lets a type that is
        // NOT a component (a test class, a fixture, a demo factory) reach the surface, by deriving from
        // the half of Component that is only the markup. Emitting them a second time onto a separate
        // markup base was the alternative, and two emissions of one surface are two things free to drift.
        // RaskBuilderEntryInjection is the one switch here, and it is opt-OUT (absent means on).
        // It turns off only the half of the consumer path that injects a forwarder per entry into every
        // local host partial, while still publishing this assembly's `RaskEntries{Assembly}` class so a
        // REFERENCING compilation keeps seeing the entries. A component LIBRARY wants exactly that split:
        // a library that IS a large entry set would inject every one of its components into every other,
        // which is O(n²) generated members whose names collide with the props those hosts inherit from
        // Element (`Style`, `Data`, `Title`, `Cite`, …). Rask.Core itself needs none of this: its entries
        // land on RaskMarkup and are INHERITED, which emits nothing per host.
        //
        // There is no switch for the surface itself. There used to be, back when a generated factory was
        // the other way to write markup; turning the chain off now would leave a project with no way to
        // build a component at all.
        var builderEnabled = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            new BuilderOptions(
                !p.GlobalOptions.TryGetValue("build_property.RaskBuilderEntryInjection", out var inject)
                || !string.Equals(inject, "false", StringComparison.OrdinalIgnoreCase)));

        return builderEnabled;
    }

    // What an asynchronous validation rule hands back: the messages, once the check completes.
    private const string ValidationTaskFqn =
        "global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<string>>";

    private sealed record Candidate(
        string Namespace,
        string TypeName,
        // The NAME the chain is reached by, which is the type's own unless [RaskChainEntry] joins it to
        // another component's entry — two controls that are one control to the page writing them, told
        // apart by the types their openings take (Ui.Select over a value, and over a collection of them).
        string EntryName,
        string FullyQualifiedName,
        string TypeParameters,
        // One entry per type parameter, in declaration order: the attribute text to repeat in front of
        // that parameter wherever it is DECLARED, or "" when it carries none. See
        // TypeParameterAnnotations for why the chain has to repeat them.
        EquatableArray<string> TypeParameterAnnotations,
        string TypeParameterConstraints,
        bool HasParameterlessCtor,
        bool HasDIConstructor,
        bool IsPublic,
        GenericFactoryConfig? GenericFactory,
        FormControlInfo? FormControl,
        EquatableArray<PropInfo> Properties,
        bool IsPartial,
        bool IsNested,
        // Drives which shared reset the builder entry hands to Entry<T>: an Element gets the whole
        // universal HTML/event surface put back, a plain Component only Component's own props.
        bool IsElement,
        // Whether the component overrides any of Component's own On* hooks. Handed to Entry<T> so an
        // entry-built child that has a lifecycle claims its LiveState at build time and the deferred
        // commit can still read "no LiveState" as "not mine to notify" — see OverridesLifecycleHook.
        bool HasLifecycle,
        // File path + span rather than a Location: Location is not value-equatable, so caching it on
        // the candidate would defeat the incremental generator's comparison (same reason PropInfo
        // stores DeclaringFilePath/Span and rebuilds via MakeLocation).
        string DeclFilePath,
        int DeclSpanStart,
        int DeclSpanLength,
        // The component's own <summary>, carried onto every factory that builds it — see
        // EmitMethodHeader. Empty when the component has none, which keeps today's `<see cref>`
        // breadcrumb as the fallback.
        string Summary = "",
        // Every name an injected entry must leave alone: this type's own members and its whole base chain's.
        // Carried on the candidate because a candidate IS an injection host, and the host decl built from it
        // used to leave this empty — so the collision filter had nothing to filter against. It went unnoticed
        // while the tag entries arrived by INHERITANCE (a member merely shadows one, and `new` says so); the
        // moment a tag family became a referenced library its entries are injected as members instead, and an
        // injected member that collides is CS0102/CS0108, not a hint.
        EquatableArray<string> MemberNames = default,
        // The enclosing types, outermost first, each written as the partial header that re-opens it — what
        // lets a NESTED component be injected into. Empty for a top-level component.
        EquatableArray<string> EnclosingTypes = default,
        bool EnclosingAllPartial = false,
        // What pairs an island with its committed props snapshot, when this is one — symbol-side facts only,
        // because the snapshot itself is an additional file the syntax transform cannot read. See
        // WithPackageProps, which adds the steps once snapshots and candidates are both in hand.
        global::Rask.Generators.External.PackageIslands.IslandFacts? Package = null,
        // Where the entry lives when it is not on the markup surface — `Ui.Button` rather than `Ui.Button`. See
        // RaskChainGroupAttribute; null for every component reached by its bare name.
        ChainGroup? Group = null,
        // The tags a [Tag] element renders, each with its entry: one for a tag of its own, several for a type
        // the DOM shares between tags (h1–h6 are one heading element). Empty for everything else.
        EquatableArray<TagInfo> Tags = default);

    /// <summary>One <c>[Tag]</c> on an element type: the tag it renders and the entry that builds it.</summary>
    private sealed record TagInfo(string Name, string Entry);

    /// <summary>A group class an entry is added to, as the generated partial that re-opens it.</summary>
    /// <param name="Fqn">The group class, fully qualified — the key the group's entries are collected under.</param>
    /// <param name="Namespace">Its namespace, or empty for the global namespace.</param>
    /// <param name="Headers">
    ///     The partial header of every type from the outermost enclosing one down to the group itself, each written
    ///     with the accessibility and <c>static</c> it was declared with (CS0262 / CS0261).
    /// </param>
    /// <param name="Member">The entry's name in the group.</param>
    /// <param name="IsPartial">Whether the group and every type enclosing it are declared partial.</param>
    private sealed record ChainGroup(
        string Fqn,
        string Namespace,
        EquatableArray<string> Headers,
        string Member,
        bool IsPartial);

    // Set when a component implements IFormControl<T> — drives the synthesized bound factory and the
    // exclusion of the bound-mode interface members from the controlled factory. ValueTypeFqn is the T
    // (the validator/after-bind fan key); the bound-member names are the fixed interface member names.
    //
    // LiftsToNullable: T is a non-nullable value type (`bool`, `int`, `DateOnly`), so the control's chain also
    // opens on `Bind(Expression<Func<T?>>)` — a form model's `bool?` binds as readily as a `bool`.
    // CollectionElementFqn: the control binds an ICollection<TItem>, so its openings also take the shapes a
    // model actually declares — List<T>, T[], HashSet<T>. Without them `Bind(() => model.Tags)` over a
    // `List<string>` binds the SINGLE-valued control of the same entry instead: the exact type wins the
    // overload, and nothing reports it.
    private readonly record struct FormControlInfo(
        string ValueTypeFqn, bool LiftsToNullable = false, string? CollectionElementFqn = null);

    private readonly record struct GenericFactoryConfig(
        string TypeParameter,
        string ModelProperty,
        EquatableArray<string> TypedDelegateProperties,
        string Constraint);

    private readonly record struct PropInfo(
        string Name,
        string TypeFqn,
        bool IsNullable,
        bool HasInitializer,
        bool UserMarkedRequired,
        int InheritanceDepth,
        string DeclaringFilePath,
        int DeclaringSpanStart,
        int DeclaringSpanLength,
        bool IsAutoRerenderDelegate,
        bool IsTypeParameter,
        bool IsBoundInterfaceProp,
        bool IsDelegate,
        string? InitializerDefault,
        bool IsInitOnly,
        bool IsSharedSurfaceProp,
        bool HasDerivedSetter,
        // The property's own <summary>, carried onto the step or setter that sets it — see EmitDocComment.
        // Empty when the property has none, which is most of them today.
        string Summary = "",
        // Whether the devtools must never show this value: a password, a token, a personal detail. Decided HERE,
        // where the symbol and its attributes are, so the description the build writes cannot carry the value at
        // all — a panel that redacted on the way out would still have put it on the wire.
        bool IsSensitive = false,
        // For an enum-typed property: its type's fully-qualified name and its members, so each can be
        // offered as a step of its own — `UiButton.Primary` rather than `UiButton.Tone(UiTone.Primary)`.
        // ONE string rather than a list: PropInfo is an incremental generator's cache key, and an array
        // compares by reference, so a list here would defeat the cache on every compilation.
        //
        // Empty when the property is not an enum, when the enum is large enough that its members are
        // names rather than styles (UiIconName has 78), or when the step would collide — see EnumSteps.
        string EnumType = "",
        string EnumMembers = "")
    {
        // The factory-parameter / property identifier, '@'-escaped when Name is a reserved keyword.
        public string Escaped => EscapeIdentifier(Name);
    }
}
