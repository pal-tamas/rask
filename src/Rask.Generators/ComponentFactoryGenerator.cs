using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class ComponentFactoryGenerator : IIncrementalGenerator
{
    private const string ComponentFullName = "Rask.Core.Component";
    private const string RaskMarkupFullName = "Rask.Core.RaskMarkup";
    private const string RaskMarkupAttributeFullName = "Rask.Core.RaskMarkupAttribute";
    private const string ElementFullName = "Rask.Core.Element";
    private const string SkipFactoryFullName = "Rask.Core.SkipFactoryAttribute";
    private const string FactoryGenericFullName = "Rask.Core.FactoryGenericAttribute";
    private const string GenerateForwarderFactoryFullName = "Rask.Core.GenerateForwarderFactoryAttribute";
    private const string FormControlOpenFullName = "Rask.Core.Forms.IFormControl<T>";
    private const string ContextFullName = "global::Rask.Core.Live.LiveRenderContext";

    // The IFormControl<T> members that belong to BOUND mode: excluded from the synthesized controlled
    // factory, and (for Bind/AfterBind/AfterBindAsync) emitted as params on the synthesized bound factory.
    // Validate/ValidateAsync are not direct params — they drive the none/sync/async validator fan-out.
    private static readonly string[] BoundInterfaceMembers =
        { "Bind", "Validate", "ValidateAsync", "AfterBind", "AfterBindAsync" };

    // The mirror set: the members that belong to CONTROLLED mode, excluded from the synthesized bound
    // factory. In bound mode the model owns the value and the framework owns the write-back handler, so a
    // control reads none of these — accepting them next to Bind would take a value and silently drop it.
    //
    // Recognised by name, like every other form-control member (see Rask.Core.Forms.IFormControl<T>).
    // Value/OnChange/OnChangeAsync are the interface's own controlled members. OnInput/OnInputAsync and
    // Checked are not on the interface — a control declares them itself (Input, Textarea) — but they mean
    // the same thing wherever they appear on an IFormControl<T>: the per-keystroke DOM handler that bound
    // mode replaces with its write-back, and the checkbox's value, which bound mode derives from the model.
    private static readonly string[] ControlledMembers =
        { "Value", "Checked", "OnChange", "OnChangeAsync", "OnInput", "OnInputAsync" };

    private static readonly DiagnosticDescriptor Rask001 = new(
        "RASK001",
        "Property is treated as a required factory parameter",
        "Property '{0}.{1}' is treated as a required factory parameter; consider also marking it 'required' for language-level enforcement",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Hidden,
        true,
        description: "The generated factory emits a non-nullable property with no initializer as a REQUIRED parameter, "
                     + "so callers must pass it. Marking the property 'required' gets you the same guarantee from the "
                     + "language, at the declaration, instead of only from the generated signature. Declare it nullable "
                     + "instead if the value really is optional.",
        helpLinkUri: DiagnosticHelp.Link("RASK001"));

    private static readonly DiagnosticDescriptor Rask002 = new(
        "RASK002",
        "'required' property cannot be honored by the generated factory",
        "Property '{0}.{1}' is marked 'required', but the generated factory for '{0}' cannot set it: '{0}' has a dependency-injected constructor and the property is either excluded from the factory parameters (it has a member initializer) or only reachable via ActivatorUtilities.CreateInstance (no parameterless constructor). Adding a parameterless constructor does not help while the DI constructor remains — the factory then builds '{0}' with 'new {0}()' and the DI constructor never runs, leaving injected services null. Remove 'required', move the value to a constructor parameter (with no initializer), or drop the DI constructor.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "Fires in exactly one shape: the component has both a DI constructor AND a parameterless one, and "
                     + "the required property carries a member initializer. The factory then builds it with 'new C() { … "
                     + "}', but an initializer-carrying property is excluded from the factory parameters — so nothing "
                     + "assigns it and the consumer's build fails with CS9035. A DI constructor with no parameterless "
                     + "sibling is fine and does not trip this.",
        helpLinkUri: DiagnosticHelp.Link("RASK002"));

    private static readonly DiagnosticDescriptor Rask036 = new(
        "RASK036",
        "A builder-entry host must be partial",
        "'{0}' is not declared 'partial', so {1} cannot be injected into it; writing one of their names unqualified inside it will not compile. Add the 'partial' modifier.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A generator cannot add members to a type in a referenced assembly, so the entry for each "
                     + "component that is not Rask.Core's is injected into every type that might name one — every "
                     + "component of yours, and every markup host (a test class, a fixture, a factory of demo "
                     + "components, marked by deriving from 'RaskMarkup' or by the '[RaskMarkup]' attribute). That "
                     + "needs somewhere to inject it, and only a 'partial' class has one. For a host that DERIVES "
                     + "from 'RaskMarkup', Rask.Core's own entries are unaffected — those are inherited and need "
                     + "nothing injected — so all that is lost is naming a non-framework component unqualified. An "
                     + "'[RaskMarkup]' host has no such fallback: the generated partial is where its base or its "
                     + "framework entries would have come from, so without 'partial' it gets no surface at all.",
        helpLinkUri: DiagnosticHelp.Link("RASK036"));

    private static readonly DiagnosticDescriptor Rask040 = new(
        "RASK040",
        "Two components share a simple name, so neither can have a builder entry",
        "Components '{1}' share the simple name '{0}', so neither receives a builder entry: an entry is a single member of 'Rask.Core.Component' (or of each consuming component) named after its type, and one name can only stand for one type. Neither is reachable from a chain until you rename one of them.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "An entry is keyed by SIMPLE name — it is one member named after its type — so two components "
                     + "sharing a simple name can have at most one entry between them, and picking a winner would be "
                     + "the generator guessing which type the name means. Neither gets one until you rename. Their "
                     + "namespaces do not separate them here the way they separate the types themselves: a member "
                     + "name has no namespace.",
        helpLinkUri: DiagnosticHelp.Link("RASK040"));

    private static readonly DiagnosticDescriptor Rask041 = new(
        "RASK041",
        "The builder surface's shared pending-bit budget is exhausted",
        "The shared Element/Component surface has {0} folding properties but only {1} pending bits; '{2}' and every later one (ordinal name order) fall back to the eager reset, which reports the property changed on every render and defeats the render cache for it. Raise 'BuilderRuntime.OwnPendingBit' (and the generator's copy of it) together, or make the property non-folding.",
        DiagnosticHelp.Category,
        DiagnosticSeverity.Warning,
        true,
        description: "A folding setter clears its own PENDING bit as it writes, and whatever is still pending when "
                     + "the parent's Render() returns is reset to what the factory would have left. The shared "
                     + "Element/Component surface owns the low bits, up to BuilderRuntime.OwnPendingBit, so a "
                     + "component compiled against one Rask.Core cannot collide with a shared property added in "
                     + "a later one. The bits are handed "
                     + "out in ordinal NAME order, so overflowing the budget does not push the NEW property off the "
                     + "end — it pushes whichever alphabetically-later one was last onto the eager reset, which "
                     + "reports that property changed on every render and defeats the render cache for it. Nothing "
                     + "else fails, which is why this exists.",
        helpLinkUri: DiagnosticHelp.Link("RASK041"));

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax c && c.BaseList is { Types.Count: > 0 } &&
                                    !c.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                static (ctx, _) => GetCandidate(ctx))
            .Where(static c => c is not null)
            .Select(static (c, _) => c!);

        var grouped = candidates.Collect();

        // The two kinds of injection host that are NOT candidates — neither has an entry of its own, and
        // both need the surface injected into their own partial:
        //
        //  * an ABSTRACT component. Nothing can construct it, so it gets no factory and no entry, but it
        //    is still a component, and an abstract base that composes other components (BsBlock,
        //    BsFormControl<T>, PollingPanel) could otherwise name no entry at all.
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

        // RASK001 / RASK002 are reported here rather than beside an emission, because what they are
        // about — which properties a chain MUST name — is a property of the component, not of anything
        // generated from it.
        context.RegisterSourceOutput(grouped, static (spc, c) => ReportPropertyDiagnostics(spc, c));

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
        // Rask.Html declares ~155 tags, and injecting every one of them into every other one is O(n²)
        // generated members whose names collide with the props those hosts inherit from Element
        // (`Style`, `Data`, `Title`, `Cite`, …) — CS0108 with nothing able to hide it, because the entries
        // land ABOVE Element rather than below it the way Rask.Core's do on RaskMarkup.
        //
        // There is no switch for the surface itself. There used to be, back when a generated factory was
        // the other way to write markup; turning the chain off now would leave a project with no way to
        // build a component at all.
        var builderEnabled = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            new BuilderOptions(
                !p.GlobalOptions.TryGetValue("build_property.RaskBuilderEntryInjection", out var inject)
                || !string.Equals(inject, "false", StringComparison.OrdinalIgnoreCase)));

        var componentHost = context.CompilationProvider.Select(static (c, _) => GetComponentHost(c));

        context.RegisterSourceOutput(grouped.Combine(componentHost),
            static (spc, t) => EmitBuilderEntries(spc, t.Left, t.Right));

        // Components in REFERENCED assemblies (Rask.Bootstrap's Bs*, any third-party component library)
        // are in neither of the two paths above: they are not Rask.Core's, so they cannot ride on
        // Component, and they are not in this compilation's syntax, so they are not consumer candidates.
        // Each assembly publishes its own entries as a public `RaskEntries{Assembly}` class, which is what
        // this finds. CompilationProvider yields a fresh Compilation per keystroke, so the result is
        // wrapped in an EquatableArray and the emission only re-runs when the entry SET changes.
        var externalEntries = context.CompilationProvider.Select(static (c, _) => ScanExternalEntries(c));

        context.RegisterSourceOutput(
            grouped.Combine(builderEnabled).Combine(componentHost).Combine(externalEntries)
                .Combine(extraHosts),
            static (spc, t) =>
                EmitConsumerEntries(spc, t.Left.Left.Left.Left, t.Left.Left.Left.Right.InjectEntries,
                    t.Left.Left.Right, t.Left.Right, t.Right));

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

    // The universal surface (Component.Key plus Element's attributes and its ~88 GlobalEventHandlers)
    // is emitted ONCE as constrained generic extensions, instead of being re-emitted per tag the way
    // the factory's parameter list is. Only the assembly declaring Element contributes them.
    private static SetterHost GetSetterHost(Compilation compilation)
    {
        var assembly = SanitizeIdentifier(compilation.AssemblyName ?? "Rask");
        var element = compilation.Assembly.GetTypeByMetadataName(ElementFullName);
        if (element is null)
        {
            return new SetterHost(assembly, string.Empty,
                new EquatableArray<SharedSetter>(Array.Empty<SharedSetter>()));
        }

        var elementFqn = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var shared = new List<SharedSetter>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var t = element; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            var owner = t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            foreach (var member in t.GetMembers())
            {
                if (member is not IPropertySymbol p || p.IsStatic || p.IsIndexer || p.IsImplicitlyDeclared)
                {
                    continue;
                }

                if (p.SetMethod is null || p.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (p.Name == "Children" || !seen.Add(p.Name))
                {
                    continue;
                }

                var isDelegate = p.Type.TypeKind == TypeKind.Delegate;
                var (defaultLiteral, isRequired) = DefaultLiteralFor(p, compilation);
                shared.Add(new SharedSetter(
                    p.Name,
                    p.Type.ToDisplayString(FullyQualifiedNullable),
                    owner,
                    isDelegate,
                    // Same rule as ResetLiteralFor: a required prop has no default on either surface, so
                    // the reset writes the constructed state and `!` is what lets a non-nullable
                    // reference prop take it under warnings-as-errors.
                    isRequired ? defaultLiteral + "!" : defaultLiteral,
                    string.Equals(owner, elementFqn, StringComparison.Ordinal),
                    isRequired,
                    HasDerivedSetter(p),
                    SummaryOf(p)));
            }
        }

        shared.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return new SetterHost(assembly, elementFqn, new EquatableArray<SharedSetter>(shared.ToArray()));
    }

    private static void EmitBuilderSetters(
        SourceProductionContext spc,
        ImmutableArray<Candidate> candidates,
        bool emitSharedSurface,
        SetterHost host)
    {
        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);
        sb.AppendLine();
        sb.AppendLine("/// <summary>Builder-surface setters. Global namespace so no `using` is needed.</summary>");
        sb.Append("public static class RaskBuilderSetters").AppendLine(host.AssemblyName);
        sb.AppendLine("{");

        // The universal surface — Component.Key plus Element's attributes and its ~88 GlobalEventHandlers —
        // as constrained generic extensions over Build<T>. Being generic they already cover every component
        // in the graph, so an assembly that is only a component LIBRARY re-emits an identical set into the
        // same global namespace and makes `.Key(id)` ambiguous to infer (CS0411). Rask.Html is that shape,
        // and opts out through the same switch that stops it injecting its own entries.
        var sharedBits = SharedPendingBits(host);
        if (emitSharedSurface)
        {
            ReportSharedBitOverflow(spc, host, sharedBits);
            foreach (var s in host.Shared)
            {
                // No reachability skip, and none needed anywhere any more: the setter's receiver is the
                // CHAIN, so a delegate-typed property is not on it and cannot swallow its own setter.
                //
                // Twice, because there are two chain shapes and the shared surface belongs to both: an
                // ordinary component's `Build<T>` and a form control's mode-carrying `Build<T, TMode>`. The
                // second is written over an OPEN TMode, so `Input.Bind(…).Class("x")` keeps the mode it was
                // in and the next step still knows it. A form control that could not say `.Class(…)` would
                // be no trade at all.
                foreach (var mode in new string?[] { null, OpenMode })
                {
                    EmitSetter(sb, s.Name, s.TypeFqn, s.Owner, s.IsDelegate, wrap: false, generic: true,
                        fold: FoldsIntoPropsChanged(s.Name, s.TypeFqn, s.IsDelegate, autoRerender: false),
                        pendingBit: Bit(sharedBits, s.Name), summary: s.Summary, mode: mode);
                }
            }
        }

        // One Candidate per component TYPE. A partial class whose declarations each carry a base list
        // (`partial class Foo : Component` in one file, `partial class Foo : IMarker` in another) reaches
        // the syntax provider twice, and emitting its setters twice is CS0111.
        foreach (var c in DistinctByType(candidates))
        {
            // FullyQualifiedName already carries the type arguments (`Input<T>`); appending
            // TypeParameters again would emit `Input<T><T>`.
            var self = c.FullyQualifiedName;
            var visibility = c.IsPublic ? "public" : "internal";
            var ownBits = OwnPendingBits(c);
            // OwnSetterProps is everything the component does not inherit from Rask.Core's
            // Element/Component chain — its own props AND those it inherits from an intermediate base
            // (HtmlMediaElement, BsBlock, BsFormControl<T>, a consumer's own base). The shared chain is
            // emitted once as constrained generic extensions above and must not be duplicated per tag;
            // an intermediate base has no such emission, so skipping it left those props with no setter
            // at all (every Bs control's Id/Class/Label/Size, every media element's Src). The receiver
            // stays the CONCRETE component so the chain keeps its type — a `BsFormControl<T>`-typed
            // extension would return the base and break the next setter. An init-only prop can only be
            // assigned in an object initializer (CS8852), so it has no setter — the factory reaches it
            // through the initializer instead. The bound IFormControl<T> members are emitted below from
            // the interface's own types, not from wherever the control happens to declare them —
            // emitting both would be CS0111. A type-parameter prop (`T? Value`) is fine here even
            // though it needs `default` rather than `null` as a factory default — a setter has no
            // default to write.
            foreach (var p in OwnSetterProps(c))
            {
                // A CHAIN STEP is not also a setter, and that single rule carries three guarantees.
                //
                // `Bind` and `Value` are both steps, so choosing one leaves the other unreachable: a
                // bound control cannot also be given a Value, which used to compile and quietly meant
                // two sources of truth. A required property is a step, so it cannot be omitted — the
                // component does not exist until it is supplied, which is a stronger statement than
                // RASK038 reporting it afterwards. And a property that pins a type argument is a step,
                // because there is no component to hang a setter on until it has been.
                if (IsExclusiveOpening(c, p.Name))
                {
                    continue;
                }

                var folded = IsFoldedCallback(c, p.Name);
                EmitSetter(sb, p.Name, p.TypeFqn, self, p.IsDelegate,
                    p.IsAutoRerenderDelegate || folded, generic: false,
                    !folded && FoldsIntoPropsChanged(p.Name, p.TypeFqn, p.IsDelegate, p.IsAutoRerenderDelegate),
                    c.TypeParameters, c.TypeParameterConstraints, visibility, Bit(ownBits, p.Name),
                    p.Summary,
                    // A form control's chain carries its mode, so its steps are written over it: the
                    // controlled-mode props (Checked, OnInput, OnChange) only on Controlled, everything
                    // else — the display and constraint props — over an open TMode.
                    c.FormControl is null ? null : ModeOf(p.Name));
            }

            EmitBoundSetters(sb, c, visibility);
        }

        EmitCandidateResets(sb, candidates);

        sb.AppendLine("}");
        spc.AddSource("RaskBuilderSetters.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

        if (host.Shared.Count != 0)
        {
            EmitSharedResets(spc, host, sharedBits);
        }
    }

    // ---- Reset emission ------------------------------------------------------------------------
    //
    // A generated factory assigns EVERY parameter each render, so a prop the caller omitted is put back
    // to its default. A setter chain writes only what it names and the entry hands back the SAME
    // instance, so without a reset `Div.Id("x")` on one render and `Div` on the next still renders
    // id="x". These emissions are what give an entry-built component the factory's end-of-render state.
    //
    // Two halves, because the propsChanged fold has to keep meaning what it meant:
    //
    //  * EAGER — the non-folding props (delegates, Key). Assigned unconditionally when the
    //    entry is created, exactly as the factory assigns them. They never call Track, so defaulting
    //    them early cannot disturb anything.
    //  * PENDING — the folding props. Defaulting one before its setter runs would make Track compare the
    //    new value against the DEFAULT instead of against last render's value, so every constant prop
    //    would report a change every frame. Instead the entry marks them pending, each setter clears its
    //    own bit, and whatever is still pending when the parent's Render() returns is reset then — with
    //    the previous value still in place, so the fold is exactly the factory's.
    //
    // Bit numbering is split so a component compiled against one Rask.Core cannot collide with a shared
    // prop added in a later one: the shared Element/Component surface owns bits below OwnPendingBit
    // (emitted by the assembly that declares Element), each component's own props the bits above it. A
    // prop that does not fit falls back to the eager half — correct, just conservative in the fold.
    private const int OwnPendingBit = 32; // mirrors Rask.Core.BuilderRuntime.OwnPendingBit

    private static int Bit(Dictionary<string, int> bits, string name) =>
        bits.TryGetValue(name, out var bit) ? bit : -1;

    private static Dictionary<string, int> SharedPendingBits(SetterHost host)
    {
        var bits = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;
        foreach (var s in host.Shared)
        {
            if (next < OwnPendingBit
                && FoldsIntoPropsChanged(s.Name, s.TypeFqn, s.IsDelegate, autoRerender: false))
            {
                bits[s.Name] = next++;
            }
        }

        return bits;
    }

    // The budget is fixed (16) and handed out in ORDINAL NAME ORDER, so adding one folding prop to
    // Element does not push ITSELF off the end — it pushes whichever alphabetically-later prop was
    // last (Title, TabIndex). That one silently moves to the eager reset, which reports the prop
    // changed on every render and defeats the render cache for it: no compile error, no test failure,
    // just a slower framework. This is the signal.
    private static void ReportSharedBitOverflow(
        SourceProductionContext spc, SetterHost host, Dictionary<string, int> bits)
    {
        var folding = host.Shared
            .Where(s => FoldsIntoPropsChanged(s.Name, s.TypeFqn, s.IsDelegate, autoRerender: false))
            .ToList();
        if (folding.Count <= OwnPendingBit)
        {
            return;
        }

        var first = folding.First(s => !bits.ContainsKey(s.Name));
        spc.ReportDiagnostic(Diagnostic.Create(Rask041, Location.None,
            folding.Count.ToString(CultureInfo.InvariantCulture),
            OwnPendingBit.ToString(CultureInfo.InvariantCulture),
            first.Name));
    }

    // The props a builder setter can write on the component ITSELF — the same filter the setter loop
    // uses, so the bit a setter clears is the bit the reset tests.
    //
    // IsParamProperty is part of that filter, and it is the half that is easy to lose: a prop with a
    // NON-constant initializer (`= new()`) is excluded from the factory's parameters entirely, so the
    // factory can neither set it nor put it back. Giving it a setter anyway would let the builder write
    // a prop the factory cannot — and, because the reset is keyed off the same rule, write it once and
    // have it survive every later render. The mirror of the staleness bug the deferred reset exists to
    // prevent, and the reason the two questions must be asked with one predicate.
    private static IEnumerable<PropInfo> OwnSetterProps(Candidate c) =>
        c.Properties.Where(static p =>
            !p.IsSharedSurfaceProp && !p.IsInitOnly && p.Name != "Children" && !p.IsBoundInterfaceProp
            && IsParamProperty(p));

    // What the reset may put back: every prop the factory re-applies each render. A prop with a
    // non-constant initializer (`= new List<>()`) is not a factory parameter at all, so the factory can
    // neither set it nor put it back and neither may the builder.
    //
    // A REQUIRED prop is in here, and used not to be. The factory re-applies it from a required
    // ARGUMENT, so it is never stale there; a chain that stops naming it has nothing to re-apply, and
    // because the entry hands back the same instance, `BsIcon.Name(Star)` on one render and a bare
    // `BsIcon` on the next still rendered the star. That staleness is a SEPARATE half of the problem
    // from the missing setter RASK038 reports — the analyzer says the value is absent, this says the
    // OLD one must not survive — and withholding the entry was what covered both at once.
    private static bool IsResettableProp(PropInfo p) => IsParamProperty(p);

    // The literal that reset writes. An optional prop goes back to the default its factory parameter
    // carries; a required one has no default on either surface, so the entry writes `default!` — the
    // state the component would have been constructed in. The `!` is what lets a non-nullable reference
    // prop take it without CS8600 under warnings-as-errors, and it is a no-op on a value type.
    private static string ResetLiteralFor(PropInfo p) =>
        IsRequiredFactoryParam(p) ? "default!" : DefaultLiteralFor(p);

    private static Dictionary<string, int> OwnPendingBits(Candidate c)
    {
        var bits = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = OwnPendingBit;
        foreach (var p in OwnSetterProps(c))
        {
            if (next >= 64 || !IsResettableProp(p) || IsFoldedCallback(c, p.Name)
                           || !FoldsIntoPropsChanged(p.Name, p.TypeFqn, p.IsDelegate, p.IsAutoRerenderDelegate))
            {
                continue;
            }

            bits[p.Name] = next++;
        }

        return bits;
    }

    // The shared Element/Component surface, emitted once by the assembly that declares Element and
    // called by every component's reset — including a consumer's, which reaches it through the fixed
    // Rask.Core.BuilderRuntime name rather than the per-assembly setter class it cannot know.
    private static void EmitSharedResets(SourceProductionContext spc, SetterHost host, Dictionary<string, int> bits)
    {
        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core;");
        sb.AppendLine();
        sb.AppendLine("public static partial class BuilderRuntime");
        sb.AppendLine("{");

        foreach (var elementOwned in new[] { false, true })
        {
            var kind = elementOwned ? "Element" : "Component";
            var receiver = elementOwned ? host.ElementFqn : "global::Rask.Core.Component";
            // Required props are reset here too, on the same rule as a component's own (IsResettableProp):
            // the factory re-applies one from a required ARGUMENT every render, and a chain that stops
            // naming it has nothing to re-apply, so leaving it alone is what makes a prop stale. There are
            // none on Element/Component today — one would have blocked every tag from having an entry back
            // when a required prop did that — which is exactly why the two halves must not drift.
            var props = host.Shared.Where(s => s.IsElementOwned == elementOwned).ToList();
            var pending = props.Where(s => bits.ContainsKey(s.Name)).ToList();

            sb.Append("    /// <summary>Puts <c>").Append(kind)
                .AppendLine("</c>'s non-folding props back where the factory would leave them.</summary>");
            sb.Append("    public static void Reset").Append(kind)
                .AppendLine("Eager(global::Rask.Core.Component __c0)");
            sb.AppendLine("    {");
            if (elementOwned)
            {
                sb.AppendLine("        ResetComponentEager(__c0);");
            }

            EmitEagerResetBody(
                sb,
                receiver,
                props.Where(s => !bits.ContainsKey(s.Name))
                    .Select(s => (s.Name, s.DefaultLiteral, s.IsDelegate)).ToList(),
                "        ");

            sb.AppendLine("    }");
            sb.AppendLine();

            sb.Append("    /// <summary>Resets whichever of <c>").Append(kind)
                .AppendLine("</c>'s folding props the chain never named.</summary>");
            sb.Append("    public static void Reset").Append(kind)
                .AppendLine("Pending(global::Rask.Core.Component __c0, ulong __p)");
            sb.AppendLine("    {");
            if (elementOwned)
            {
                sb.AppendLine("        ResetComponentPending(__c0, __p);");
            }

            if (pending.Count != 0)
            {
                EmitReceiverCast(sb, receiver, "        ");
                foreach (var s in pending)
                {
                    EmitPendingReset(sb, s.Name, s.TypeFqn, s.DefaultLiteral, bits[s.Name], "        ", s.HasDerivedSetter);
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine();

            sb.Append("    /// <summary>Every folding bit <c>").Append(kind).AppendLine("</c> owns.</summary>");
            sb.Append("    public const ulong Shared").Append(kind).Append("Pending = ")
                .Append(MaskLiteral(pending.Select(s => bits[s.Name]))).AppendLine(";");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        spc.AddSource("RaskBuilderReset.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>
    ///     The eager reset's body, with the callback writes moved behind a "is there anything to clear"
    ///     check.
    /// </summary>
    /// <remarks>
    ///     Element carries ~88 callback props. Written unconditionally, that is ~88 stores on every
    ///     entry-built element on every render, and for the overwhelming majority of elements every one
    ///     of them assigns null over null — the element never named a callback. The flag is set by the
    ///     callback setters (see <c>EmitSetter</c>) and read-and-cleared here, so the block runs on
    ///     exactly the renders that have something to undo.
    ///     <para>
    ///         The non-callback props stay unconditional. They are few (Key is the shared one), and
    ///         gating them behind the same flag would make a Key-only chain pay for the callback block.
    ///     </para>
    /// </remarks>
    private static void EmitEagerResetBody(
        StringBuilder sb,
        string receiver,
        IReadOnlyList<(string Name, string DefaultLiteral, bool IsDelegate)> props,
        string indent)
    {
        var plain = props.Where(static p => !p.IsDelegate).ToList();
        var callbacks = props.Where(static p => p.IsDelegate).ToList();
        var cast = false;

        if (plain.Count != 0)
        {
            EmitReceiverCast(sb, receiver, indent);
            cast = true;
            foreach (var p in plain)
            {
                sb.Append(indent).Append("__c.").Append(EscapeIdentifier(p.Name)).Append(" = ")
                    .Append(p.DefaultLiteral).AppendLine(";");
            }
        }

        if (callbacks.Count == 0)
        {
            return;
        }

        sb.Append(indent).AppendLine("if (!global::Rask.Core.BuilderRuntime.HasCallbacks(__c0))");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    return;");
        sb.Append(indent).AppendLine("}");
        sb.AppendLine();

        if (!cast)
        {
            EmitReceiverCast(sb, receiver, indent);
        }

        foreach (var p in callbacks)
        {
            sb.Append(indent).Append("__c.").Append(EscapeIdentifier(p.Name)).Append(" = ")
                .Append(p.DefaultLiteral).AppendLine(";");
        }
    }

    private static void EmitReceiverCast(StringBuilder sb, string receiver, string indent)
    {
        if (string.Equals(receiver, "global::Rask.Core.Component", StringComparison.Ordinal))
        {
            sb.Append(indent).AppendLine("var __c = __c0;");
            return;
        }

        sb.Append(indent).Append("var __c = (").Append(receiver).AppendLine(")__c0;");
    }

    // One folding prop's deferred reset. The equality test comes first so an untouched prop costs a bit
    // test and a comparison rather than a write — Element's Ref/Role/TabIndex/Aria setters would
    // otherwise force a LiveState allocation onto every element that never used them.
    private static void EmitPendingReset(
        StringBuilder sb,
        string name,
        string typeFqn,
        string defaultLiteral,
        int bit,
        string indent,
        bool derivedSetter)
    {
        var escaped = EscapeIdentifier(name);

        // A prop whose setter DERIVES state has to be assigned even when it already reads as its default,
        // because for that prop "reads as the default" is not the same statement as "the setter has run".
        // Router.Routes is the case that proves it: assigning null resolves RouteRegistry.BuildTree() and
        // flattens the route leaves, so the factory's `Routes: null` on every render is what builds the
        // routing table at all. Skipping the write because Routes is already null leaves the leaves empty,
        // nothing matches, and the whole page renders as nothing — with no diagnostic, because a nullable
        // prop is not a required one and RASK038 has no claim on it.
        //
        // The fold still has to mean what it meant, so the comparison moves to the other side of the
        // assignment: what changed is `before` vs `after`, not `before` vs the literal. For an ordinary
        // auto-property those two are the same question, which is why the cheaper form below stays the
        // default — this one costs a redundant write per unnamed prop per render, and the shared
        // Element/Component surface is ~90 of them on every element in the tree.
        if (derivedSetter)
        {
            sb.Append(indent).Append("if ((__p & ").Append(MaskLiteral(new[] { bit })).AppendLine(") != 0UL)");
            sb.Append(indent).AppendLine("{");
            sb.Append(indent).Append("    var __was = __c.").Append(escaped).AppendLine(";");
            sb.Append(indent).Append("    __c.").Append(escaped).Append(" = ").Append(defaultLiteral).AppendLine(";");
            sb.Append(indent).Append("    if (!global::System.Collections.Generic.EqualityComparer<")
                .Append(typeFqn).Append(">.Default.Equals(__was, __c.").Append(escaped).AppendLine("))");
            sb.Append(indent).AppendLine("    {");
            sb.Append(indent).AppendLine("        global::Rask.Core.BuilderRuntime.MarkChanged(__c);");
            sb.Append(indent).AppendLine("    }");
            sb.Append(indent).AppendLine("}");
            return;
        }

        sb.Append(indent).Append("if ((__p & ").Append(MaskLiteral(new[] { bit }))
            .Append(") != 0UL && !global::System.Collections.Generic.EqualityComparer<").Append(typeFqn)
            .Append(">.Default.Equals(__c.").Append(escaped).Append(", ").Append(defaultLiteral)
            .AppendLine("))");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    global::Rask.Core.BuilderRuntime.MarkChanged(__c);");
        sb.Append(indent).Append("    __c.").Append(escaped).Append(" = ")
            .Append(defaultLiteral).AppendLine(";");
        sb.Append(indent).AppendLine("}");
    }

    /// <summary>
    ///     Whether <paramref name="p" />'s <c>set</c> accessor has a body — i.e. it derives state rather
    ///     than storing what it was handed. <c>Router.Routes</c> turns a <c>null</c> into
    ///     <c>RouteRegistry.BuildTree()</c>; <c>Form.Model</c> and <c>Form.Context</c> register with the
    ///     ambient <c>EditContext</c> and walk the model graph.
    /// </summary>
    /// <remarks>
    ///     Answered from syntax, which is always available where it is asked: an assembly emits the resets
    ///     for the components it DECLARES. A prop inherited from an intermediate base in a REFERENCED
    ///     assembly has no syntax here and reads as <c>false</c> — the same blind spot a member
    ///     initializer has (see <c>PublishedRequiredProperties</c>), and unreached by anything in this
    ///     repo, since the bases that cross an assembly boundary (<c>BsBlock</c>,
    ///     <c>BsFormControl&lt;T&gt;</c>) are auto-properties throughout.
    /// </remarks>
    private static bool HasDerivedSetter(IPropertySymbol p)
    {
        if (p.SetMethod is not { } setter)
        {
            return false;
        }

        foreach (var reference in setter.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is AccessorDeclarationSyntax accessor
                && (accessor.Body is not null || accessor.ExpressionBody is not null))
            {
                return true;
            }
        }

        return false;
    }

    private static string MaskLiteral(IEnumerable<int> bits)
    {
        var mask = 0UL;
        foreach (var bit in bits)
        {
            mask |= 1UL << bit;
        }

        return "0x" + mask.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + "UL";
    }

    // Per-component resets, emitted next to that component's setters. Skipped entirely when the
    // component adds nothing to the shared surface — 140 of the HTML tags are in that case, and their
    // entries hand BuilderRuntime's shared routines to Entry<T> directly.
    private static void EmitCandidateResets(StringBuilder sb, ImmutableArray<Candidate> candidates)
    {
        foreach (var c in DistinctByType(candidates))
        {
            if (!NeedsOwnReset(c))
            {
                continue;
            }

            var visibility = c.IsPublic ? "public" : "internal";
            var bits = OwnPendingBits(c);
            var eager = OwnEagerResetProps(c).ToList();
            var pending = OwnSetterProps(c).Where(p => bits.ContainsKey(p.Name)).ToList();

            sb.Append("    ").Append(visibility).Append(" static void ").Append(EagerResetName(c))
                .Append(c.TypeParameters).Append("(global::Rask.Core.Component __c0)")
                .AppendLine(c.TypeParameterConstraints);
            sb.AppendLine("    {");
            sb.Append("        global::Rask.Core.BuilderRuntime.Reset").Append(c.IsElement ? "Element" : "Component")
                .AppendLine("Eager(__c0);");
            if (eager.Count != 0)
            {
                EmitReceiverCast(sb, c.FullyQualifiedName, "        ");
                foreach (var p in eager)
                {
                    sb.Append("        __c.").Append(p.Escaped).Append(" = ").Append(ResetLiteralFor(p))
                        .AppendLine(";");
                }
            }

            sb.AppendLine("    }");

            sb.Append("    ").Append(visibility).Append(" static void ").Append(PendingResetName(c))
                .Append(c.TypeParameters).Append("(global::Rask.Core.Component __c0, ulong __p)")
                .AppendLine(c.TypeParameterConstraints);
            sb.AppendLine("    {");
            sb.Append("        global::Rask.Core.BuilderRuntime.Reset").Append(c.IsElement ? "Element" : "Component")
                .AppendLine("Pending(__c0, __p);");
            if (pending.Count != 0)
            {
                EmitReceiverCast(sb, c.FullyQualifiedName, "        ");
                foreach (var p in pending)
                {
                    EmitPendingReset(sb, p.Name, p.TypeFqn, ResetLiteralFor(p), bits[p.Name], "        ", p.HasDerivedSetter);
                }
            }

            sb.AppendLine("    }");
        }
    }

    // The props the entry defaults on the spot: everything a setter can write that does NOT fold, plus
    // any folding prop that ran out of pending bits (reset early rather than not at all — the fold then
    // over-reports for that prop, which costs a cache miss instead of stale HTML). The bound
    // IFormControl<T> members are here at any depth: they never fold, and EntryBound re-assigns Bind
    // straight afterwards.
    private static IEnumerable<PropInfo> OwnEagerResetProps(Candidate c)
    {
        var bits = OwnPendingBits(c);
        return OwnSetterProps(c).Where(p => IsResettableProp(p) && !bits.ContainsKey(p.Name))
            .Concat(c.Properties.Where(static p => p.IsBoundInterfaceProp && !p.IsInitOnly)
                .Where(IsResettableProp));
    }

    private static bool NeedsOwnReset(Candidate c) => OwnEagerResetProps(c).Any() || OwnPendingBits(c).Count != 0;

    private static string EagerResetName(Candidate c) => "__RaskResetEager_" + ResetSuffix(c);

    private static string PendingResetName(Candidate c) => "__RaskResetPending_" + ResetSuffix(c);

    // Namespace-qualified, because a component's SIMPLE name is not unique. Factories live in a
    // per-namespace `Generated` class, so `Features.Products.Card` and `Features.Orders.Card` coexist
    // happily; the resets share one static class per assembly. Keyed by simple name, the second `Card`
    // is dropped and the survivor's `var __c = (Features.Products.Card)__c0;` is then handed the OTHER
    // type's instance — an InvalidCastException at render time, out of source that compiles clean.
    private static string ResetSuffix(Candidate c)
    {
        var name = c.FullyQualifiedName;
        var open = name.IndexOf('<');
        if (open >= 0)
        {
            name = name.Substring(0, open);
        }

        if (name.StartsWith("global::", StringComparison.Ordinal))
        {
            name = name.Substring("global::".Length);
        }

        // Arity, not the type-parameter NAMES: EmitBoundEntry renames a parameter that collides with an
        // enclosing type's (CS0693), and the reset it points at must keep the same name either way.
        var arity = c.TypeParameters.Length == 0
            ? string.Empty
            : "_" + (c.TypeParameters.Count(ch => ch == ',') + 1).ToString(CultureInfo.InvariantCulture);
        return SanitizeIdentifier(name) + arity;
    }

    // One Candidate per component type, ordered for a deterministic emission. The syntax provider
    // yields one candidate per class DECLARATION, so a partial class with a base list in two files
    // appears twice.
    private static List<Candidate> DistinctByType(ImmutableArray<Candidate> candidates)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Candidate>(candidates.Length);
        foreach (var c in candidates.OrderBy(static c => c.FullyQualifiedName, StringComparer.Ordinal))
        {
            if (seen.Add(c.FullyQualifiedName))
            {
                result.Add(c);
            }
        }

        return result;
    }

    // A setter is named after the property it writes. Always — including a DELEGATE property, which used
    // to be the one exception: an extension method could not share a delegate prop's name, because C#
    // resolves `x.OnClick(fn)` against the property and reads it as an invocation (CS1593). The rule
    // dropped a leading `On` to dodge that (`OnRate` -> `.Rate(…)`), and where it could not, the property
    // had no reachable setter at all and RASK042 asked the author to wrap the delegate in a carrier.
    //
    // Both are gone because the chain's receiver is `Build<TComponent>` rather than the component: the
    // property is no longer on the receiver, so the lookup never finds it and the setter binds whatever
    // the property's type. A callback property is now an ordinary `Action`/`Func`, and its setter says
    // what the property says.

    // The bound half of an IFormControl<T> control: one setter per interface member, typed from the
    // interface's T rather than from the declaring class. That matters twice — the members may be
    // inherited from a non-Element base (BsInput<T> gets them from BsFormControl<T>, which the
    // depth-0 rule above would skip), and it is what lets the generic entry take only `Bind`:
    //
    //     Input.Bind(() => _form.Name).Validate(ProductName.Validate).Id("name")
    //
    // replaces the factory's none/sync/async overload fan-out, whose only purpose was to make
    // Validate a required, correctly-typed parameter. Never auto-wrapped: a validator is not an event
    // callback, and AfterBind is a post-bind hook (the bound factory has always assigned them raw).
    private static void EmitBoundSetters(StringBuilder sb, Candidate c, string visibility)
    {
        if (c.FormControl is not { } fc)
        {
            return;
        }

        var t = fc.ValueTypeFqn;

        // Typed from the INTERFACE rather than from wherever the control happens to declare them, so a
        // control inheriting them from a non-Element base still gets setters typed on the interface's T.
        var members = new (string Name, string TypeFqn)[]
        {
            ("Bind", "global::System.Linq.Expressions.Expression<global::System.Func<" + t + ">>?"),
            ("Validate", "global::Rask.Core.Forms.Validate<" + t + ">?"),
            ("ValidateAsync", "global::Rask.Core.Forms.ValidateAsync<" + t + ">?"),
            ("AfterBind", "global::System.Action<" + t + ">?"),
            ("AfterBindAsync",
                "global::System.Func<" + t
                + ", global::System.Threading.Tasks.Task>?"),
        };

        foreach (var (name, typeFqn) in members)
        {
            // `Bind` is a chain STEP, not a setter — it is how a bound control gets its value type, and
            // leaving it here as well would put it back within reach of a chain that already chose
            // `Value`. That is the shape this rule exists to forbid: a control bound to an expression AND
            // handed a value has two sources of truth, and nothing decided which won.
            if (IsExclusiveOpening(c, name))
            {
                continue;
            }

            // The TYPE comes from the interface, but the documentation comes from the control's own
            // declaration — that is where a reader wrote it, and Input/Select/Textarea each document
            // Validate/AfterBind in their own words. Without this the members emitted here were the one
            // group on the whole chain with no tooltip, however well the source was documented, because
            // nothing carried a summary into this call at all. SummaryOf already falls back to
            // IFormControl<T>'s docs for a control that declares them without a comment.
            var summary = c.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
                .Summary ?? string.Empty;

            // fold: false — the bound members are a fresh expression tree / delegate every render, so
            // folding them would report propsChanged on every frame. Exactly what EmitBoundOverload's
            // foldProps does (only the shared display props participate there too).
            //
            // mode: these are the BOUND members, so they are declared only on a bound chain. A chain that
            // opened with `Value` never sees them — `.AfterBind(…)` on it is a compile error naming
            // Build<…, Controlled>, where before it compiled and the hook simply never ran.
            EmitSetter(sb, name, typeFqn, c.FullyQualifiedName, isDelegate: false, wrap: false, generic: false,
                fold: false, c.TypeParameters, c.TypeParameterConstraints, visibility, pendingBit: -1,
                summary: summary, mode: ModeOf(name));
        }
    }

    // Which props participate in the propsChanged diff, asked of a builder setter. Mirrors the
    // factory's foldProps exactly (EmitFactory / EmitBoundOverload) so both surfaces report the same
    // flag to NotifyParameters: Key is a reconciliation identity rather than a reactive prop;
    // auto-wrapped callbacks and raw delegates are a fresh closure every render, so folding them would
    // force propsChanged: true on every frame and defeat the render cache for any callback-taking
    // component.
    /// <summary>
    ///     Whether <paramref name="propName" /> is a property a <c>[FactoryGeneric]</c> component folds a
    ///     <i>typed</i> callback into — <c>Form.OnValidSubmit</c> and <c>OnInvalidSubmit</c>, whose generic
    ///     factory takes <c>Callback&lt;TModel&gt;</c>/<c>CallbackAsync&lt;TModel&gt;</c>, wraps whichever it
    ///     was handed in <c>AutoCallback</c>, and stores the result as a bare <c>Delegate?</c>.
    /// </summary>
    /// <remarks>
    ///     The builder setter is generated from the PROPERTY, so it sees only the folded
    ///     <c>Delegate?</c> and did neither half: no wrap, and — because a bare <c>Delegate</c> is not a
    ///     delegate-typed symbol — it folded into <c>propsChanged</c> as if it were an ordinary value. Both
    ///     halves are decided here so they cannot drift apart: a wrapped callback is a fresh closure on
    ///     every render, so folding one would report a prop change every frame and defeat the render cache
    ///     for the whole subtree. Same rule the auto-wrapped callbacks have always followed.
    /// </remarks>
    private static bool IsFoldedCallback(Candidate c, string propName) =>
        c.GenericFactory is { } gf && gf.TypedDelegateProperties.Contains(propName);

    // Opt-in wrapping for a callback an ELEMENT-derived component invokes itself rather than handing to
    // the DOM. Matched by name so Rask.Core's own attribute needs no symbol threaded through here.
    private static bool HasAutoCallbackAttribute(ISymbol prop) =>
        prop.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == "Rask.Core.AutoCallbackAttribute");

    private static bool FoldsIntoPropsChanged(string name, string typeFqn, bool isDelegate, bool autoRerender) =>
        !string.Equals(name, "Key", StringComparison.Ordinal)
        && !isDelegate
        && !autoRerender
        ;


    // One line of `///` above a generated member, so a chain's tooltip says what the property it writes
    // says. Skipped when the property has no summary — an empty doc comment is worse than none, because
    // it suppresses the fallback the IDE would otherwise show.
    private static void EmitDocComment(StringBuilder sb, string summary, string pad)
    {
        if (summary.Length == 0)
        {
            return;
        }

        sb.Append(pad).Append("/// <summary>").Append(summary).AppendLine("</summary>");
    }

    // The doc comment on a chain ENTRY — `Div`, `Span`, `BsButton`, the identifier that OPENS a markup
    // expression and so the first thing anyone types. It went undocumented while the factories and the
    // setters after it were fully covered, which is the worst place to have a blank tooltip: hovering
    // `Div` said nothing while hovering `.Class(…)` one keystroke later explained itself.
    //
    // The type's own summary, and a <seealso> back to it. No fallback text when a component has no
    // summary: an empty doc comment SUPPRESSES the tooltip an IDE would otherwise synthesise, so saying
    // nothing is better than saying nothing at length.
    private static void EmitEntryDoc(StringBuilder sb, Candidate c)
    {
        if (c.Summary.Length == 0)
        {
            return;
        }

        var cref = c.FullyQualifiedName.Replace('<', '{').Replace('>', '}');
        sb.Append("    /// <summary>").Append(c.Summary).AppendLine("</summary>");
        sb.Append("    /// <seealso cref=\"").Append(cref).AppendLine("\"/>");
    }

    // The opening lines every generated file shares. Centralised for the pragma, which is not optional
    // anywhere docs are emitted: a factory or a chain setter documents the properties that carry a summary
    // and leaves the rest bare, and CS1573 fires per UNDOCUMENTED parameter as soon as ANY parameter on
    // that member is documented. Partial documentation is the normal, permanent state here — an element
    // factory carries ~50 universal event props — so without this every consumer building with
    // warnings-as-errors fails on our generated code, which they cannot edit.
    //
    // Emitted for every file rather than only the ones that document something today, so that adding a
    // doc comment to a generator that currently emits none cannot reintroduce the break.
    private static void EmitGeneratedFileHeader(StringBuilder sb)
    {
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1573 // parameter has no matching param tag");
    }

    private static void EmitSetter(
        StringBuilder sb,
        string name,
        string typeFqn,
        string receiver,
        bool isDelegate,
        bool wrap,
        bool generic,
        bool fold,
        string typeParameters = "",
        string constraints = "",
        string visibility = "public",
        int pendingBit = -1,
        string summary = "",
        // null for an ordinary component's chain (`Build<T>`); for a form control, the mode argument its
        // chain carries — OpenMode when the step is legal in either mode, one of the two mode types when
        // it belongs to only one. See Rask.Core.Build{T,TMode}.
        string? mode = null)
    {
        EmitDocComment(sb, summary, "    ");
        var setterName = name;

        // Key is not an ordinary assignment: it decides WHICH instance the chain is building (#685).
        // Before this, a child's identity inside its parent was its ordinal among entry-built siblings
        // and Key took no part, so inserting an item at the top of a keyed list handed every later row
        // the next row's instance — the state-follows-position bug Key exists to prevent, one layer
        // below where Key was being consulted. ClaimKey hands back the instance this key owns, which is
        // why this step returns a NEW chain rather than the one it was given.
        //
        // It also makes Key an opening step in practice: any setter written before it would land on the
        // instance the key is about to discard. RASK046 reports that at the call site.
        if (generic && string.Equals(name, "Key", StringComparison.Ordinal))
        {
            // Mode-aware like every other shared step: the shared surface is emitted once per chain
            // SHAPE (`Build<T>` and a form control's `Build<T, TMode>`), so writing `Build<T>` literally
            // here emitted the same `Key<T>` twice — CS0111. A form control is as keyable as anything
            // else, and its chain has to keep its mode across the claim.
            var self = BuildOf("T", mode);
            sb.Append("    ").Append(visibility).Append(" static ")
                .Append(self).Append(" Key").Append(WithMode("<T>", mode)).Append("(this ").Append(self)
                .Append(" __b, ")
                .Append(typeFqn).Append(" value) where T : ").Append(receiver);
            sb.Append(" { var __c = global::Rask.Core.BuilderRuntime.ClaimKey(__b.Value, value); ");
            if (pendingBit >= 0)
            {
                sb.Append("global::Rask.Core.BuilderRuntime.Written(__c, ")
                    .Append(MaskLiteral(new[] { pendingBit })).Append("); ");
            }

            sb.Append("__c.Key = value; return new ").Append(self).AppendLine("(__c); }");
            return;
        }

        // `wrap` is the AutoCallback decision, and it is per property: an Element's
        // handlers go to the DOM unwrapped (owner resolution already re-renders, and wrapping would
        // allocate per render), a non-Element component's event callbacks are wrapped, and a form
        // control's bound members (validators, post-bind hooks) are never wrapped at all.
        var paramType = typeFqn;
        // Wrap returns a nullable delegate (null in → null out); assigning it to a non-nullable prop
        // needs the null-forgiving `!` (CS8601), the same way the factory's assignment pass does it.
        var value = wrap
            ? "global::Rask.Core.AutoCallback.Wrap(value)"
              + (typeFqn.EndsWith("?", StringComparison.Ordinal) ? string.Empty : "!")
            : "value";
        var assigned = value;

        // The propsChanged fold, one prop at a time. The factory can snapshot every prop, assign them
        // all and diff once, because it knows where the assignments end; a setter chain does not, so
        // each folding setter accumulates its own delta and the parent fires the single notification
        // when its Render() returns (Component.RenderForLive). Same EqualityComparer semantics, and the
        // non-folding props (Key, delegates) emit no call at all — see FoldsIntoPropsChanged.
        var track = fold
            ? "global::Rask.Core.BuilderRuntime.Track(__c, __c." + EscapeIdentifier(name) + ", value); "
            : string.Empty;

        // …and the other half: the chain NAMED this prop, so the deferred reset must leave it alone.
        // A no-op when the receiver came from a factory instead of an entry (both surfaces compile side
        // by side during the migration) — that component is fully re-assigned by its factory already.
        if (pendingBit >= 0)
        {
            track += "global::Rask.Core.BuilderRuntime.Written(__c, " + MaskLiteral(new[] { pendingBit }) + "); ";
        }

        // A callback prop is non-folding, so it carries no pending bit and the eager reset puts it back
        // unconditionally at the next entry. Record that there IS one to put back: an element that
        // names no callback — almost every element — then skips a block of ~88 delegate writes on
        // every render. See Component.FlagCallbackAssigned.
        if (isDelegate && !fold && pendingBit < 0)
        {
            track += "global::Rask.Core.BuilderRuntime.MarkCallbacks(__c); ";
        }

        // An `internal` component cannot appear in a `public` signature (CS0050/CS0051), so the
        // setter's accessibility tracks its component's — the same rule the factory emission uses.
        // The receiver is `Build<TComponent>`, never the component. That is the whole reason a callback
        // property can be an ORDINARY delegate: C# stops at a delegate-typed property when it resolves
        // `x.OnClick(fn)` and reads the call as an invocation (CS1593), never reaching an extension
        // method — but only if the property is on the receiver. One step off it, the lookup finds
        // nothing and the setter binds. See Rask.Core.Build{T}.
        sb.Append("    ").Append(visibility).Append(" static ");
        if (generic)
        {
            var self = BuildOf("T", mode);
            sb.Append(self).Append(' ').Append(EscapeIdentifier(setterName))
                .Append(WithMode("<T>", mode)).Append("(this ")
                .Append(self).Append(" __b, ").Append(paramType)
                .Append(" value) where T : ").Append(receiver);
            sb.Append(" { var __c = __b.Value; ").Append(track).Append("__c.").Append(EscapeIdentifier(name))
                .Append(" = ").Append(assigned).AppendLine("; return __b; }");
            EmitAttrBagOverloads(sb, setterName, name, typeFqn, receiver, fold, pendingBit, visibility,
                generic: true, mode);
            return;
        }

        var target = BuildOf(receiver, mode);
        sb.Append(target).Append(' ').Append(EscapeIdentifier(setterName))
            .Append(WithMode(typeParameters, mode))
            .Append("(this ").Append(target).Append(" __b, ").Append(paramType).Append(" value)")
            .Append(constraints);
        sb.Append(" { var __c = __b.Value; ").Append(track).Append("__c.").Append(EscapeIdentifier(name))
            .Append(" = ").Append(assigned).AppendLine("; return __b; }");
        EmitAttrBagOverloads(sb, setterName, name, typeFqn, receiver, fold, pendingBit, visibility,
            generic: false, mode, typeParameters, constraints);
    }

    // A property whose type IS an attribute bag — Data, Aria, FieldAria, and anything added later.
    // Keyed off the type rather than a list of names, so a new bag property gets the ergonomic steps
    // without anyone remembering to add it here.
    //
    // Whole-type equality, not a substring test: `Func<IReadOnlyDictionary<string, string?>, Component>`
    // is the gesture triggers' render callback, and it CONTAINS the bag's name. A substring test hands it
    // a `.Data(string, string?)` overload whose body cannot compile.
    private static bool IsAttrBag(string typeFqn)
    {
        var bare = typeFqn.EndsWith("?", StringComparison.Ordinal)
            ? typeFqn.Substring(0, typeFqn.Length - 1)
            : typeFqn;
        return string.Equals(
            bare, "global::System.Collections.Generic.IReadOnlyDictionary<string, string?>",
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Two extra steps beside a bag property's dictionary setter, so the shape real markup is full of
    ///     — one attribute — reads as <c>.Data("test-id", "primary")</c> rather than
    ///     <c>.Data(new Dictionary&lt;string, string?&gt; { ["test-id"] = "primary" })</c>.
    /// </summary>
    /// <remarks>
    ///     Both forward to <c>Rask.Core.AttrBag</c>, which the element writer knows by type: one pair costs
    ///     a single object rather than a Dictionary plus its bucket and entry arrays, and is written
    ///     without materialising an enumerator. <c>params ReadOnlySpan&lt;…&gt;</c> keeps a multi-pair call
    ///     site's argument list on the stack.
    /// </remarks>
    private static void EmitAttrBagOverloads(
        StringBuilder sb, string setterName, string propertyName, string typeFqn, string receiver,
        bool fold, int pendingBit, string visibility, bool generic, string? mode = null,
        string typeParameters = "", string constraints = "")
    {
        if (!IsAttrBag(typeFqn))
        {
            return;
        }

        var escaped = EscapeIdentifier(setterName);
        var prop = EscapeIdentifier(propertyName);

        // The same bookkeeping the dictionary setter does, but written against `__bag`: the caller's
        // `track` string names a local `value`, which is this overload's own parameter name.
        var track = fold
            ? "global::Rask.Core.BuilderRuntime.Track(__c, __c." + prop + ", __bag); "
            : string.Empty;
        if (pendingBit >= 0)
        {
            track += "global::Rask.Core.BuilderRuntime.Written(__c, " + MaskLiteral(new[] { pendingBit }) + "); ";
        }

        var self = BuildOf(generic ? "T" : receiver, mode);
        // The component's own type parameters are carried here too — a generic component's bag setter that
        // declared none would not compile. Non-generic components (every one that has a bag prop today)
        // are unaffected: the list is empty either way.
        var typeArgs = WithMode(generic ? "<T>" : typeParameters, mode);
        // …and with them their CONSTRAINTS, or a constrained generic component with a bag prop emits
        // `Foo<TValue>(this Build<Widget<TValue>, TMode> …)` with no `where TValue : …` and fails to
        // compile (CS0314). Carrying the parameters without the constraints was half a fix.
        var where = generic ? " where T : " + receiver : constraints;

        // The body is assigned here rather than forwarded to the dictionary overload. Every component's
        // setters are extension methods on Build<…> in one static class, so a forwarding `Data(__b, …)`
        // is resolved against ALL of them and binds to whichever component's overload wins — it picked
        // Build<FullscreenTrigger> for an EyeDropperTrigger. Assigning the property directly has no name
        // to resolve.
        // The prefix these entries render under, which is the whole point of the overload: `.Aria("label",
        // "Close")` is aria-label, not an attribute called "label". Naming it in the doc is what tells a
        // reader they must NOT write the prefix themselves.
        //
        // Listed, not derived. The prefix lives in Element.WriteAttributes as a literal, and nothing ties it
        // to the property's name — a bag property added later could render under anything. Lowercasing the
        // name would document that new property CONFIDENTLY and WRONGLY, which is worse than saying less, so
        // an unknown bag falls back to wording that makes no claim about the rendered name.
        var prefix = propertyName switch
        {
            "Data" => "data-",
            "Aria" => "aria-",
            _ => null,
        };

        var attr = prefix is null ? "attribute" : "<c>" + prefix + "</c> attribute";
        var named = prefix is null ? "<c>{name}</c>" : "<c>" + prefix + "{name}</c>";
        var without = prefix is null
            ? "The attribute name."
            : "The part after <c>" + prefix + "</c>. Do not include the prefix.";

        foreach (var (parameters, expression, doc) in new[]
                 {
                     // Name only — a BARE attribute (`data-rask-no-restore`), which is how the framework's
                     // own opt-out flags are written. A null value is what renders one, the same rule
                     // `disabled` follows; `""` would render `=""`, which is a different attribute.
                     ("string name", "new global::Rask.Core.AttrBag(name, null)",
                         "<summary>Adds one bare " + named + " " + attr + ", with no value — the way <c>disabled</c> "
                         + "is written. Pass <c>\"\"</c> as the value instead to render <c>=\"\"</c>, which is a different "
                         + "attribute.</summary>"
                         + "\n    /// <param name=\"name\">" + without + "</param>"),
                     ("string name, string? value", "new global::Rask.Core.AttrBag(name, value)",
                         "<summary>Sets one " + named + " " + attr + " to a value — the everyday shape, as in "
                         + "<c>." + setterName + "(\"…\", \"…\")</c>. The value is HTML-encoded; a <see langword=\"null\"/> value "
                         + "renders the attribute bare.</summary>"
                         + "\n    /// <param name=\"name\">" + without + "</param>"
                         + "\n    /// <param name=\"value\">The attribute value, or <see langword=\"null\"/> for a bare attribute.</param>"),
                     ("params global::System.ReadOnlySpan<(string Name, string? Value)> pairs",
                         "new global::Rask.Core.AttrBag(pairs)",
                         "<summary>Sets several " + attr + "s at once. Cheaper than passing a dictionary — the "
                         + "argument list stays on the stack and nothing is allocated per render.</summary>"
                         + "\n    /// <param name=\"pairs\">Name/value pairs. " + without + "</param>"),
                 })
        {
            sb.Append("    /// ").AppendLine(doc);
            sb.Append("    ").Append(visibility).Append(" static ").Append(self).Append(' ').Append(escaped)
                .Append(typeArgs).Append("(this ").Append(self).Append(" __b, ").Append(parameters).Append(')')
                .Append(where)
                .Append(" { var __c = __b.Value; var __bag = ").Append(expression).Append("; ").Append(track)
                .Append("__c.").Append(prop).AppendLine(" = __bag; return __b; }");
        }
    }

    // The chain's receiver and result for a component type — `Rask.Core.Build<TComponent>`, or
    // `Rask.Core.Build<TComponent, TMode>` for a form control, whose chain carries the mode its entry
    // step opened in. See Rask.Core.Build{T,TMode}.
    private static string BuildOf(string componentFqn, string? mode = null) =>
        mode is null
            ? "global::Rask.Core.Build<" + componentFqn + ">"
            : "global::Rask.Core.Build<" + componentFqn + ", " + mode + ">";

    private const string BoundMode = "global::Rask.Core.Forms.Bound";
    private const string ControlledMode = "global::Rask.Core.Forms.Controlled";

    // The mode argument a step that is legal in EITHER mode is written over: left open, so the chain
    // keeps whichever mode it is in rather than being pinned to one by an ordinary display prop.
    private const string OpenMode = "TMode";

    // Which mode a form control's member belongs to, or OpenMode when it belongs to both. The two
    // name lists are the whole rule (see BoundInterfaceMembers / ControlledMembers): everything else —
    // Placeholder, Rows, Options, the Element surface — is shared and stays reachable either way.
    private static string ModeOf(string memberName) =>
        Array.IndexOf(ControlledMembers, memberName) >= 0 ? ControlledMode
        : Array.IndexOf(BoundInterfaceMembers, memberName) >= 0 ? BoundMode
        : OpenMode;

    // One more type argument on a `<…>` list — or the whole list, when there was none.
    private static string Append(string typeArgs, string? extra) =>
        extra is null ? typeArgs
        : typeArgs.Length == 0 ? "<" + extra + ">"
        : typeArgs.Substring(0, typeArgs.Length - 1) + ", " + extra + ">";

    // A method's type parameter list with TMode appended, for a step written over the OPEN mode. A step
    // pinned to one mode names that mode in its receiver and declares no parameter of its own.
    private static string WithMode(string typeParameters, string? mode) =>
        string.Equals(mode, OpenMode, StringComparison.Ordinal)
            ? Append(typeParameters, OpenMode)
            : typeParameters;

    // The mode a form control's chain is in once the given opening step has been taken. `Bind` is the
    // bound mode by definition; every other way in (`Value`, and `Of` for a control given no value at
    // all) leaves the parent owning the value, which is the controlled mode.
    private static string? OpeningMode(Candidate c, EntryInference opening) =>
        c.FormControl is null
            ? null
            : string.Equals(opening.PropertyName, "Bind", StringComparison.Ordinal)
                ? BoundMode
                : ControlledMode;

    // The two ways into a form control, as steps. A GENERIC control's Bind and Value already open its
    // chain because they pin the value type (see PinCandidates); a non-generic one — BsCheck, whose value
    // is a plain bool — pins nothing, so without this its seed would offer no way to choose a mode at all
    // and the choice would fall back to two setters that could both be taken.
    private static List<List<EntryInference>> ModeOpenings(Candidate c)
    {
        var bits = OwnPendingBits(c);
        var openings = new List<List<EntryInference>>();
        foreach (var name in new[] { "Bind", "Value" })
        {
            if (c.Properties.FirstOrDefault(p => p.Name == name) is not { Name.Length: > 0 } p)
            {
                continue;
            }

            // Bind NEVER folds into propsChanged, exactly as PinCandidates and EmitBoundSetters have it:
            // an expression tree is a fresh object every render and the eager reset blanks it beforehand,
            // so a Track call here compares a new tree against null and reports a change on every single
            // frame — which costs a bound control the render cache entirely. Value is an ordinary value
            // prop and folds like one.
            var isBind = string.Equals(p.Name, "Bind", StringComparison.Ordinal);

            // Bind SUPPLIES the binding, so its parameter is non-nullable however the property is
            // declared: `BsCheck.Bind(x)` taking an `Expression<…>?` would let the mode be chosen and
            // left empty in the same breath. Value keeps its declared nullability — a control over a
            // nullable value type legitimately opens on `Value(null)`, and the generic path
            // (PinCandidates) keeps it nullable too.
            openings.Add([
                new EntryInference(
                    p.Name,
                    isBind ? StripNullable(p.TypeFqn) : p.TypeFqn,
                    p.Name,
                    !isBind && FoldsIntoPropsChanged(p.Name, p.TypeFqn, p.IsDelegate, p.IsAutoRerenderDelegate),
                    isBind ? -1 : Bit(bits, p.Name),
                    p.Summary),
            ]);
        }

        return openings;
    }

    // Whether a form control actually has a mode to choose. A control that implements IFormControl<T>
    // EXPLICITLY exposes neither Bind nor Value as a settable property, so there is no opening to build
    // and forcing a seed on it would emit one with no members at all — leaving the control unreachable
    // from markup with nothing reported. Such a control keeps the ordinary entry it always had.
    private static bool HasModeOpening(Candidate c) =>
        c.FormControl is not null && c.Properties.Any(static p => p.Name is "Bind" or "Value");

    // A generated parameter, and a generated assignment, are the property's own type and the value
    // itself. There used to be a layer here: a callback property was a CARRIER wrapping its delegate, so
    // every parameter had to be typed as the carried delegate (a lambda cannot reach a carrier — that
    // needs a delegate conversion followed by a user-defined one, which C# will not chain) and every
    // assignment had to run back through `From`. The carriers existed only so a delegate-typed property
    // would not swallow its own setter; the chain's `Build<TComponent>` receiver removes the collision at
    // its source, so the property is the delegate and there is nothing to map.
    private static string ParamType(PropInfo p) => p.TypeFqn;

    private static string SanitizeIdentifier(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return sb.ToString();
    }

    private static readonly SymbolDisplayFormat FullyQualifiedNullable =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    private readonly record struct SetterHost(
        string AssemblyName,
        string ElementFqn,
        EquatableArray<SharedSetter> Shared);

    private readonly record struct SharedSetter(
        string Name,
        string TypeFqn,
        string Owner,
        bool IsDelegate,
        string DefaultLiteral,
        bool IsElementOwned,
        bool IsRequired,
        bool HasDerivedSetter,
        string Summary = "");

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
        var names = new SortedSet<string>(StringComparer.Ordinal);
        for (var t = component; t is not null; t = t.BaseType)
        {
            foreach (var m in t.GetMembers())
            {
                if (!string.IsNullOrEmpty(m.Name))
                {
                    names.Add(m.Name);
                }
            }
        }

        return new ComponentHost(declaresComponent, assembly, new EquatableArray<string>(names.ToArray()));
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
        shared.AppendLine("///     Rask.Core's builder entries, one per framework component, in the form a");
        shared.AppendLine("///     REFERENCING assembly can name. Almost every host reaches these by inheriting");
        shared.AppendLine("///     'Rask.Core.RaskMarkup' instead; this is for the hosts that cannot inherit.");
        shared.AppendLine("/// </summary>");
        shared.Append("public static class ").AppendLine(EntryHostName(host.AssemblyName));
        shared.AppendLine("{");

        var entries = EntryCandidates(spc, candidates, taken);
        var seeded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in entries)
        {
            // The entry hands back a SEED whenever the chain has something to demand first — a type
            // argument to pin, or a required property. One property per component NAME.
            if (NeedsSeed(c))
            {
                if (!seeded.Add(c.TypeName))
                {
                    continue;
                }

                EmitEntryDoc(sb, c);
                sb.Append(c.IsPublic ? "    protected static " : "    private protected static ")
                    .Append(SeedFqn(c)).Append(' ').Append(EscapeIdentifier(c.TypeName))
                    .AppendLine(" = default;");

                EmitEntryDoc(shared, c);
                shared.Append(c.IsPublic ? "    public static " : "    internal static ")
                    .Append(SeedFqn(c)).Append(' ').Append(EscapeIdentifier(c.TypeName))
                    .AppendLine(" => default;");
                continue;
            }

            // An internal component cannot surface through a `protected` member of the public
            // Component (CS0053); `private protected` keeps it to derived types in this assembly.
            //
            // The entry opens a chain, so it hands back `Build<TComponent>` and not the component: the
            // steps after it are extension methods on the chain, which is what keeps a delegate-typed
            // property from swallowing its own setter (see Rask.Core.Build{T}).
            EmitEntryDoc(sb, c);
            sb.Append(c.IsPublic ? "    protected static " : "    private protected static ")
                .Append(BuildOf(c.FullyQualifiedName)).Append(' ')
                .Append(EscapeIdentifier(c.TypeName)).Append(" => new(").Append(runtime).Append(EntryMethod(c))
                .Append(c.FullyQualifiedName).Append(">(");
            EmitResetArguments(sb, c, host.AssemblyName);
            sb.AppendLine("));");

            EmitEntryDoc(shared, c);
            shared.Append(c.IsPublic ? "    public static " : "    internal static ")
                .Append(BuildOf(c.FullyQualifiedName)).Append(' ')
                .Append(EscapeIdentifier(c.TypeName)).Append(" => new(").Append(runtime).Append(EntryMethod(c))
                .Append(c.FullyQualifiedName).Append(">(");
            EmitResetArguments(shared, c, host.AssemblyName);
            shared.AppendLine("));");
        }

        sb.AppendLine("}");
        spc.AddSource("RaskBuilderEntries.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

        shared.AppendLine("}");
        EmitSeeds(shared, entries, host.AssemblyName, runtime);
        spc.AddSource("RaskBuilderEntryHost.g.cs", SourceText.From(shared.ToString(), Encoding.UTF8));
    }

    /// <summary>
    ///     The seed types a generic component's entry hands back, and the pins that turn one into the
    ///     component.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The pins are <b>extension methods</b>, which is what lets them be generic where the entry
    ///         property cannot be, and what lets them infer: <c>Input.Bind(() =&gt; m.Name)</c> pins
    ///         <c>T</c> from the expression, <c>BsRadioGroup.Options(all)</c> from the sequence.
    ///     </para>
    ///     <para>
    ///         In the GLOBAL namespace, like the setters, so a referencing assembly reaches them with no
    ///         <c>using</c> — and so a consumer needs nothing injected for them: only the entry property
    ///         is forwarded per host, while the pins are found once, wherever the chain is written.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     The whole staged surface: a seed per component that has anything to demand, the states in
    ///     between, and the steps that move from one to the next.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A chain is a state machine and the TYPE at each point says what is legal next. The seed
    ///         offers the ways in; each step returns a state that offers only what is still outstanding;
    ///         and the component — with its optional setters and its children indexer — appears once
    ///         nothing is. So a required property cannot be forgotten, and two mutually exclusive ways in
    ///         cannot both be taken, without either being reported: they are unrepresentable.
    ///     </para>
    ///     <para>
    ///         States are identified by WHICH required properties are already set, so the remaining ones
    ///         may be given in any order — <c>BsToast.Id(7).Message("…")</c> and
    ///         <c>BsToast.Message("…").Id(7)</c> are both chains and both end at the component. That costs
    ///         one struct per reachable subset, which is why only REQUIRED properties take part: the
    ///         optional surface would make it 2^n over everything.
    ///     </para>
    /// </remarks>
    private static void EmitSeeds(
        StringBuilder sb, List<Candidate> entries, string assemblyName, string runtimePrefix)
    {
        var staged = entries.Where(NeedsSeed).ToList();
        if (staged.Count == 0)
        {
            return;
        }

        foreach (var byNamespace in staged.GroupBy(static c => c.Namespace, StringComparer.Ordinal))
        {
            var scoped = byNamespace.Key.Length != 0;
            if (scoped)
            {
                sb.AppendLine();
                sb.Append("namespace ").AppendLine(byNamespace.Key);
                sb.AppendLine("{");
            }

            foreach (var name in byNamespace.GroupBy(static c => c.TypeName, StringComparer.Ordinal))
            {
                EmitStateTypes(sb, name.First(), scoped ? "    " : string.Empty, assemblyName, runtimePrefix);
            }

            if (scoped)
            {
                sb.AppendLine("}");
            }
        }

    }

    // The seed, the type stage an opening of two pins needs, and one state per reachable subset of
    // satisfied required properties. All EditorBrowsable(Never): they are machinery a chain moves
    // through, never a type anybody names, and they would otherwise crowd every completion list that has
    // the component's namespace in scope.
    // The seed, the stage an opening of two steps needs, and one state per reachable subset of satisfied
    // required properties — each carrying the steps that lead out of it as INSTANCE methods.
    //
    // Instance rather than extension methods, and the difference is not stylistic. A state fixes its own
    // type arguments, so a step on it introduces none of its own — which lets an overload that pins
    // nothing extra beat one that does, and that is what lets `Options(IEnumerable<TValue>)` win over
    // `Options<TItem>(IEnumerable<TItem>)` when the option IS the value. As extensions both would have
    // had to declare the state's type parameters themselves, leaving them equally generic and the call
    // ambiguous (CS0121). They also need no `using`, which extensions in another assembly only avoid by
    // living in the global namespace.
    private static void EmitStateTypes(
        StringBuilder sb, Candidate c, string pad, string assemblyName, string runtimePrefix)
    {
        const string hidden =
            "[global::System.ComponentModel.EditorBrowsable("
            + "global::System.ComponentModel.EditorBrowsableState.Never)]";
        var visibility = c.IsPublic ? "public" : "internal";
        var required = RequiredSteps(c);
        var openings = Openings(c);

        // A form control's stages and states carry the mode forward as a type parameter of their own: the
        // opening step fixed it, everything between passes it along, and the chain it finally hands back
        // is in the mode the chain opened in. Anything else is `null` here and nothing changes for it.
        var carriedMode = c.FormControl is null ? null : OpenMode;

        sb.Append(pad).Append("/// <summary>Where a ").Append(c.TypeName)
            .AppendLine(" chain starts. Take one of its steps to begin.</summary>");
        sb.Append(pad).AppendLine(hidden);
        sb.Append(pad).Append(visibility).Append(" readonly struct ").Append(SeedName(c)).AppendLine();
        sb.Append(pad).AppendLine("{");

        // Key has to be able to come FIRST — before the required steps, not merely before the optional
        // ones (#685, RASK046). It decides WHICH instance the chain is building, so a required property
        // written ahead of it lands on the instance the key then discards: `BsToast.Id(…).Message(…)
        // .Key(…)` was silently rendering the previous frame's message whenever the list changed shape.
        //
        // The seed itself holds no component — the first step constructs one — so this step constructs,
        // claims, and hands back the state that still awaits everything. The required steps then assign
        // onto the instance the key settled on, which is the whole point.
        //
        // Only for a non-generic component: a generic one has type arguments still to be pinned by its
        // opening, so there is no state type to name here yet. Those keep Key on the finished chain.
        var seedCarriesKey = required.Count > 0 && c.TypeParameters.Length == 0;
        if (seedCarriesKey)
        {
            sb.Append(pad).AppendLine("    private readonly object? _key;");
            sb.Append(pad).AppendLine();
            sb.Append(pad).Append("    internal ").Append(SeedName(c)).AppendLine("(object? key) => _key = key;");
            sb.Append(pad).AppendLine();
            sb.Append(pad).AppendLine(
                "    /// <summary>Sets the reconciliation identity. Name it FIRST — see RASK046.</summary>");
            sb.Append(pad).Append("    public ").Append(SeedName(c)).AppendLine(" Key(object? key) => new(key);");
            sb.Append(pad).AppendLine();
        }

        EmitExplicitTypeOpening(sb, c, pad + "    ", assemblyName, runtimePrefix, required);

        foreach (var opening in openings)
        {
            if (opening.Count == 0)
            {
                // Nothing to pin, so the component is constructible from the word go and any one of its
                // required properties opens the chain.
                foreach (var first in required)
                {
                    EmitBuildingStep(
                        sb, c, pad + "    ", assemblyName, runtimePrefix, first, [],
                        new HashSet<string>(StringComparer.Ordinal) { first.PropertyName }, mode: null,
                        seedCarriesKey);
                }

                continue;
            }

            if (opening.Count == 2)
            {
                // The stage is reached by the opening step, so it is instantiated in the mode that step
                // chose — the chain is in one from here on.
                var stageParams = TypeParametersFor(c, opening[0]);
                sb.Append(pad).Append("    public ")
                    .Append(StageFqn(c, opening[0], Append(stageParams, OpeningMode(c, opening[0])))).Append(' ')
                    .Append(EscapeIdentifier(opening[0].ParamName)).Append(stageParams).Append('(')
                    .Append(StepParamType(opening[0])).Append(' ')
                    .Append(EscapeIdentifier(opening[0].ParamName)).AppendLine(")");
                sb.Append(pad).Append("        => new(").Append(EscapeIdentifier(opening[0].ParamName))
                    .AppendLine(");");
                continue;
            }

            EmitBuildingStep(
                sb, c, pad + "    ", assemblyName, runtimePrefix, opening[0], [],
                SatisfiedBy(c, opening), OpeningMode(c, opening[0]), seedCarriesKey);
        }

        sb.Append(pad).AppendLine("}");

        foreach (var opening in openings.Where(static o => o.Count == 2))
        {
            var stageParams = TypeParametersFor(c, opening[0]);
            sb.Append(pad).Append("/// <summary>").Append(c.TypeName).Append(" awaiting ")
                .Append(opening[1].ParamName).AppendLine(", which fixes the rest of its type.</summary>");
            sb.Append(pad).AppendLine(hidden);
            sb.Append(pad).Append(visibility).Append(" readonly struct ").Append(StageName(c, opening[0]))
                .Append(Append(stageParams, carriedMode)).AppendLine();
            sb.Append(pad).AppendLine("{");
            sb.Append(pad).Append("    internal ").Append(StageName(c, opening[0])).Append('(')
                .Append(StepParamType(opening[0])).Append(" value) => ")
                .Append(opening[0].ParamName).AppendLine(" = value;");
            sb.Append(pad).AppendLine();
            sb.Append(pad).Append("    private ").Append(StepParamType(opening[0])).Append(' ')
                .Append(opening[0].ParamName).AppendLine(" { get; }");
            sb.Append(pad).AppendLine();

            EmitBuildingStep(
                sb, c, pad + "    ", assemblyName, runtimePrefix, opening[1],
                [(opening[0], "this." + EscapeIdentifier(opening[0].ParamName))],
                SatisfiedBy(c, opening), carriedMode);

            EmitIdentityStep(sb, c, pad + "    ", assemblyName, runtimePrefix, opening, carriedMode);

            sb.Append(pad).AppendLine("}");
        }

        foreach (var state in ReachableStates(c))
        {
            sb.Append(pad).Append("/// <summary>").Append(c.TypeName).Append(" still awaiting ")
                .Append(string.Join(", ", required.Where(r => !state.Contains(r.PropertyName))
                    .Select(r => r.ParamName)))
                .AppendLine(".</summary>");
            sb.Append(pad).AppendLine(hidden);
            sb.Append(pad).Append(visibility).Append(" readonly struct ").Append(StateName(c, state))
                .Append(Append(c.TypeParameters, carriedMode)).AppendLine();
            sb.Append(pad).AppendLine("{");
            sb.Append(pad).Append("    internal ").Append(StateName(c, state)).Append('(')
                .Append(c.FullyQualifiedName).AppendLine(" component) => Component = component;");
            sb.Append(pad).AppendLine();
            sb.Append(pad).Append("    private ").Append(c.FullyQualifiedName)
                .AppendLine(" Component { get; }");

            // Key is offered here, not only on the finished chain, because it has to be able to come
            // FIRST (#685, RASK046): it decides which instance is being built, so anything written before
            // it lands on the one the key discards. A component with required steps would otherwise have
            // no way to satisfy both rules — `BsToast.Id(…).Message(…).Key(…)` is precisely the shape that
            // was silently losing its props. Returns the same state, so it composes anywhere in the
            // required sequence and changes nothing about what is still outstanding.
            sb.Append(pad).AppendLine();
            sb.Append(pad).AppendLine(
                "    /// <summary>Sets the reconciliation identity. Name it FIRST — see RASK046.</summary>");
            sb.Append(pad).Append("    public ").Append(StateName(c, state))
                .Append(Append(c.TypeParameters, carriedMode))
                .AppendLine(" Key(object? key)");
            sb.Append(pad).AppendLine("    {");
            sb.Append(pad).AppendLine(
                "        var __c = global::Rask.Core.BuilderRuntime.ClaimKey(Component, key);");
            sb.Append(pad).AppendLine("        __c.Key = key;");
            sb.Append(pad).AppendLine("        return new(__c);");
            sb.Append(pad).AppendLine("    }");

            foreach (var step in required.Where(r => !state.Contains(r.PropertyName)))
            {
                var next = new HashSet<string>(state, StringComparer.Ordinal) { step.PropertyName };
                var done = next.Count == required.Count;
                sb.Append(pad).AppendLine();
                EmitDocComment(sb, step.Summary, pad + "    ");
                sb.Append(pad).Append("    public ")
                    .Append(done
                        ? BuildOf(c.FullyQualifiedName, carriedMode)
                        : StateFqn(c, next) + Append(c.TypeParameters, carriedMode))
                    .Append(' ').Append(EscapeIdentifier(step.ParamName)).Append('(')
                    .Append(StepParamType(step)).Append(' ').Append(EscapeIdentifier(step.ParamName))
                    .AppendLine(")");
                sb.Append(pad).AppendLine("    {");
                sb.Append(pad).AppendLine("        var __c = Component;");
                EmitPinAssignment(sb, step, EscapeIdentifier(step.ParamName), pad + "    ");
                // Either way a target-typed `new`: the last step wraps the component in its chain, an
                // earlier one hands on the state still waiting for something.
                sb.Append(pad).AppendLine("        return new(__c);");
                sb.Append(pad).AppendLine("    }");
            }

            sb.Append(pad).AppendLine("}");
        }
    }


    // The way into a generic component that has nothing to infer from.
    //
    // Every other opening PINS: `Input.Bind(() => m.Name)` reads T off the expression, and that is the
    // shape almost every call site wants. But a generic component is not obliged to be used generically
    // — Rask.Bootstrap drives a bare `<input type=checkbox>` through `Input<string>`, naming no bind and
    // no value, purely for the element half of it. The old generic FACTORY had a no-argument overload for
    // exactly this; a seed of pins alone silently dropped it, which is what left those sites on the
    // factory. `Input.Of<string>()` is that overload, restored as a step.
    //
    // It states the type argument rather than inferring one, so it is spelled `Of` rather than sharing a
    // pin's name: a reader seeing `Of<string>` knows nothing was inferred.
    //
    // Only for a generic component with NOTHING REQUIRED. Where something is required, one of its steps
    // is the opening and that step already pins the type — `Form.Model(m)` reads TModel off the model —
    // so `Of` would be a second spelling of the same move, and a chain that took it would owe the
    // required step anyway.
    private static void EmitExplicitTypeOpening(
        StringBuilder sb, Candidate c, string pad, string assemblyName, string runtimePrefix,
        List<EntryInference> required)
    {
        if (c.TypeParameters.Length == 0 || required.Count != 0)
        {
            return;
        }

        // A control opened this way was given no value at all, so the parent still owns whatever it ends
        // up with: that is the controlled mode, and `Of` is the way into it for a control that wants only
        // the element half — `Input.Of<string>().Type(Search).Placeholder("…")`.
        var result = BuildOf(c.FullyQualifiedName, c.FormControl is null ? null : ControlledMode);

        sb.Append(pad).Append("/// <summary>Opens a ").Append(c.TypeName)
            .AppendLine(" whose type argument is stated rather than inferred.</summary>");
        sb.Append(pad).Append("public ").Append(result).Append(" Of").Append(c.TypeParameters)
            .Append("()").AppendLine(c.TypeParameterConstraints);
        sb.Append(pad).AppendLine("{");
        sb.Append(pad).Append("    var __c = ").Append(runtimePrefix).Append(EntryMethod(c))
            .Append(c.FullyQualifiedName).Append(">(");
        EmitResetArguments(sb, c, assemblyName, c.TypeParameters);
        sb.AppendLine(");");
        sb.Append(pad).AppendLine("    return new(__c);");
        sb.Append(pad).AppendLine("}");
        sb.AppendLine();
    }

    // The shortcut for "the option IS the value".
    //
    // `BsSelect` carries two type parameters and a required projection between them, so the long way
    // round is `.Options(items).OptionValue(x => x)` — stating an identity nobody wanted to write. When
    // the sequence's element type is the value type the projection is knowable, so an overload takes
    // that case and fills it in.
    //
    // This is only expressible because the steps are INSTANCE methods: the stage fixes TValue, so this
    // overload introduces no type parameter of its own and beats the generic one. As extension methods
    // both would have had to declare TValue themselves, leaving them equally generic and the call
    // ambiguous (CS0121) — which is what made the two-arity design look impossible in the first place.
    private static void EmitIdentityStep(
        StringBuilder sb, Candidate c, string pad, string assemblyName, string runtimePrefix,
        List<EntryInference> opening, string? mode)
    {
        var names = OrderedTypeParameters(c.TypeParameters);
        if (names.Count != 2)
        {
            return;
        }

        var pinnedByStage = MentionedTypeParameters(opening[0].ParamTypeFqn, ParseTypeParameters(c.TypeParameters));
        var value = names.FirstOrDefault(pinnedByStage.Contains);
        var item = names.FirstOrDefault(n => !pinnedByStage.Contains(n));
        if (value is null || item is null)
        {
            return;
        }

        // The only thing still outstanding after this step must be the projection itself.
        var satisfied = SatisfiedBy(c, opening);
        satisfied.Add(opening[1].PropertyName);
        var outstanding = RequiredSteps(c).Where(r => !satisfied.Contains(r.PropertyName)).ToList();
        if (outstanding.Count != 1)
        {
            return;
        }

        var projection = outstanding[0];
        // A step's parameter is the property's declared type, so a nullable one keeps its `?`; the shape
        // comparison is about the delegate itself.
        var expected = "global::System.Func<" + item + ", " + value + ">";
        if (StepParamType(projection).TrimEnd('?') != expected)
        {
            return;
        }

        var unified = RenameTypeParameter(c.FullyQualifiedName, item, value);
        sb.Append(pad).Append("public ").Append(BuildOf(unified, mode)).Append(' ')
            .Append(EscapeIdentifier(opening[1].ParamName)).Append('(')
            .Append(RenameTypeParameter(StepParamType(opening[1]), item, value)).Append(' ')
            .Append(EscapeIdentifier(opening[1].ParamName)).AppendLine(")");
        sb.Append(pad).AppendLine("{");
        sb.Append(pad).Append("    var __c = ").Append(runtimePrefix).Append(EntryMethod(c))
            .Append(unified).Append(">(");
        // The reset delegates are generic over the component's parameters, and here both are TValue —
        // the whole point of this overload. Handing them the component's own list would name a TItem
        // this method does not declare.
        EmitResetArguments(sb, c, assemblyName, "<" + value + ", " + value + ">");
        sb.AppendLine(");");
        // Through EmitPinAssignment, not raw: a step has to mark its property WRITTEN, or the deferred
        // reset blanks it again at the end of the parent's Render(). Assigning these directly is what
        // made every identity-form select render with a null `Options` — caught by the golden markup,
        // which is the only thing that would have caught it.
        EmitPinAssignment(sb, opening[0], "this." + EscapeIdentifier(opening[0].ParamName), pad);
        EmitPinAssignment(sb, opening[1], EscapeIdentifier(opening[1].ParamName), pad);
        // The projection's own type still names TItem, which this overload does not declare — it is the
        // one being unified away.
        EmitPinAssignment(
            sb,
            projection with { ParamTypeFqn = RenameTypeParameter(projection.ParamTypeFqn, item, value) },
            "static __x => __x",
            pad);
        sb.Append(pad).AppendLine("    return new(__c);");
        sb.Append(pad).AppendLine("}");
    }

    // Which required properties an opening has already supplied — `Options` opens nothing for a form
    // control, but for a component whose only required property also pins the type, the opening is the
    // whole of what was outstanding.
    private static HashSet<string> SatisfiedBy(Candidate c, List<EntryInference> opening) =>
        new(RequiredSteps(c)
                .Where(r => opening.Any(o =>
                    string.Equals(o.PropertyName, r.PropertyName, StringComparison.Ordinal)))
                .Select(r => r.PropertyName),
            StringComparer.Ordinal);

    // A step that CONSTRUCTS: it completes the component's type, so it builds, assigns whatever earlier
    // steps parked, assigns its own, and hands back either the component or the state still wanting
    // something.
    private static void EmitBuildingStep(
        StringBuilder sb, Candidate c, string pad, string assemblyName, string runtimePrefix,
        EntryInference step, IReadOnlyList<(EntryInference Pin, string Value)> carried,
        HashSet<string> satisfied, string? mode, bool carriesKey = false)
    {
        var required = RequiredSteps(c);
        var done = satisfied.Count == required.Count;
        var result = done
            ? BuildOf(c.FullyQualifiedName, mode)
            : StateFqn(c, satisfied) + Append(c.TypeParameters, mode);
        // A step on a STAGE declares only the type parameters the stage has not already fixed: the stage
        // is generic over what the first step pinned, and re-declaring those would shadow them (CS0693),
        // while declaring none leaves the ones this step pins unresolved.
        var fixedByStage = new HashSet<string>(
            carried.SelectMany(x => MentionedTypeParameters(
                x.Pin.ParamTypeFqn, ParseTypeParameters(c.TypeParameters))),
            StringComparer.Ordinal);
        var outstanding = OrderedTypeParameters(c.TypeParameters).Where(n => !fixedByStage.Contains(n)).ToList();
        var methodParams = outstanding.Count == 0 ? string.Empty : "<" + string.Join(", ", outstanding) + ">";

        EmitDocComment(sb, step.Summary, pad);
        sb.Append(pad).Append("public ").Append(result).Append(' ')
            .Append(EscapeIdentifier(step.ParamName)).Append(methodParams).Append('(')
            .Append(StepParamType(step)).Append(' ').Append(EscapeIdentifier(step.ParamName)).Append(')')
            .Append(methodParams.Length == 0 ? string.Empty : c.TypeParameterConstraints).AppendLine();
        sb.Append(pad).AppendLine("{");
        sb.Append(pad).Append("    var __c = ").Append(runtimePrefix).Append(EntryMethod(c))
            .Append(c.FullyQualifiedName).Append(">(");
        EmitResetArguments(sb, c, assemblyName, c.TypeParameters);
        sb.AppendLine(");");

        // The key the seed was carrying, applied BEFORE any property is assigned — which is the whole
        // reason the seed carries it (#685, RASK046). Claiming settles which instance the chain is
        // building, so every pin below lands on that one rather than on an instance about to be
        // discarded. A default seed carries null, and a null key claims nothing.
        if (carriesKey)
        {
            sb.Append(pad).AppendLine("    __c = global::Rask.Core.BuilderRuntime.ClaimKey(__c, _key);");
            sb.Append(pad).AppendLine("    if (_key is not null) { __c.Key = _key; }");
        }

        foreach (var (pin, value) in carried)
        {
            EmitPinAssignment(sb, pin, value, pad);
        }

        EmitPinAssignment(sb, step, EscapeIdentifier(step.ParamName), pad);
        // Target-typed either way — the chain when this step completed the component, otherwise the
        // state that still wants something.
        sb.Append(pad).AppendLine("    return new(__c);");
        sb.Append(pad).AppendLine("}");
    }

    // The required properties a chain has to name, as steps. Required means what RASK001 means — a
    // non-nullable property with no member initializer — plus the language's `required` modifier.
    private static List<EntryInference> RequiredSteps(Candidate c)
    {
        var bits = OwnPendingBits(c);
        var steps = new List<EntryInference>();
        foreach (var p in c.Properties)
        {
            if (!IsRequiredFactoryParam(p) || p.IsInitOnly || p.Name == "Children" || p.IsSharedSurfaceProp)
            {
                continue;
            }

            steps.Add(new EntryInference(
                p.Name,
                p.TypeFqn,
                p.Name,
                FoldsIntoPropsChanged(p.Name, p.TypeFqn, p.IsDelegate, p.IsAutoRerenderDelegate),
                Bit(bits, p.Name),
                p.Summary));
        }

        return steps;
    }

    // The ways in. A generic component's are its pin chains; a non-generic one has nothing to pin, so its
    // single (empty) opening means "buildable straight away" and its required properties open the chain.
    // Whether a component's entry hands back a SEED rather than the component: it does whenever there is
    // anything to demand first — a type argument to pin, or a required property to supply.
    /// <summary>
    ///     Whether a property is one of the MUTUALLY EXCLUSIVE ways into a control, and so must not also
    ///     be reachable as a setter.
    /// </summary>
    /// <remarks>
    ///     Only a form control's <c>Bind</c> and <c>Value</c> qualify. They are two answers to one
    ///     question — where the value comes from — so a chain that took either must not be able to take
    ///     the other, and leaving them as setters is exactly how a bound control could still be handed a
    ///     value.
    ///     <para>
    ///         Every OTHER property that happens to pin a type argument stays a setter as well as a step.
    ///         They are not alternatives: <c>BsDataGrid.Data(rows).Columns(cols)</c> sets both, and
    ///         withdrawing the second because it could have opened the chain made it unreachable
    ///         (CS1955 — the property is invoked, because no setter exists). Requiredness is not enforced
    ///         by withholding the setter; it is enforced by the component not existing until the step is
    ///         taken.
    ///     </para>
    /// </remarks>
    private static bool IsExclusiveOpening(Candidate c, string name) =>
        c.FormControl is not null && name is "Bind" or "Value"
        // …and only where a SEED exists to reach them through. A non-generic control with nothing
        // required — `BsCheck`, whose Bind is a plain `Expression<Func<bool>>?` — has no chain in front
        // of it, so withdrawing the setter leaves the property with no way in at all.
        && NeedsSeed(c);

    // …and a FORM CONTROL always needs one, whether or not it has anything to pin or demand: its seed is
    // where the mode is chosen, and the mode has to be chosen before there is a chain to put steps on.
    private static bool NeedsSeed(Candidate c) =>
        c.TypeParameters.Length != 0 || RequiredSteps(c).Count != 0 || HasModeOpening(c);

    private static List<List<EntryInference>> Openings(Candidate c)
    {
        if (c.TypeParameters.Length == 0)
        {
            return HasModeOpening(c) ? ModeOpenings(c) : [[]];
        }

        var sets = PinSets(c);

        // A FORM CONTROL opens on its value and nothing else. Its other properties can pin the type just
        // as well — `Options` is an `IEnumerable<TItem>` — but letting one of those open the chain says
        // the control is complete before it has been told where its value comes from, and leaves the
        // choice between bound and controlled unmade. Narrowing this is what makes the mode the first
        // decision: `BsCheckboxGroup.Value(v).Options(o)` or `.Bind(x).Options(o)`, never `.Options(o)`
        // followed by whichever of the two the author remembered.
        if (c.FormControl is null)
        {
            return sets;
        }

        var valueFirst = sets
            .Where(s => s.Count != 0 && s[0].PropertyName is "Bind" or "Value")
            .ToList();
        return valueFirst.Count != 0 ? valueFirst : sets;
    }

    // Every state a chain can stand in: some required properties set, at least one still missing. Named
    // by what is SATISFIED, so two orders through the same set meet at the same type instead of
    // multiplying.
    private static List<HashSet<string>> ReachableStates(Candidate c)
    {
        var required = RequiredSteps(c);
        var states = new List<HashSet<string>>();
        if (required.Count == 0)
        {
            return states;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new List<HashSet<string>>();

        foreach (var opening in Openings(c))
        {
            var satisfied = required
                .Where(r => opening.Any(o => string.Equals(o.PropertyName, r.PropertyName, StringComparison.Ordinal)))
                .Select(r => r.PropertyName);
            var satisfiedSet = new HashSet<string>(satisfied, StringComparer.Ordinal);

            // A non-generic component opens on any one of its required properties.
            if (opening.Count == 0)
            {
                foreach (var first in required)
                {
                    frontier.Add(new HashSet<string>(StringComparer.Ordinal) { first.PropertyName });
                }

                continue;
            }

            frontier.Add(satisfiedSet);
        }

        while (frontier.Count != 0)
        {
            var state = frontier[frontier.Count - 1];
            frontier.RemoveAt(frontier.Count - 1);
            // The EMPTY state is a real one whenever an opening pins the type without satisfying any
            // required property — `BsCheckboxGroup.Bind(x)` still owes `Options`. It is a distinct type
            // from the seed, because it carries the component the opening built.
            if (state.Count >= required.Count || !seen.Add(StateKey(state)))
            {
                continue;
            }

            states.Add(state);
            foreach (var step in required.Where(r => !state.Contains(r.PropertyName)))
            {
                frontier.Add(new HashSet<string>(state, StringComparer.Ordinal) { step.PropertyName });
            }
        }

        return states;
    }

    private static string StateKey(IEnumerable<string> satisfied) =>
        string.Join("_", satisfied.OrderBy(static s => s, StringComparer.Ordinal));

    private static string StateName(Candidate c, IEnumerable<string> satisfied)
    {
        var key = StateKey(satisfied);
        return key.Length == 0 ? "RaskPending_" + c.TypeName : "RaskPending_" + c.TypeName + "_" + key;
    }

    private static string StateFqn(Candidate c, IEnumerable<string> satisfied) =>
        (c.Namespace.Length == 0 ? "global::" : "global::" + c.Namespace + ".") + StateName(c, satisfied);

    // What a step does to the component, which is exactly what the property's own setter would do — the
    // fold that reports `propsChanged`, and the pending bit that tells the deferred reset this prop was
    // written after all. Shared by every step form so they cannot drift.
    private static void EmitPinAssignment(
        StringBuilder sb, EntryInference pin, string value, string pad = "")
    {
        var assigned = value;

        if (pin.Track)
        {
            sb.Append(pad).Append("        global::Rask.Core.BuilderRuntime.Track(__c, __c.")
                .Append(EscapeIdentifier(pin.PropertyName)).Append(", ").Append(assigned).AppendLine(");");
        }

        if (pin.PendingBit >= 0)
        {
            sb.Append(pad).Append("        global::Rask.Core.BuilderRuntime.Written(__c, ")
                .Append(MaskLiteral(new[] { pin.PendingBit })).AppendLine(");");
        }

        sb.Append(pad).Append("        __c.").Append(EscapeIdentifier(pin.PropertyName)).Append(" = ")
            .Append(assigned).AppendLine(";");
    }

    // A step's parameter is the property's own type.
    private static string StepParamType(EntryInference step) => step.ParamTypeFqn;

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
    //    (CrossAssemblyRequiredPropertyTests);
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
    // been constructible and permanently incomplete. The chain's `Build<TComponent>` receiver removed
    // that, so ValidationMessage, ValidationSummary, ValidatingIndicator, ToastOutlet, Shareable, the
    // GestureTrigger family and BsSelect's OptionValue simply have entries.
    //
    // …and a name Component already declares (`Head`) still blocks too, which would be CS0102.
    private static bool CanHaveEntry(Candidate c, HashSet<string> taken) =>
        (c.TypeParameters.Length == 0 ? c.HasParameterlessCtor || c.HasDIConstructor : HasGenericEntryShape(c))
        && !taken.Contains(c.TypeName);

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

    private static string SeedName(Candidate c) => "RaskSeed_" + c.TypeName;

    // Named after the step that OPENS it, because two ways in carry different things: `Bind` parks an
    // expression and `Value` parks a value, so one stage type cannot serve both — its constructor would
    // have to take either.
    private static string StageName(Candidate c, EntryInference opening) =>
        "RaskStage_" + c.TypeName + "_" + opening.ParamName;

    private static string StageFqn(Candidate c, EntryInference opening, string typeParameters) =>
        (c.Namespace.Length == 0 ? "global::" : "global::" + c.Namespace + ".")
        + StageName(c, opening) + typeParameters;

    // The type parameters a pin accounts for, in the component's own declaration order — the stage
    // between two pins is generic over exactly the ones the FIRST pin fixed.
    private static string TypeParametersFor(Candidate c, EntryInference pin)
    {
        var names = ParseTypeParameters(c.TypeParameters);
        var mentioned = MentionedTypeParameters(pin.ParamTypeFqn, names);
        var ordered = OrderedTypeParameters(c.TypeParameters).Where(mentioned.Contains).ToList();
        return ordered.Count == 0 ? string.Empty : "<" + string.Join(", ", ordered) + ">";
    }

    // "<TValue, TItem>" → [TValue, TItem]. ParseTypeParameters answers the same question as a SET, which
    // loses the order a type parameter list has to keep.
    private static List<string> OrderedTypeParameters(string list)
    {
        var result = new List<string>();
        if (list.Length < 3)
        {
            return result;
        }

        foreach (var name in list.Substring(1, list.Length - 2).Split(','))
        {
            var trimmed = name.Trim();
            if (trimmed.Length != 0)
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    /// <summary>
    ///     Every way in to a generic component: one pin set per overload the seed publishes.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A pin set has to pin <b>every</b> type parameter, because C# infers a method's type
    ///         arguments all or nothing. So each property that can do that alone becomes an overload of
    ///         its own — <c>Input.Bind(() =&gt; m.Name)</c>, <c>Input.Value(_text)</c>,
    ///         <c>BsRadioGroup.Options(AllPlans)</c> — and a component no single property can pin falls
    ///         back to the one combination <see cref="InferencePins" /> assembles, spelled as a staged
    ///         chain — <c>BsSelect.Bind(() =&gt; m.PersonId).Options(people)</c>.
    ///     </para>
    ///     <para>
    ///         Several ways in is the point rather than a cost: the pin is also what carries the type, so
    ///         a component reachable only through <c>Bind</c> would have no spelling at all for a
    ///         controlled site, and one reachable only through <c>Options</c> none for a bound one.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     Every property a chain has to name before the component exists: the ones that pin a type
    ///     argument, and the ones that are required.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These are steps rather than setters, and the difference is the whole of what the chain
    ///         enforces. A step is reachable only from the state before it, so a required property cannot
    ///         be forgotten (the component is not produced until it is set) and two mutually exclusive
    ///         ones cannot both be used (taking either leaves the other behind).
    ///     </para>
    ///     <para>
    ///         Required here means what RASK001 means — a non-nullable property with no member
    ///         initializer — plus the language's own <c>required</c> modifier, which is the same set
    ///         RASK038 walks a chain looking for. <c>Children</c> is never a step: it arrives through the
    ///         indexer, which the finished component carries.
    ///     </para>
    /// </remarks>
    private static List<EntryInference> ChainSteps(Candidate c)
    {
        var steps = new List<EntryInference>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // EVERY pin, not just the ones on one chain: a component publishes several ways in — `Bind` for a
        // bound control, `Value` for a controlled one — and each is a step. Taking only one chain left
        // the others as ordinary setters, which is precisely how a bound control could still be handed a
        // `Value`. Longest chain first, so the ones that must precede others still do.
        foreach (var pin in PinSets(c).OrderByDescending(s => s.Count).SelectMany(s => s))
        {
            if (seen.Add(pin.PropertyName))
            {
                steps.Add(pin);
            }
        }

        var bits = OwnPendingBits(c);
        foreach (var p in c.Properties)
        {
            if (!IsRequiredFactoryParam(p) || p.IsInitOnly || p.Name == "Children" || !seen.Add(p.Name))
            {
                continue;
            }

            steps.Add(new EntryInference(
                p.Name,
                p.TypeFqn,
                p.Name,
                FoldsIntoPropsChanged(p.Name, p.TypeFqn, p.IsDelegate, p.IsAutoRerenderDelegate),
                Bit(bits, p.Name),
                p.Summary));
        }

        return steps;
    }

    private static List<List<EntryInference>> PinSets(Candidate c)
    {
        var names = ParseTypeParameters(c.TypeParameters);
        var sets = new List<List<EntryInference>>();
        if (names.Count == 0)
        {
            return sets;
        }

        var candidates = PinCandidates(c, names).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var first in candidates)
        {
            if (!seen.Add(first.ParamName))
            {
                continue;
            }

            // Every way in is grown from ITS OWN first step, completing whatever type parameters that
            // step left open with the other candidates. One pin is a chain of one; a component whose
            // value fixes only half its type — `BsSelect`, whose `TItem` comes from `Options` — gets a
            // staged chain per way in. Building only ONE chain (the bound one) is what made controlled
            // mode unreachable: `Value` pinned TValue, never completed, and so opened nothing.
            var chain = new List<EntryInference> { first };
            var unpinned = new HashSet<string>(names, StringComparer.Ordinal);
            unpinned.ExceptWith(MentionedTypeParameters(first.ParamTypeFqn, names));

            foreach (var next in candidates)
            {
                if (unpinned.Count == 0)
                {
                    break;
                }

                var mentioned = MentionedTypeParameters(next.ParamTypeFqn, names);
                if (string.Equals(next.ParamName, first.ParamName, StringComparison.Ordinal)
                    || !mentioned.Overlaps(unpinned))
                {
                    continue;
                }

                chain.Add(next);
                unpinned.ExceptWith(mentioned);
            }

            if (unpinned.Count == 0)
            {
                sets.Add(chain);
            }
        }

        return sets;
    }

    // The properties a pin can be made from, in the order the overloads should read: an
    // IFormControl<T>'s Bind first, then the component's own factory-parameter properties.
    //
    // Never a DELEGATE, however plainly it names the type parameter: the argument at the call site is an
    // implicitly-typed lambda, which contributes nothing to inference. BsSelect's OptionValue
    // (Func<TItem, TValue>) is exactly that shape — it would compile here and fail at every call site.
    private static IEnumerable<EntryInference> PinCandidates(Candidate c, HashSet<string> names)
    {
        var bits = OwnPendingBits(c);

        if (c.FormControl is { } fc)
        {
            yield return new EntryInference(
                "Bind",
                "global::System.Linq.Expressions.Expression<global::System.Func<" + fc.ValueTypeFqn + ">>",
                "Bind",
                Track: false,
                PendingBit: -1);
        }

        foreach (var p in c.Properties)
        {
            if (p.IsInitOnly || p.IsSharedSurfaceProp || !IsParamProperty(p) || p.IsDelegate
                || p.IsBoundInterfaceProp)
            {
                continue;
            }

            var type = p.TypeFqn;
            if (MentionedTypeParameters(type, names).Count == 0)
            {
                continue;
            }

            // The pin has to leave the property exactly as its own setter would, or the two surfaces
            // disagree about a prop that every chain sets: the fold that reports `propsChanged`, and the
            // pending bit that tells the deferred reset this prop was written after all.
            yield return new EntryInference(
                p.Name,
                type,
                p.Name,
                FoldsIntoPropsChanged(p.Name, p.TypeFqn, p.IsDelegate, p.IsAutoRerenderDelegate),
                Bit(bits, p.Name),
                p.Summary);
        }
    }

    private static List<EntryInference>? InferencePins(Candidate c)
    {
        var names = ParseTypeParameters(c.TypeParameters);
        if (names.Count == 0)
        {
            return null;
        }

        // Greedy over the SAME candidate list the single-property sets are drawn from, so the two cannot
        // disagree about what may be a pin. That mattered once and silently: this loop had its own filter
        // and never learned to skip a DELEGATE, so BsSelect's second pin came out as `OptionValue`
        // (Func<TItem, TValue>) — which compiles here and infers nothing at any call site, because the
        // argument is an implicitly-typed lambda.
        var pins = new List<EntryInference>();
        var unpinned = new HashSet<string>(names, StringComparer.Ordinal);

        foreach (var pin in PinCandidates(c, names))
        {
            if (unpinned.Count == 0)
            {
                break;
            }

            var mentioned = MentionedTypeParameters(pin.ParamTypeFqn, names);
            if (!mentioned.Overlaps(unpinned))
            {
                continue;
            }

            pins.Add(pin);
            unpinned.ExceptWith(mentioned);
        }

        return pins.Count != 0 && unpinned.Count == 0 ? pins : null;
    }

    // Which of `names` appear as a whole identifier in a fully-qualified type string — so
    // `IEnumerable<TItem>` mentions TItem, and `IEnumerable<TItemKind>` does not.
    private static HashSet<string> MentionedTypeParameters(string typeFqn, HashSet<string> names)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var from = 0;
            while (from <= typeFqn.Length - name.Length)
            {
                var at = typeFqn.IndexOf(name, from, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                var beforeOk = at == 0 || !IsIdentifierChar(typeFqn[at - 1]);
                var afterAt = at + name.Length;
                var afterOk = afterAt == typeFqn.Length || !IsIdentifierChar(typeFqn[afterAt]);
                if (beforeOk && afterOk)
                {
                    found.Add(name);
                    break;
                }

                from = at + 1;
            }
        }

        return found;
    }

    private readonly record struct EntryInference(
        string ParamName,
        string ParamTypeFqn,
        string PropertyName,
        bool Track,
        int PendingBit,
        string Summary = "");

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
                     .GroupBy(static c => c.TypeName, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                result.Add(members[0]);
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

    private static void ReportEntryCollision(SourceProductionContext spc, List<Candidate> members)
    {
        var names = string.Join("', '", members.Select(static c => c.FullyQualifiedName));
        foreach (var c in members)
        {
            spc.ReportDiagnostic(Diagnostic.Create(Rask040, MakeDeclLocation(c), c.TypeName, names));
        }
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
    }

    private static void EmitBoundEntry(
        StringBuilder sb, Candidate c, HashSet<string> taken, string visibility, string indent,
        string assemblyName, string hostTypeParameters = "", string runtimePrefix = "")
    {
        // Nothing to infer the type arguments from, no `new T()` to build, or a name already taken: no
        // entry — the factory stays the way in.
        if (!CanHaveEntry(c, taken) || InferencePins(c) is not { } pins)
        {
            return;
        }

        // A method's type parameter may not reuse an enclosing type's name (CS0693), and the consumer
        // entries are injected INTO components — including generic ones (`BsDataGrid<T>` hosting the
        // entry for `Input<T>`). Rename only the colliding ones, so the common case reads unchanged.
        var typeParameters = c.TypeParameters;
        var constraints = c.TypeParameterConstraints;
        var self = c.FullyQualifiedName;
        var paramTypes = pins.Select(static p => p.ParamTypeFqn).ToList();
        var reserved = ParseTypeParameters(hostTypeParameters);
        if (reserved.Count != 0)
        {
            foreach (var name in ParseTypeParameters(c.TypeParameters))
            {
                if (!reserved.Contains(name))
                {
                    continue;
                }

                var renamed = name;
                do
                {
                    renamed += "_";
                }
                while (reserved.Contains(renamed));

                typeParameters = RenameTypeParameter(typeParameters, name, renamed);
                constraints = RenameTypeParameter(constraints, name, renamed);
                self = RenameTypeParameter(self, name, renamed);
                for (var i = 0; i < paramTypes.Count; i++)
                {
                    paramTypes[i] = RenameTypeParameter(paramTypes[i], name, renamed);
                }
            }
        }

        // Inference mode: one parameter per pin, in the order that reads like the factory call it
        // replaces. Each property is assigned AFTER the entry returns, which is after the entry's reset
        // has run — the same ordering a setter in the chain gets, because this IS the chain's first link.
        sb.Append(indent).Append(visibility).Append(" static ").Append(self).Append(' ')
            .Append(EscapeIdentifier(c.TypeName)).Append(typeParameters).Append('(');
        for (var i = 0; i < pins.Count; i++)
        {
            sb.Append(i == 0 ? string.Empty : ", ").Append(paramTypes[i]).Append(' ')
                .Append(EscapeIdentifier(pins[i].ParamName));
        }

        sb.Append(')').Append(constraints).AppendLine();
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).Append("    var __c = ").Append(runtimePrefix).Append(EntryMethod(c)).Append(self)
            .Append(">(");
        EmitResetArguments(sb, c, assemblyName, typeParameters);
        sb.AppendLine(");");

        // Exactly what each property's own setter would do, so the entry and a later `.Prop(x)` cannot
        // disagree: fold the change into propsChanged when the prop folds, and clear its pending bit so
        // the deferred reset does not put back the value the entry just set. A bound-mode `Bind` needs
        // neither — it never folds and is reset eagerly — so that pin degenerates to the bare assignment
        // that path always had.
        foreach (var pin in pins)
        {
            var param = EscapeIdentifier(pin.ParamName);
            if (pin.Track)
            {
                sb.Append(indent).Append("    global::Rask.Core.BuilderRuntime.Track(__c, __c.")
                    .Append(EscapeIdentifier(pin.PropertyName)).Append(", ").Append(param).AppendLine(");");
            }

            if (pin.PendingBit >= 0)
            {
                sb.Append(indent).Append("    global::Rask.Core.BuilderRuntime.Written(__c, ")
                    .Append(MaskLiteral(new[] { pin.PendingBit })).AppendLine(");");
            }

            sb.Append(indent).Append("    __c.").Append(EscapeIdentifier(pin.PropertyName)).Append(" = ")
                .Append(param).AppendLine(";");
        }

        sb.Append(indent).AppendLine("    return __c;");
        sb.Append(indent).AppendLine("}");

        // Plain / controlled mode: nothing to infer from, so the caller writes the type argument
        // (`Input<string>().Value(v).Change(h)`). This is the method form of the property entry every
        // non-generic component gets.
        sb.Append(indent).Append(visibility).Append(" static ").Append(self).Append(' ')
            .Append(EscapeIdentifier(c.TypeName)).Append(typeParameters).Append("()").Append(constraints)
            .AppendLine();
        sb.Append(indent).Append("    => ").Append(runtimePrefix)
            .Append(EntryMethod(c)).Append(self).Append(">(");
        EmitResetArguments(sb, c, assemblyName, typeParameters);
        sb.AppendLine(");");
    }

    // "<TValue, TItem>" → { "TValue", "TItem" }; empty for a non-generic type.
    private static HashSet<string> ParseTypeParameters(string list)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (list.Length < 3)
        {
            return result;
        }

        foreach (var name in list.Substring(1, list.Length - 2).Split(','))
        {
            var trimmed = name.Trim();
            if (trimmed.Length != 0)
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    // Whole-identifier replace. Safe on the strings it is used with: every type name in them is
    // `global::`-qualified, so a bare identifier token can only be a type parameter.
    private static string RenameTypeParameter(string text, string from, string to)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length;)
        {
            if (string.CompareOrdinal(text, i, from, 0, from.Length) == 0
                && !IsIdentifierChar(i > 0 ? text[i - 1] : ' ')
                && !IsIdentifierChar(i + from.Length < text.Length ? text[i + from.Length] : ' '))
            {
                sb.Append(to);
                i += from.Length;
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

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
        if (refs.Count == 0 && frameworkRefs.Count == 0
                            && !hosts.Any(static h => h.Delivery == Delivery.Base))
        {
            return;
        }

        var sb = new StringBuilder();
        EmitGeneratedFileHeader(sb);

        foreach (var host2 in hosts)
        {
            // A nested host CAN be injected into — the generated file just has to re-open every enclosing
            // type as a partial around it. That is only possible if the author declared them partial, and
            // it stopped being optional when the tag family moved to Rask.Html: entries used to reach a
            // nested component by INHERITANCE from RaskMarkup, where nesting is irrelevant, and a
            // referenced library's can only be injected. Silently skipping now means a nested component
            // silently loses the chain, so the skip reports instead — the same RASK036 a non-partial
            // top-level host gets, naming the enclosing type that has to change.
            if (host2.IsNested && !host2.EnclosingAllPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask036, MakeDeclLocation(host2), host2.TypeName, Rask036Loses(host2.Delivery)));
                continue;
            }

            if (!host2.IsPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Rask036, MakeDeclLocation(host2), host2.TypeName, Rask036Loses(host2.Delivery)));
                continue;
            }

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
            var declared = new HashSet<string>(host2.MemberNames, StringComparer.Ordinal);
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

                EmitEntryForwarder(sb, e, host2.TypeParameters);
            }

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

        spc.AddSource("RaskBuilderConsumerEntries.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    // What a non-partial host loses, which is not the same for every host: an inheriting one still has
    // the framework tags and loses only the injected half, while an attributed one that cannot inherit
    // loses the entire surface — including the base the generated partial would have given it.
    private static string Rask036Loses(Delivery delivery) =>
        delivery == Delivery.Inherited
            ? "the builder entries for this project's and its referenced libraries' components"
            : "the builder surface — the framework tags as well as this project's and its referenced "
              + "libraries' components";

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
        foreach (var e in framework)
        {
            if (!taken.Contains(e.Name))
            {
                result.Add(e);
            }
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
        sb.AppendLine("///     This assembly's builder entries, one per component. Every component's own entry");
        sb.AppendLine("///     member — here and in any assembly that references this one — forwards to these, so");
        sb.AppendLine("///     the per-component injection stays one line. Global namespace, like the setters:");
        sb.AppendLine("///     a referencing assembly must be able to name it with no `using`.");
        sb.AppendLine("/// </summary>");
        sb.Append("public static class ").AppendLine(EntryHostName(host.AssemblyName));
        sb.AppendLine("{");

        var seeded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in entries)
        {
            var visibility = c.IsPublic ? "public" : "internal";
            if (NeedsSeed(c))
            {
                // One seed property per component NAME; the steps that turn it into a component are
                // emitted below, as extensions.
                if (seeded.Add(c.TypeName))
                {
                    EmitEntryDoc(sb, c);
                    sb.Append("    ").Append(visibility).Append(" static ").Append(SeedFqn(c)).Append(' ')
                        .Append(EscapeIdentifier(c.TypeName)).AppendLine(" => default;");
                }

                continue;
            }

            // The entry opens a chain, so it hands back `Build<TComponent>` rather than the component:
            // the steps that follow are extension methods on the chain, which is what keeps a
            // delegate-typed property from swallowing its own setter (see Rask.Core.Build{T}).
            EmitEntryDoc(sb, c);
            sb.Append("    ").Append(visibility).Append(" static ").Append(BuildOf(c.FullyQualifiedName))
                .Append(' ').Append(EscapeIdentifier(c.TypeName)).Append(" => new(").Append(runtime)
                .Append(EntryMethod(c))
                .Append(c.FullyQualifiedName).Append(">(");
            EmitResetArguments(sb, c, host.AssemblyName);
            sb.AppendLine("));");
        }

        sb.AppendLine("}");
        EmitSeeds(sb, entries, host.AssemblyName, runtime);
        spc.AddSource("RaskBuilderEntryHost.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string EntryHostName(string sanitizedAssemblyName) => "RaskEntries" + sanitizedAssemblyName;

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
            if (NeedsSeed(c) && !seeded.Add(c.TypeName))
            {
                continue;
            }

            refs.Add(new EntryRef(
                hostFqn,
                c.TypeName,
                NeedsSeed(c) ? SeedFqn(c) : BuildOf(c.FullyQualifiedName),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty));
        }

        return refs;
    }

    // Which referenced-assembly entries this compilation can actually inject.
    //
    //  * a name Component already carries (its own members, plus every tag entry Rask.Core emitted) would
    //    HIDE that member — CS0108, an error here — and the inherited entry is the better one anyway;
    //  * a name one of this assembly's own components already claims stays with the local component: it
    //    is the one the author wrote, and two members cannot share a name (CS0102);
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
    private static void EmitEntryForwarder(StringBuilder sb, EntryRef e, string hostTypeParameters)
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

            var renamed = name;
            do
            {
                renamed += "_";
            }
            while (reserved.Contains(renamed));

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
        sb.Append("    private static ").Append(returnType).Append(' ').Append(EscapeIdentifier(e.Name))
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
                        into.Add(new EntryRef(hostFqn, p.Name, p.Type.ToDisplayString(FullyQualifiedNullable),
                            string.Empty, string.Empty, string.Empty, string.Empty));
                        break;
                    case IMethodSymbol { MethodKind: MethodKind.Ordinary } m:
                        into.Add(ExternalMethodEntry(hostFqn, m));
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

    private static EntryRef ExternalMethodEntry(string hostFqn, IMethodSymbol m)
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
            parameters.Append(p.Type.ToDisplayString(FullyQualifiedNullable)).Append(' ')
                .Append(EscapeIdentifier(p.Name));
            arguments.Append(EscapeIdentifier(p.Name));
        }

        return new EntryRef(
            hostFqn,
            m.Name,
            m.ReturnType.ToDisplayString(FullyQualifiedNullable),
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
        NeedsDiEntry(c) ? "EntryDi<" : HasRequiredMember(c) ? "EntryRequired<" : "Entry<";

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

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<EntryHostDecl>(all.Count);
        foreach (var h in all.OrderBy(static h => h.Key, StringComparer.Ordinal))
        {
            if (seen.Add(h.Key))
            {
                result.Add(h);
            }
        }

        return result;
    }

    // An injection host that is not a candidate, read purely as a host. Deliberately none of Candidate's
    // construction facts (constructors, props, form-control shape): nothing here is ever built.
    //
    // Two shapes qualify, and the difference is which of them can be CONSTRUCTED, not which can host:
    //
    //  * an abstract component — a concrete one is already a candidate and gets its host decl from there;
    //  * anything deriving from RaskMarkup that is not a Component, abstract or not. That is the opt-in
    //    for code outside a component: a test class writes `: RaskMarkup` and the framework entries come
    //    by inheritance, while its own assembly's and its referenced libraries' come from here;
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
        EquatableArray<string> MemberNames);

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

    private static Candidate? GetCandidate(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl)
        {
            return null;
        }

        if (ctx.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol symbol)
        {
            return null;
        }

        if (symbol.IsAbstract)
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

        if (!InheritsFromComponent(symbol))
        {
            return null;
        }

        if (IsInRaskCoreNamespace(symbol))
        {
            return null;
        }

        if (HasSkipFactoryAttribute(symbol))
        {
            return null;
        }

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        var hasParameterlessCtor = HasPublicParameterlessConstructor(symbol);
        var hasDICtor = HasDIConstructor(symbol);
        var isPublic = IsExternallyVisible(symbol);
        var formControl = GetFormControlInfo(symbol);
        var properties = GetFactoryProperties(symbol, formControl is not null, ctx.SemanticModel.Compilation);
        var typeParams = symbol.IsGenericType
            ? "<" + string.Join(", ", symbol.TypeParameters.Select(tp => tp.Name)) + ">"
            : string.Empty;
        var constraints = BuildConstraintsClause(symbol.TypeParameters);
        GenericFactoryConfig? genericFactory = null;
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == FactoryGenericFullName)
            {
                genericFactory = ParseGenericFactoryConfig(attr);
            }
        }

        var forwarders = GetForwarderInfos(symbol);
        return new Candidate(
            ns,
            symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            typeParams,
            constraints,
            hasParameterlessCtor,
            hasDICtor,
            isPublic,
            genericFactory,
            formControl,
            new EquatableArray<PropInfo>(properties),
            new EquatableArray<ForwarderInfo>(forwarders),
            classDecl.Modifiers.Any(SyntaxKind.PartialKeyword),
            symbol.ContainingType is not null,
            InheritsFromElement(symbol),
            OverridesLifecycleHook(symbol),
            classDecl.Identifier.GetLocation().SourceTree?.FilePath ?? string.Empty,
            classDecl.Identifier.Span.Start,
            classDecl.Identifier.Span.Length,
            SummaryOf(symbol),
            ReachableMemberNames(symbol),
            EnclosingTypeHeaders(symbol),
            AllEnclosingPartial(symbol));
    }

    // Detects IFormControl<T> among the component's implemented interfaces and returns the bound value
    // type T (fully qualified). Null when the component is not a form control.
    private static FormControlInfo? GetFormControlInfo(INamedTypeSymbol symbol)
    {
        foreach (var i in symbol.AllInterfaces)
        {
            if (i.TypeArguments.Length == 1 &&
                i.OriginalDefinition.ToDisplayString() == FormControlOpenFullName)
            {
                var valueType = i.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                              | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
                return new FormControlInfo(valueType);
            }
        }

        return null;
    }

    private static List<ForwarderInfo> GetForwarderInfos(INamedTypeSymbol symbol)
    {
        var result = new List<ForwarderInfo>();
        foreach (var member in symbol.GetMembers())
        {
            if (member is not IMethodSymbol method)
            {
                continue;
            }

            if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var hasAttr = false;
            string? validatorParam = null;
            foreach (var attr in method.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() == GenerateForwarderFactoryFullName)
                {
                    hasAttr = true;
                    foreach (var named in attr.NamedArguments)
                    {
                        if (named.Key == "Validator" && named.Value.Value is string v && v.Length > 0)
                        {
                            validatorParam = v;
                        }
                    }

                    break;
                }
            }

            if (!hasAttr)
            {
                continue;
            }

            // The validator fan-out types its sync/async overloads as Validate<T>/ValidateAsync<T>, where T
            // is the bound value type — derived from the `Expression<Func<T>>` Bind parameter. That's the
            // method's own type parameter for Input.Bound<TProp> (Expression<Func<TProp>>) and a concrete
            // constructed type for MultiSelect.Bound (Expression<Func<ICollection<TItem>>>).
            var validatorTypeArg = ExtractBoundValueType(method);

            var typeParams = method.TypeParameters.Length > 0
                ? "<" + string.Join(", ", method.TypeParameters.Select(tp => tp.Name)) + ">"
                : string.Empty;
            var constraints = BuildConstraintsClause(method.TypeParameters);

            var parameters = new List<ForwarderParamInfo>();
            foreach (var p in method.Parameters)
            {
                var typeFqn = p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                              | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
                var defaultLiteral = string.Empty;
                if (p.HasExplicitDefaultValue)
                {
                    defaultLiteral = TryGetDefaultLiteralFromSyntax(p) ?? FormatDefaultLiteral(p.ExplicitDefaultValue);
                }

                parameters.Add(new ForwarderParamInfo(typeFqn, p.Name, defaultLiteral, p.IsParams));
            }

            result.Add(new ForwarderInfo(
                method.Name,
                typeParams,
                constraints,
                new EquatableArray<ForwarderParamInfo>(parameters),
                validatorTypeArg.Length > 0 ? validatorParam : null,
                validatorTypeArg));
        }

        return result;
    }

    private static string? TryGetDefaultLiteralFromSyntax(IParameterSymbol p)
    {
        if (p.DeclaringSyntaxReferences.Length == 0)
        {
            return null;
        }

        if (p.DeclaringSyntaxReferences[0].GetSyntax() is not ParameterSyntax syntax)
        {
            return null;
        }

        var value = syntax.Default?.Value;
        return value?.ToString();
    }

    private static string FormatDefaultLiteral(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return value switch
        {
            bool b => b ? "true" : "false",
            string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            char c => "'" + c + "'",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "default"
        };
    }

    private static GenericFactoryConfig? ParseGenericFactoryConfig(AttributeData attr)
    {
        if (attr.ConstructorArguments.Length == 0
            || attr.ConstructorArguments[0].Value is not string typeParameter
            || typeParameter.Length == 0)
        {
            return null;
        }

        var modelProperty = string.Empty;
        var constraint = "class";
        var typedDelegates = Array.Empty<string>();
        var typedValidators = Array.Empty<string>();
        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "ModelProperty":
                    if (named.Value.Value is string mp)
                    {
                        modelProperty = mp;
                    }

                    break;
                case "Constraint":
                    if (named.Value.Value is string ct && ct.Length > 0)
                    {
                        constraint = ct;
                    }

                    break;
                case "TypedDelegateProperties":
                    if (!named.Value.IsNull)
                    {
                        typedDelegates = named.Value.Values
                            .Select(v => v.Value as string)
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Select(s => s!)
                            .ToArray();
                    }

                    break;
                case "TypedValidatorProperties":
                    if (!named.Value.IsNull)
                    {
                        typedValidators = named.Value.Values
                            .Select(v => v.Value as string)
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Select(s => s!)
                            .ToArray();
                    }

                    break;
            }
        }

        return new GenericFactoryConfig(
            typeParameter,
            modelProperty,
            new EquatableArray<string>(typedDelegates),
            new EquatableArray<string>(typedValidators),
            constraint);
    }

    // Finds the bound value type T from a method's `Expression<Func<T>>` parameter (the Bind expression),
    // fully qualified. Returns "" when no such parameter exists. Used to type the validator fan-out's
    // Validate<T>/ValidateAsync<T> overloads.
    private static string ExtractBoundValueType(IMethodSymbol method)
    {
        foreach (var p in method.Parameters)
        {
            if (p.Type is INamedTypeSymbol { Name: "Expression", TypeArguments.Length: 1 } expr
                && expr.TypeArguments[0] is INamedTypeSymbol { Name: "Func", TypeArguments.Length: 1 } func)
            {
                return func.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                              | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));
            }
        }

        return string.Empty;
    }

    // Merges two `<...>` type-parameter lists into one. Either may be empty; when both are present (a
    // generic method on a generic component) they're concatenated: "<A>" + "<B>" → "<A, B>".
    private static string MergeTypeParams(string a, string b)
    {
        if (a.Length == 0)
        {
            return b;
        }

        if (b.Length == 0)
        {
            return a;
        }

        return a.Substring(0, a.Length - 1) + ", " + b.Substring(1);
    }

    private static string BuildConstraintsClause(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        if (typeParameters.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var tp in typeParameters)
        {
            var clauses = new List<string>();
            if (tp.HasReferenceTypeConstraint)
            {
                clauses.Add("class");
            }

            if (tp.HasValueTypeConstraint && !tp.HasUnmanagedTypeConstraint)
            {
                clauses.Add("struct");
            }

            if (tp.HasUnmanagedTypeConstraint)
            {
                clauses.Add("unmanaged");
            }

            if (tp.HasNotNullConstraint)
            {
                clauses.Add("notnull");
            }

            foreach (var ct in tp.ConstraintTypes)
            {
                clauses.Add(ct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            if (tp.HasConstructorConstraint)
            {
                clauses.Add("new()");
            }

            if (clauses.Count == 0)
            {
                continue;
            }

            sb.Append(" where ").Append(tp.Name).Append(" : ").Append(string.Join(", ", clauses));
        }

        return sb.ToString();
    }

    private static bool IsExternallyVisible(INamedTypeSymbol symbol)
    {
        for (var t = symbol; t is not null; t = t.ContainingType)
        {
            if (t.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    // Rask.Core.RaskMarkup is the builder surface and nothing else — Component's own base. A type that
    // names it DIRECTLY is a markup host: it wants to name markup without being a component, which is
    // what a test class, a fixture or a demo factory is.
    //
    // Directly, not transitively, and that is not a detail. A shared test base that derives from
    // RaskMarkup passes the framework entries down to every subclass by ordinary inheritance — but if
    // each of those subclasses were a host too, every one of them would need 'partial' (RASK036) the day
    // the base was changed, in files that name no markup at all. One edit to a base is not allowed to
    // become an error in fourteen untouched files. Injection is the expensive, opt-in half of the
    // surface, so it follows the declaration that opted in.
    private static bool DeclaresRaskMarkup(INamedTypeSymbol symbol) =>
        symbol.BaseType?.OriginalDefinition.ToDisplayString() == RaskMarkupFullName;

    // The attribute form of the same opt-in, for a type that cannot spend its base slot. Direct by
    // construction and not merely by policy: GetAttributes() returns what was written on THIS type's
    // declarations, never what a base carries — so the contagion the base-class form had to rule out by
    // hand cannot arise here at all.
    private static bool HasRaskMarkupAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == RaskMarkupAttributeFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsFromComponent(INamedTypeSymbol symbol)
    {
        for (var t = symbol.BaseType; t is not null; t = t.BaseType)
        {
            var name = t.OriginalDefinition.ToDisplayString();
            if (name == ComponentFullName)
            {
                return true;
            }
        }

        return false;
    }

    // Does this level of a component's inheritance chain belong to the SHARED builder surface — the
    // props emitted once as constrained generic extensions (GetSetterHost) instead of per component?
    // That is exactly Rask.Core's Element/Component chain, which GetSetterHost walks from Element up to
    // object; the two must agree, or a prop is either emitted twice or not at all. Every other base a
    // component inherits from — HtmlMediaElement, BsBlock, BsFormControl<T>, a consumer's own base —
    // has no shared emission, so its props need a per-component setter or they get none.
    private static bool IsSharedSurfaceType(ITypeSymbol type)
    {
        var name = type.OriginalDefinition.ToDisplayString();
        return name == ElementFullName || name == ComponentFullName;
    }

    /// <summary>
    ///     Whether the component overrides any of <c>Component</c>'s own lifecycle hooks.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is what a builder entry hands to <c>Entry&lt;T&gt;</c> so the runtime knows whether the
    ///         child has anything to run when the deferred commit reaches it. A component that does gets
    ///         its <c>LiveState</c> claimed at build time, because the commit uses "no LiveState" to mean
    ///         "not mine to notify" and a handle-less render leaves one unallocated; a component that does
    ///         not is left alone, which is what keeps a page of plain tags from paying a LiveState apiece.
    ///     </para>
    ///     <para>
    ///         The hook set is read off the <c>Component</c> symbol rather than hard-coded — every virtual
    ///         <c>On*</c> it declares — so adding a hook to the framework cannot silently leave a component
    ///         uncommitted. <c>Element</c>-derived types are NOT exempt: <c>NavLink</c> is an Element and
    ///         overrides <c>OnMount</c>.
    ///     </para>
    /// </remarks>
    private static bool OverridesLifecycleHook(INamedTypeSymbol symbol)
    {
        INamedTypeSymbol? componentType = null;
        for (var t = symbol; t is not null; t = t.BaseType)
        {
            if (t.OriginalDefinition.ToDisplayString() == ComponentFullName)
            {
                componentType = t;
                break;
            }
        }

        // Not a component at all, or a shape this walk cannot see the base of: assume it has a lifecycle,
        // because the cost of being wrong that way is one allocation and the cost of the other way is a
        // component that never mounts.
        if (componentType is null)
        {
            return true;
        }

        var hooks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in componentType.GetMembers())
        {
            if (member is IMethodSymbol { IsVirtual: true } m && m.Name.StartsWith("On", StringComparison.Ordinal))
            {
                hooks.Add(m.Name);
            }
        }

        for (var t = symbol; t is not null && !SymbolEqualityComparer.Default.Equals(t, componentType); t = t.BaseType)
        {
            foreach (var member in t.GetMembers())
            {
                if (member is IMethodSymbol { IsOverride: true } m && hooks.Contains(m.Name))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool InheritsFromElement(INamedTypeSymbol symbol)
    {
        for (var t = symbol; t is not null; t = t.BaseType)
        {
            if (t.OriginalDefinition.ToDisplayString() == ElementFullName)
            {
                return true;
            }
        }

        return false;
    }

    // An event-callback delegate prop whose invocation should re-render its owner: an
    // Action/Action<T>/Func<Task>/Func<T,Task> shape (void- or Task-returning, arity <= 1). The
    // return-type rule excludes template/data delegates — Func<…,Component> (ErrorBoundary.Fallback),
    // Func<…,ValueTask<…>> (VirtualizeModel.ItemsProvider), Func<…,IEnumerable<…>>
    // (validators) — so only true parent→child callbacks are wrapped.
    private static bool IsAutoRerenderDelegate(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || named.TypeKind != TypeKind.Delegate)
        {
            return false;
        }

        var invoke = named.DelegateInvokeMethod;
        if (invoke is null || invoke.Parameters.Length > 1)
        {
            return false;
        }

        var ret = invoke.ReturnType;
        if (ret.SpecialType == SpecialType.System_Void)
        {
            return true;
        }

        return ret.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
               == "global::System.Threading.Tasks.Task";
    }

    // The same question asked of a PROPERTY: lift a `Nullable<>` first, then ask the delegate.
    //
    // This used to unwrap a carrier as well — a callback property was a struct holding its delegate, so
    // without the unwrap every one of them looked like a plain value type and silently lost its
    // auto-rerender wrapping. Properties are delegates again, so the lift is all that is left.
    private static bool IsAutoRerenderProp(ITypeSymbol type) =>
        IsAutoRerenderDelegate(
            type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } lifted
                ? lifted.TypeArguments[0]
                : type);

    private static bool IsInRaskCoreNamespace(INamedTypeSymbol symbol)
    {
        var ns = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        // Rask.Core itself (Component base, Text/Raw) and the Live runtime are excluded —
        // they are not user-facing tag wrappers. Rask.Core.Components is intentionally NOT
        // excluded: that is where the HTML tag wrappers live, and the generator now emits
        // their factories the same way it does for user components.
        if (ns == "Rask.Core")
        {
            return true;
        }

        if (ns == "Rask.Core.Live" || ns.StartsWith("Rask.Core.Live.", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool HasSkipFactoryAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() == SkipFactoryFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol symbol)
    {
        foreach (var ctor in symbol.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDIConstructor(INamedTypeSymbol symbol)
    {
        foreach (var ctor in symbol.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static List<PropInfo> GetFactoryProperties(INamedTypeSymbol symbol, bool isFormControl,
        Compilation compilation)
    {
        var result = new List<PropInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Element subclasses (Button, Input, Form, …) forward their delegate props straight onto a
        // DOM element, where handler-owner resolution already re-renders the parent for free. Only
        // plain (non-Element) components host parent↔child callbacks that need auto-wrapping; this
        // also keeps the render hot path (and the CounterAllocationPin) free of wrapper closures.
        var isElement = InheritsFromElement(symbol);

        // Walk the inheritance chain (most-derived first). Properties on a derived type
        // shadow same-name properties on a base — the `seen` set enforces "first wins" so
        // user shadows beat Component's defaults. `depth` records each property's distance
        // from the most-derived type so the final sort can keep derived-class properties
        // ahead of inherited ones (tag-specific first, then Id/Class/Style/Data). The
        // Children property is filtered out below — it's reached via the indexer, not a
        // factory parameter.
        var depth = 0;
        for (var current = symbol;
             current is not null && current.SpecialType != SpecialType.System_Object;
             current = current.BaseType, depth++)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop)
                {
                    continue;
                }

                if (!seen.Add(prop.Name))
                {
                    // Shadowed by a more-derived declaration we already visited.
                    continue;
                }

                if (prop.IsStatic || prop.IsIndexer || prop.IsImplicitlyDeclared)
                {
                    continue;
                }

                if (prop.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (prop.SetMethod is null)
                {
                    continue;
                }

                if (prop.SetMethod.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (HasSkipFactoryAttribute(prop))
                {
                    continue;
                }

                if (IsOverrideOfRaskCoreMember(prop))
                {
                    continue;
                }

                // Children is exposed via the `Component this[params Component[]]` indexer, not as
                // a factory parameter. Skip any property that matches the standard Children shape
                // so subclasses can't accidentally bring it back into the factory signature.
                if (prop.Name == "Children" && IsChildCollectionType(
                        prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                            .WithMiscellaneousOptions(
                                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                | SymbolDisplayMiscellaneousOptions.UseSpecialTypes))))
                {
                    continue;
                }

                var filePath = string.Empty;
                var spanStart = 0;
                var spanLength = 0;
                var hasInitializer = false;
                // A constant member initializer (`= "x"`, `= BsColor.Danger`) becomes the factory
                // param's DEFAULT value instead of excluding the property; non-constant initializers
                // (`= new List<>()`) stay excluded. Formatted for the generated file (no usings there).
                // Restricted to a regular `set` accessor: an `init`-only property cannot be reassigned
                // post-construction, and the factory reassigns every param on the reused persisted-
                // component path (`__c.Prop = prop;`), so promoting an init-only initializer to a param
                // would emit code that fails CS8852. Init-only-with-initializer stays excluded (as before).
                var isInitOnly = prop.SetMethod.IsInitOnly;
                string? initializerDefault = null;
                if (prop.DeclaringSyntaxReferences.Length > 0)
                {
                    var syntaxRef = prop.DeclaringSyntaxReferences[0];
                    filePath = syntaxRef.SyntaxTree.FilePath ?? string.Empty;
                    spanStart = syntaxRef.Span.Start;
                    spanLength = syntaxRef.Span.Length;
                    if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax pds)
                    {
                        hasInitializer = pds.Initializer is not null;
                        if (pds.Initializer is { } init && !isInitOnly)
                        {
                            var constant = compilation.GetSemanticModel(init.SyntaxTree)
                                .GetConstantValue(init.Value);
                            if (constant.HasValue)
                                initializerDefault = FormatConstantDefault(constant.Value, prop.Type);
                        }
                    }
                }

                var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated
                                 || (prop.Type.IsValueType && prop.Type.OriginalDefinition.SpecialType ==
                                     SpecialType.System_Nullable_T);

                var typeFqn = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat
                    .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                                              | SymbolDisplayMiscellaneousOptions.UseSpecialTypes));

                // A bound-mode IFormControl<T> member (Bind/Validate/ValidateAsync/AfterBind/AfterBindAsync):
                // excluded from the controlled factory and instead emitted on the synthesized bound factory.
                var isBoundInterfaceProp = isFormControl &&
                                           Array.IndexOf(BoundInterfaceMembers, prop.Name) >= 0;

                // AfterBind/AfterBindAsync are Action<T>/Func<T,Task>-shaped, so they would qualify for
                // auto-wrapping on a non-Element control (BsInput, BsSelect, …) — but they are post-bind
                // hooks, not event callbacks, and the bound factory has always assigned them raw. Excluding
                // every bound member here keeps the builder setters on the same rule.
                // An Element subclass forwards its delegate props straight to the DOM, where
                // handler-owner resolution already repaints and a wrap would only add a hot-path closure
                // — so they are assigned verbatim (DelegatePropOnElementSubclass_IsNotWrapped).
                //
                // [AutoCallback] is the exception, and it exists because Form needs one: its submit
                // handlers are NOT dispatched by the DOM, they are invoked by Form's own submit bridge
                // after validation, so nothing else would repaint the component that owns them. That is
                // what [FactoryGeneric]'s TypedDelegateProperties used to say, on a component that is no
                // longer generic-by-factory.
                var isAutoRerenderDelegate =
                    (!isElement || HasAutoCallbackAttribute(prop))
                    && !isBoundInterfaceProp && IsAutoRerenderProp(prop.Type);

                // Any delegate-typed prop (event callbacks: Callback/CallbackAsync/Action/Func) is
                // excluded from the propsChanged fold below — two delegates/closures are practically never
                // equal, so folding them forces propsChanged: true every render (defeating the render
                // cache) AND emits per-prop snapshot+compare bookkeeping that scales with the count. The
                // universal GlobalEventHandlers surface adds ~50 delegate props to every element factory,
                // so this is load-bearing for the render-hotpath allocation pin. Distinct from
                // isAutoRerenderDelegate, which ALSO drives the parent re-render wrapping that element
                // props must NOT get.
                var isDelegate = prop.Type is INamedTypeSymbol { TypeKind: TypeKind.Delegate };

                // An unconstrained type-parameter prop (e.g. `TValue? Value`) can't default to `null` —
                // there's no conversion from null to T — so its optional factory param must default to
                // `default`. (A `class`/`notnull`-constrained T would accept null, but `default` is always
                // valid, so key off the type-parameter kind rather than the constraint set.)
                var isTypeParameter = prop.Type is ITypeParameterSymbol;

                result.Add(new PropInfo(
                    prop.Name,
                    typeFqn,
                    isNullable,
                    hasInitializer,
                    prop.IsRequired,
                    depth,
                    filePath,
                    spanStart,
                    spanLength,
                    isAutoRerenderDelegate,
                    isTypeParameter,
                    isBoundInterfaceProp,
                    isDelegate,
                    initializerDefault,
                    prop.SetMethod?.IsInitOnly == true,
                    IsSharedSurfaceType(current),
                    HasDerivedSetter(prop),
                    SummaryOf(prop)));
            }
        }

        // Sort: (a) derived-class properties first (lowest depth), then (b) by file path
        // and span — preserves the user's declaration order within each level of the
        // inheritance chain.
        result.Sort(static (a, b) =>
        {
            var d = a.InheritanceDepth.CompareTo(b.InheritanceDepth);
            if (d != 0)
            {
                return d;
            }

            var c = string.CompareOrdinal(a.DeclaringFilePath, b.DeclaringFilePath);
            return c != 0 ? c : a.DeclaringSpanStart.CompareTo(b.DeclaringSpanStart);
        });
        return result;
    }

    private static bool IsOverrideOfRaskCoreMember(IPropertySymbol prop)
    {
        if (!prop.IsOverride)
        {
            return false;
        }

        var overridden = prop.OverriddenProperty;
        while (overridden is not null)
        {
            var ns = overridden.ContainingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (ns == "Rask.Core" || ns.StartsWith("Rask.Core.", StringComparison.Ordinal))
            {
                return true;
            }

            overridden = overridden.OverriddenProperty;
        }

        return false;
    }

    private static string DefaultLiteralFor(PropInfo p)
    {
        // A property with a constant member initializer contributes its value as the param default.
        if (p.InitializerDefault is { } init)
            return init;

        // Otherwise the optional set is exactly the nullable props (a non-nullable prop with no
        // initializer is a required factory param with no default). A type-parameter prop must use
        // `default` — `null` has no conversion to an unconstrained T.
        return p.IsNullable && !p.IsTypeParameter ? "null" : "default";
    }

    // The same rule straight off the symbol, for the shared Element/Component surface — which is
    // collected from symbols (GetSetterHost) rather than as PropInfo. A constant member initializer is
    // the value an omitted factory parameter carries, so it is what a reset has to restore.
    private static (string Literal, bool IsRequired) DefaultLiteralFor(IPropertySymbol p, Compilation compilation)
    {
        var hasInitializer = false;
        if (p.DeclaringSyntaxReferences.Length > 0
            && p.DeclaringSyntaxReferences[0].GetSyntax() is PropertyDeclarationSyntax { Initializer: { } init })
        {
            hasInitializer = true;
            if (p.SetMethod?.IsInitOnly == false)
            {
                var constant = compilation.GetSemanticModel(init.SyntaxTree).GetConstantValue(init.Value);
                if (constant.HasValue && FormatConstantDefault(constant.Value, p.Type) is { } literal)
                {
                    return (literal, false);
                }
            }
        }

        var isNullable = p.Type.NullableAnnotation == NullableAnnotation.Annotated
                         || (p.Type.IsValueType
                             && p.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);
        return (isNullable && p.Type is not ITypeParameterSymbol ? "null" : "default",
            !isNullable && !hasInitializer);
    }

    // Formats a constant initializer value as a C# default-parameter literal usable in the generated
    // file (which has no `using`s): enums cast from their underlying constant to the fully-qualified
    // type; strings/chars use escaped literals; floating/long values carry their type suffix.
    private static string? FormatConstantDefault(object? value, ITypeSymbol type)
    {
        if (value is null)
            return "null";

        var underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } n
            ? n.TypeArguments[0]
            : type;

        if (underlying.TypeKind == TypeKind.Enum)
        {
            var enumFqn = underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return "(" + enumFqn + ")" +
                   Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return value switch
        {
            string s => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, true),
            char ch => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(ch, true),
            bool b => b ? "true" : "false",
            float f => f.ToString(System.Globalization.CultureInfo.InvariantCulture) + "F",
            double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture) + "D",
            decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture) + "M",
            long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            ulong ul => ul.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    private static void ReportPropertyDiagnostics(
        SourceProductionContext spc, ImmutableArray<Candidate> candidates)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var c in candidates)
        {
            foreach (var p in c.Properties)
            {
                var location = MakeLocation(p);
                // RASK002 only fires when the chain genuinely cannot honor `required`. A DI ctor alone
                // is fine: with no parameterless ctor the entry builds via
                // ActivatorUtilities.CreateInstance (reflection bypasses the CS9035 check) and the steps
                // assign afterwards, so a required no-initializer prop IS set. The one broken shape is a
                // parameterless ctor present *and* a required prop carrying a member initializer: the
                // entry then constructs with `new T()` and the prop is excluded from what the steps can
                // set (IsParamProperty), so the consumer build hits CS9035.
                if (p.UserMarkedRequired && c.HasDIConstructor && c.HasParameterlessCtor && p.HasInitializer)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask002, location, c.FullyQualifiedName, p.Name));
                }
                else if (IsRequiredFactoryParam(p) && !p.UserMarkedRequired)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(Rask001, location, c.FullyQualifiedName, p.Name));
                }
            }
        }
    }

    private static bool IsChildCollectionType(string typeFqn)
    {
        // Strip ALL nullable annotations (the outer collection `?` and the inner `Component?`
        // element annotation) so the match holds regardless of how nullability is rendered.
        var t = typeFqn.Replace("?", "");
        return t is "global::System.Collections.Generic.IEnumerable<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.IReadOnlyList<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.IReadOnlyCollection<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.IList<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.ICollection<global::Rask.Core.Component>"
            or "global::System.Collections.Generic.List<global::Rask.Core.Component>"
            or "global::Rask.Core.Component[]";
    }

    private static string StripNullable(string typeFqn) =>
        typeFqn.EndsWith("?", StringComparison.Ordinal)
            ? typeFqn.Substring(0, typeFqn.Length - 1)
            : typeFqn;

    private static bool IsRequiredFactoryParam(PropInfo p) =>
        !p.IsNullable && !p.HasInitializer;

    private static bool IsParamProperty(PropInfo p) =>
        // A property with a constant member initializer is an optional param defaulting to that value;
        // a non-constant initializer (InitializerDefault == null) is still excluded entirely.
        !p.HasInitializer || p.InitializerDefault is not null;

    private static Location MakeLocation(PropInfo p)
    {
        if (string.IsNullOrEmpty(p.DeclaringFilePath))
        {
            return Location.None;
        }

        return Location.Create(
            p.DeclaringFilePath,
            new TextSpan(p.DeclaringSpanStart, p.DeclaringSpanLength),
            new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));
    }

    // A property/parameter name as a valid C# identifier in emitted code. ISymbol.Name strips the
    // leading '@' from a verbatim identifier (a property declared `@event` has Name "event"), so a
    // reserved keyword must be re-escaped with '@' wherever it is emitted as an identifier —
    // otherwise the generated factory (`string? event = null`, `__c.event = event`) fails to
    // compile in the consumer's build. Use this only for emitted identifiers; comparisons against
    // metadata names (modelProperty, "Children", typed-delegate sets) keep the raw Name.
    internal static string EscapeIdentifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private enum ValidatorShape { None, Sync, Async }

    private sealed record Candidate(
        string Namespace,
        string TypeName,
        string FullyQualifiedName,
        string TypeParameters,
        string TypeParameterConstraints,
        bool HasParameterlessCtor,
        bool HasDIConstructor,
        bool IsPublic,
        GenericFactoryConfig? GenericFactory,
        FormControlInfo? FormControl,
        EquatableArray<PropInfo> Properties,
        EquatableArray<ForwarderInfo> Forwarders,
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
        bool EnclosingAllPartial = false);

    // Set when a component implements IFormControl<T> — drives the synthesized bound factory and the
    // exclusion of the bound-mode interface members from the controlled factory. ValueTypeFqn is the T
    // (the validator/after-bind fan key); the bound-member names are the fixed interface member names.
    private readonly record struct FormControlInfo(string ValueTypeFqn);

    private readonly record struct GenericFactoryConfig(
        string TypeParameter,
        string ModelProperty,
        EquatableArray<string> TypedDelegateProperties,
        EquatableArray<string> TypedValidatorProperties,
        string Constraint);

    private readonly record struct ForwarderInfo(
        string MethodName,
        string TypeParameters,
        string TypeParameterConstraints,
        EquatableArray<ForwarderParamInfo> Parameters,
        string? ValidatorParam,
        string ValidatorTypeArg);

    private readonly record struct ForwarderParamInfo(
        string TypeFqn,
        string Name,
        string DefaultLiteral,
        bool IsParams);


    /// <summary>
    ///     A property's <c>&lt;summary&gt;</c>, flattened to one line, or empty when it has none.
    /// </summary>
    /// <remarks>
    ///     What a chain shows in a tooltip is the setter, not the property — so unless the summary is
    ///     carried across, hovering <c>.Placeholder(…)</c> says nothing at all while the property it
    ///     writes is fully documented.
    ///     <para>
    ///         <c>&lt;inheritdoc/&gt;</c> is followed by hand. It does NOT arrive resolved:
    ///         <c>GetDocumentationCommentXml</c> hands back the literal <c>&lt;inheritdoc/&gt;</c> element,
    ///         because resolving it is an IDE/DocFX-layer job, not a compiler one. That made every async
    ///         twin in the framework — <c>OnValidSubmitAsync</c>, <c>ValidateAsync</c>, each written as
    ///         <c>&lt;inheritdoc cref="OnValidSubmit"/&gt;</c> — emit a setter with no documentation at all,
    ///         while its sibling was fully documented and the source looked complete either way.
    ///     </para>
    /// </remarks>
    private static string SummaryOf(ISymbol symbol) => SummaryOf(symbol, depth: 0);

    private static string SummaryOf(ISymbol symbol, int depth)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml))
        {
            // No doc comment at all. An override or an interface implementation still HAS documentation as
            // far as a reader is concerned — every IDE shows the base member's — and the overwhelmingly
            // common way to write one is to add no comment rather than an explicit <inheritdoc/>. Every
            // form control lands here: Input/Select/Textarea implement IFormControl<T>.Validate, OnChange
            // and AfterBind without redeclaring their docs, so without this the interface can be documented
            // exhaustively and every control's chain still shows nothing.
            return depth < 4 && InheritedMember(symbol) is { } from
                ? SummaryOf(from, depth + 1)
                : string.Empty;
        }

        var open = xml!.IndexOf("<summary>", StringComparison.Ordinal);
        var close = xml.IndexOf("</summary>", StringComparison.Ordinal);
        if (open < 0 || close <= open)
        {
            // No summary of its own — an <inheritdoc/> stands in for one. Depth-capped because a pair of
            // members can point <inheritdoc/> at each other, and this walk has no other terminator.
            if (depth >= 4 || xml.IndexOf("<inheritdoc", StringComparison.Ordinal) < 0)
            {
                return string.Empty;
            }

            return InheritDocTarget(symbol, xml) is { } inherited
                ? SummaryOf(inherited, depth + 1)
                : string.Empty;
        }

        var text = xml.Substring(open + "<summary>".Length, close - open - "<summary>".Length);
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words);
    }

    /// <summary>
    ///     The member an <c>&lt;inheritdoc/&gt;</c> borrows its summary from, or <see langword="null" />.
    /// </summary>
    /// <remarks>
    ///     Two shapes, which is all C# offers. With a <c>cref</c>, the attribute names the member — Roslyn
    ///     writes it as a documentation-comment id (<c>P:Some.Type.Member</c>), and the member name is what
    ///     follows the last dot. Without one, the doc is inherited the way the language inherits it: from
    ///     the overridden member, else from the interface member this one implements.
    ///     <para>
    ///         A cref is resolved against the CONTAINING TYPE only. Doing it properly needs the
    ///         <c>Compilation</c> (<c>DocumentationCommentId.GetFirstSymbolForDeclarationId</c>), which this
    ///         call site does not have; every use in the framework points at a sibling, and a cross-type
    ///         cref degrades to no summary rather than to a wrong one.
    ///     </para>
    /// </remarks>
    private static ISymbol? InheritDocTarget(ISymbol symbol, string xml)
    {
        var cref = AttributeValue(xml, "cref");
        if (cref is { Length: > 0 })
        {
            var name = cref.Substring(cref.LastIndexOf('.') + 1);
            // A generic type's id carries an arity suffix on the TYPE, not the member, so the member name
            // needs no stripping — but a malformed id would, and an empty name matches nothing anyway.
            return symbol.ContainingType?.GetMembers(name).FirstOrDefault();
        }

        return InheritedMember(symbol);
    }

    /// <summary>
    ///     The member C# itself would inherit documentation from: the one this overrides, else the
    ///     interface member this implements. <see langword="null" /> when it inherits from neither.
    /// </summary>
    private static ISymbol? InheritedMember(ISymbol symbol)
    {
        if (symbol is IPropertySymbol { OverriddenProperty: { } baseProperty })
        {
            return baseProperty;
        }

        if (symbol is IMethodSymbol { OverriddenMethod: { } baseMethod })
        {
            return baseMethod;
        }

        var type = symbol.ContainingType;
        if (type is null)
        {
            return null;
        }

        // Match on the implementation rather than on the name alone: a control can declare a member that
        // merely SHARES a name with an interface member it does not implement, and borrowing docs from an
        // unrelated member is worse than having none.
        return type
            .AllInterfaces
            .SelectMany(i => i.GetMembers(symbol.Name))
            .FirstOrDefault(m => SymbolEqualityComparer.Default.Equals(
                type.FindImplementationForInterfaceMember(m), symbol));
    }

    // The value of one attribute on the first <inheritdoc …> element. A hand-rolled read rather than an
    // XML parse: the surrounding string is compiler-produced doc XML, and the generator runs per property
    // on every keystroke in the IDE.
    private static string? AttributeValue(string xml, string attribute)
    {
        var element = xml.IndexOf("<inheritdoc", StringComparison.Ordinal);
        if (element < 0)
        {
            return null;
        }

        var end = xml.IndexOf('>', element);
        var at = xml.IndexOf(attribute + "=\"", element, StringComparison.Ordinal);
        if (at < 0 || (end >= 0 && at > end))
        {
            return null;
        }

        var start = at + attribute.Length + 2;
        var quote = xml.IndexOf('"', start);
        return quote < 0 ? null : xml.Substring(start, quote - start);
    }

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
        string Summary = "")
    {
        // The factory-parameter / property identifier, '@'-escaped when Name is a reserved keyword.
        public string Escaped => EscapeIdentifier(Name);
    }
}

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[] _items;

    public EquatableArray(IEnumerable<T> items) => _items = items?.ToArray() ?? Array.Empty<T>();

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items[index];

    public bool Equals(EquatableArray<T> other)
    {
        var a = _items ?? Array.Empty<T>();
        var b = other._items ?? Array.Empty<T>();
        if (a.Length != b.Length)
        {
            return false;
        }

        for (var i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            var arr = _items ?? Array.Empty<T>();
            foreach (var item in arr)
            {
                hash = (hash * 31) + item.GetHashCode();
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        var arr = _items ?? Array.Empty<T>();
        return ((IEnumerable<T>)arr).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
