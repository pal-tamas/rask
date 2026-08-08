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

    private static readonly DiagnosticDescriptor Rask001 = new(
        "RASK001",
        "Property is treated as a required factory parameter",
        "Property '{0}.{1}' is treated as a required factory parameter; consider also marking it 'required' for language-level enforcement",
        "Rask.Generators",
        DiagnosticSeverity.Hidden,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK001"));

    private static readonly DiagnosticDescriptor Rask002 = new(
        "RASK002",
        "'required' property cannot be honored by the generated factory",
        "Property '{0}.{1}' is marked 'required', but the generated factory for '{0}' cannot set it: '{0}' has a dependency-injected constructor and the property is either excluded from the factory parameters (it has a member initializer) or only reachable via ActivatorUtilities.CreateInstance (no parameterless constructor). Adding a parameterless constructor does not help while the DI constructor remains — the factory then builds '{0}' with 'new {0}()' and the DI constructor never runs, leaving injected services null. Remove 'required', move the value to a constructor parameter (with no initializer), or drop the DI constructor.",
        "Rask.Generators",
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK002"));

    private static readonly DiagnosticDescriptor Rask036 = new(
        "RASK036",
        "Component must be partial to receive builder entries",
        "Component '{0}' is not declared 'partial', so the builder entries for the project's other components cannot be injected into it; writing another component's name unqualified in its render body will not compile. Add the 'partial' modifier.",
        "Rask.Generators",
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK036"));

    private static readonly DiagnosticDescriptor Rask037 = new(
        "RASK037",
        "Two components share a simple name, so neither can have a builder entry",
        "Components '{1}' share the simple name '{0}', so neither receives a builder entry: an entry is a single member of 'Rask.Core.Component' (or of each consuming component) named after its type, and one name can only stand for one type. The generated factories are unaffected — they live in a per-namespace 'Generated' class — so both components stay reachable through 'Generated.{0}(...)'. Rename one of them to give both an entry.",
        "Rask.Generators",
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK037"));

    private static readonly DiagnosticDescriptor Rask038 = new(
        "RASK038",
        "The builder surface's shared pending-bit budget is exhausted",
        "The shared Element/Component surface has {0} folding properties but only {1} pending bits; '{2}' and every later one (ordinal name order) fall back to the eager reset, which reports the property changed on every render and defeats the render cache for it. Raise 'BuilderRuntime.OwnPendingBit' (and the generator's copy of it) together, or make the property non-folding.",
        "Rask.Generators",
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK038"));

    private static readonly DiagnosticDescriptor Rask039 = new(
        "RASK039",
        "Delegate-typed property cannot receive a builder setter",
        "Property '{0}.{1}' is a raw delegate, so it is invocable and C#'s invocable-member rule binds '{1}(...)' to the property instead of to the same-named builder setter — the setter can never be reached. Declare it as a carrier ('{2}') instead: the carrier is not invocable, its implicit conversion keeps assignment and every generated '{1}:' factory argument working, and calling the callback back becomes '{1}?.Invoke(...)'.",
        "Rask.Generators",
        DiagnosticSeverity.Warning,
        true,
        helpLinkUri: DiagnosticHelp.Link("RASK039"));

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

        // RaskFactoryNavigation (default true) gates the per-factory `<see cref>` doc breadcrumb
        // that links each generated factory to its component type (Quick-Doc / hover navigation;
        // F12 still resolves to the generated method — Roslyn/ReSharper navigate generated symbols
        // to the generated file, so use "Navigate to Type of Symbol" for a one-action jump to the
        // component). `[DebuggerStepThrough]` is emitted unconditionally so the debugger always
        // steps over the factory into user code.
        var navigationEnabled = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            !p.GlobalOptions.TryGetValue("build_property.RaskFactoryNavigation", out var v)
            || !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase));

        context.RegisterSourceOutput(grouped.Combine(navigationEnabled),
            static (spc, t) => Emit(spc, t.Left, t.Right));

        var globalUsingsEnabled = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            !p.GlobalOptions.TryGetValue("build_property.RaskGlobalUsings", out var v)
            || !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase));

        // Satellite factory families (e.g. Rask.Native.Components) live in referenced assemblies, so their
        // components never appear in this compilation's syntax candidates. Scan the reference graph for the
        // [assembly: RaskFactoryNamespace(...)] marker and surface each hit as a global using too. Wrapped in
        // an EquatableArray so this only re-runs the emit when the marker SET changes, not on every keystroke
        // (CompilationProvider yields a fresh Compilation per edit).
        var factoryMarkerNamespaces = context.CompilationProvider.Select(
            static (c, _) => new EquatableArray<string>(ScanFactoryMarkerNamespaces(c)));

        var globalUsingsInput = grouped.Combine(globalUsingsEnabled).Combine(factoryMarkerNamespaces);
        context.RegisterSourceOutput(globalUsingsInput,
            static (spc, t) => EmitGlobalUsings(spc, t.Left.Left, t.Left.Right, t.Right));

        // Builder surface (opt-in, RaskBuilderSurface). Only the assembly that DECLARES
        // Rask.Core.Component can add entry members to it, so this emission is scoped to that
        // compilation; a consumer's own components are handled separately (they are injected into
        // the consumer's own partial class, since a generator cannot add members to a type in a
        // referenced assembly).
        var builderEnabled = context.AnalyzerConfigOptionsProvider.Select(static (p, _) =>
            p.GlobalOptions.TryGetValue("build_property.RaskBuilderSurface", out var v)
            && string.Equals(v, "true", StringComparison.OrdinalIgnoreCase));

        var componentHost = context.CompilationProvider.Select(static (c, _) => GetComponentHost(c));

        context.RegisterSourceOutput(grouped.Combine(builderEnabled).Combine(componentHost),
            static (spc, t) => EmitBuilderEntries(spc, t.Left.Left, t.Left.Right, t.Right));

        context.RegisterSourceOutput(grouped.Combine(builderEnabled).Combine(componentHost),
            static (spc, t) => EmitConsumerEntries(spc, t.Left.Left, t.Left.Right, t.Right));

        // Setters. Emitted into the GLOBAL namespace: an extension method is only found when its
        // containing namespace is in scope, and the global namespace encloses every namespace — so
        // this is what lets `Div.Class("card")` bind with no `using` anywhere. The class name carries
        // the assembly name because several assemblies each contribute one.
        var setterHost = context.CompilationProvider.Select(static (c, _) => GetSetterHost(c));

        context.RegisterSourceOutput(grouped.Combine(builderEnabled).Combine(setterHost),
            static (spc, t) => EmitBuilderSetters(spc, t.Left.Left, t.Left.Right, t.Right));
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
                    defaultLiteral,
                    string.Equals(owner, elementFqn, StringComparison.Ordinal),
                    isRequired));
            }
        }

        shared.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return new SetterHost(assembly, elementFqn, new EquatableArray<SharedSetter>(shared.ToArray()));
    }

    private static void EmitBuilderSetters(
        SourceProductionContext spc,
        ImmutableArray<Candidate> candidates,
        bool enabled,
        SetterHost host)
    {
        if (!enabled)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Builder-surface setters. Global namespace so no `using` is needed.</summary>");
        sb.Append("public static class RaskBuilderSetters").AppendLine(host.AssemblyName);
        sb.AppendLine("{");

        var sharedBits = SharedPendingBits(host);
        ReportSharedBitOverflow(spc, host, sharedBits);
        foreach (var s in host.Shared)
        {
            if (s.IsDelegate && SetterName(s.Name, s.IsDelegate) == s.Name)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask039, Location.None, s.Owner, s.Name,
                    SuggestedCarrier(s.TypeFqn)));
                continue;
            }

            EmitSetter(sb, s.Name, s.TypeFqn, s.Owner, s.IsDelegate, wrap: false, generic: true,
                fold: FoldsIntoPropsChanged(s.Name, s.TypeFqn, s.IsDelegate, autoRerender: false),
                pendingBit: Bit(sharedBits, s.Name));
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
                // A raw delegate prop is INVOCABLE, so `__c.RowClass(fn)` binds to the property and the
                // same-named setter can never be reached (CS1593 at best, a wrong-arity invocation at
                // worst). Emitting it anyway would be dead code that reads like a working surface.
                //
                // Reported only when the prop is otherwise settable through a chain. A REQUIRED delegate
                // prop (`required Func<…> Template`) has no chain to be set from at all — its component
                // is excluded from entries by BlocksEntry, and its factory assigns the prop on every
                // render — so moving it to a carrier would buy nothing and would cost its non-nullness
                // (a carrier converted from a null delegate is a non-null carrier wrapping null, where
                // the raw `required` delegate simply cannot be null).
                if (p.IsDelegate && SetterName(p.Name, p.IsDelegate) == p.Name)
                {
                    if (!IsRequiredFactoryParam(p))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(Rask039, MakeLocation(p), c.FullyQualifiedName,
                            p.Name, SuggestedCarrier(p.TypeFqn)));
                    }

                    continue;
                }

                EmitSetter(sb, p.Name, p.TypeFqn, self, p.IsDelegate, p.IsAutoRerenderDelegate, generic: false,
                    FoldsIntoPropsChanged(p.Name, p.TypeFqn, p.IsDelegate, p.IsAutoRerenderDelegate),
                    c.TypeParameters, c.TypeParameterConstraints, visibility, Bit(ownBits, p.Name));
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
    //  * EAGER — the non-folding props (raw delegates, carriers, Key). Assigned unconditionally when the
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
    private const int OwnPendingBit = 16; // mirrors Rask.Core.BuilderRuntime.OwnPendingBit

    private static int Bit(Dictionary<string, int> bits, string name) =>
        bits.TryGetValue(name, out var bit) ? bit : -1;

    private static Dictionary<string, int> SharedPendingBits(SetterHost host)
    {
        var bits = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 0;
        foreach (var s in host.Shared)
        {
            if (next < OwnPendingBit && !s.IsRequired
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
            .Where(s => !s.IsRequired && FoldsIntoPropsChanged(s.Name, s.TypeFqn, s.IsDelegate, autoRerender: false))
            .ToList();
        if (folding.Count <= OwnPendingBit)
        {
            return;
        }

        var first = folding.First(s => !bits.ContainsKey(s.Name));
        spc.ReportDiagnostic(Diagnostic.Create(Rask038, Location.None,
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

    // What the reset may put back: a prop the factory would re-apply from a parameter DEFAULT. A prop
    // with a non-constant initializer (`= new List<>()`) is not a factory parameter at all, and a
    // required one has no default — the caller has to name it every render either way.
    private static bool IsResettableProp(PropInfo p) => IsParamProperty(p) && !IsRequiredFactoryParam(p);

    private static Dictionary<string, int> OwnPendingBits(Candidate c)
    {
        var bits = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = OwnPendingBit;
        foreach (var p in OwnSetterProps(c))
        {
            if (next >= 64 || !IsResettableProp(p)
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
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core;");
        sb.AppendLine();
        sb.AppendLine("public static partial class BuilderRuntime");
        sb.AppendLine("{");

        foreach (var elementOwned in new[] { false, true })
        {
            var kind = elementOwned ? "Element" : "Component";
            var receiver = elementOwned ? host.ElementFqn : "global::Rask.Core.Component";
            // A required factory parameter has no default at all — the caller must name it every
            // render, and the factory has nothing to put back either — so it is never reset.
            var props = host.Shared.Where(s => s.IsElementOwned == elementOwned && !s.IsRequired).ToList();
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

            EmitReceiverCast(sb, receiver, "        ");
            foreach (var s in props.Where(s => !bits.ContainsKey(s.Name)))
            {
                sb.Append("        __c.").Append(EscapeIdentifier(s.Name)).Append(" = ")
                    .Append(s.DefaultLiteral).AppendLine(";");
            }

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
                    EmitPendingReset(sb, s.Name, s.TypeFqn, s.DefaultLiteral, bits[s.Name], "        ");
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
        StringBuilder sb, string name, string typeFqn, string defaultLiteral, int bit, string indent)
    {
        sb.Append(indent).Append("if ((__p & ").Append(MaskLiteral(new[] { bit }))
            .Append(") != 0UL && !global::System.Collections.Generic.EqualityComparer<").Append(typeFqn)
            .Append(">.Default.Equals(__c.").Append(EscapeIdentifier(name)).Append(", ").Append(defaultLiteral)
            .AppendLine("))");
        sb.Append(indent).AppendLine("{");
        sb.Append(indent).AppendLine("    global::Rask.Core.BuilderRuntime.MarkChanged(__c);");
        sb.Append(indent).Append("    __c.").Append(EscapeIdentifier(name)).Append(" = ")
            .Append(defaultLiteral).AppendLine(";");
        sb.Append(indent).AppendLine("}");
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
                    sb.Append("        __c.").Append(p.Escaped).Append(" = ").Append(DefaultLiteralFor(p))
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
                    EmitPendingReset(sb, p.Name, p.TypeFqn, DefaultLiteralFor(p), bits[p.Name], "        ");
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

    // The name a builder setter takes for a property. A raw delegate prop is invocable and would beat a
    // same-named extension (CS1593), so historically those setters dropped the `On` prefix; the props
    // that matter have since moved to carriers, which are not invocable and so keep their own name. A
    // delegate prop whose name the rule leaves unchanged has no reachable setter at all — RASK039.
    private static string SetterName(string name, bool isDelegate) =>
        isDelegate && name.StartsWith("On", StringComparison.Ordinal) && name.Length > 2
            ? name.Substring(2)
            : name;

    // The carrier RASK039 tells the author to declare instead: the named pair for a Callback-shaped
    // delegate, the open Carrier<TDelegate> for anything else.
    private static string SuggestedCarrier(string typeFqn)
    {
        var t = StripNullable(typeFqn);
        return t switch
        {
            "global::Rask.Core.Callback" => "global::Rask.Core.Handler?",
            "global::Rask.Core.CallbackAsync" => "global::Rask.Core.HandlerAsync?",
            _ when t.StartsWith("global::Rask.Core.Callback<", StringComparison.Ordinal) =>
                "global::Rask.Core.Handler<" + t.Substring("global::Rask.Core.Callback<".Length) + "?",
            _ when t.StartsWith("global::Rask.Core.CallbackAsync<", StringComparison.Ordinal) =>
                "global::Rask.Core.HandlerAsync<" + t.Substring("global::Rask.Core.CallbackAsync<".Length) + "?",
            _ => "global::Rask.Core.Carrier<" + t + ">?",
        };
    }

    // The bound half of an IFormControl<T> control: one setter per interface member, typed from the
    // interface's T rather than from the declaring class. That matters twice — the members may be
    // inherited from a non-Element base (BsInput<T> gets them from BsFormControl<T>, which the
    // depth-0 rule above would skip), and it is what lets the generic entry take only `Bind`:
    //
    //     Input(() => _form.Name).Validate(ProductName.Validate).Id("name")
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

        // The four delegate members are typed as they are DECLARED — `Carrier<…>?`, not the bare
        // delegate. That is what makes EmitSetter take the delegate as its parameter and assign it
        // through the carrier's `From`: written as the raw delegate the assignment would run the
        // carrier's implicit conversion instead, and a null validator or hook would land as a non-null
        // carrier wrapping null — exactly the trap `From` exists to close, reopened one layer up.
        var members = new (string Name, string TypeFqn)[]
        {
            ("Bind", "global::System.Linq.Expressions.Expression<global::System.Func<" + t + ">>?"),
            ("Validate", "global::Rask.Core.Carrier<global::Rask.Core.Forms.Validate<" + t + ">>?"),
            ("ValidateAsync", "global::Rask.Core.Carrier<global::Rask.Core.Forms.ValidateAsync<" + t + ">>?"),
            ("AfterBind", "global::Rask.Core.Carrier<global::System.Action<" + t + ">>?"),
            ("AfterBindAsync",
                "global::Rask.Core.Carrier<global::System.Func<" + t
                + ", global::System.Threading.Tasks.Task>>?"),
        };

        foreach (var (name, typeFqn) in members)
        {
            // fold: false — the bound members are a fresh expression tree / delegate every render, so
            // folding them would report propsChanged on every frame. Exactly what EmitBoundOverload's
            // foldProps does (only the shared display props participate there too).
            EmitSetter(sb, name, typeFqn, c.FullyQualifiedName, isDelegate: false, wrap: false, generic: false,
                fold: false, c.TypeParameters, c.TypeParameterConstraints, visibility);
        }
    }

    // Which props participate in the propsChanged diff, asked of a builder setter. Mirrors the
    // factory's foldProps exactly (EmitFactory / EmitBoundOverload) so both surfaces report the same
    // flag to NotifyParameters: Key is a reconciliation identity rather than a reactive prop;
    // auto-wrapped callbacks and raw delegates are a fresh closure every render, so folding them would
    // force propsChanged: true on every frame and defeat the render cache for any callback-taking
    // component; a carrier prop wraps one of those.
    private static bool FoldsIntoPropsChanged(string name, string typeFqn, bool isDelegate, bool autoRerender) =>
        !string.Equals(name, "Key", StringComparison.Ordinal)
        && !isDelegate
        && !autoRerender
        && CarrierDelegate(typeFqn) is null;

    // A delegate-typed property is invocable, so an extension of the same name loses to it (CS1593).
    // Until those props move to a non-delegate carrier, their setters drop the `On` prefix.
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
        int pendingBit = -1)
    {
        var setterName = SetterName(name, isDelegate);

        // A carrier-typed prop takes the underlying DELEGATE as its parameter, not the carrier: a method
        // group or lambda cannot reach `Handler?` / `Carrier<…>?` (that needs a delegate conversion
        // followed by a user-defined one, which C# will not chain). The carrier exists so the prop and
        // setter can share a name; the conversion back happens at the assignment.
        //
        // `wrap` is the AutoCallback decision, and it is per property, not per carrier: an Element's
        // handlers go to the DOM unwrapped (owner resolution already re-renders, and wrapping would
        // allocate per render), a non-Element component's event callbacks are wrapped, and a form
        // control's bound members (validators, post-bind hooks) are never wrapped at all.
        var carrier = CarrierDelegate(typeFqn);
        var paramType = carrier ?? typeFqn;
        // Wrap returns a nullable delegate (null in → null out); assigning it to a non-nullable prop
        // needs the null-forgiving `!` (CS8601), the same way the factory's assignment pass does it.
        var value = wrap
            ? "global::Rask.Core.AutoCallback.Wrap(value)"
              + (carrier is null && !typeFqn.EndsWith("?", StringComparison.Ordinal) ? "!" : string.Empty)
            : "value";
        // `From`, not `new Handler(…)`: an unset handler must land as a NULL carrier, or the component's
        // own `OnClose is not null` tests all start answering true — see AssignExpr. (A non-nullable
        // carrier prop cannot hold From's `Handler?`, but it is already rejected upstream as a required
        // factory parameter; the constructor stays as the fallback so the emission can't break on one.)
        var assigned = carrier is null
            ? value
            : typeFqn.EndsWith("?", StringComparison.Ordinal)
                ? StripNullable(typeFqn) + ".From(" + value + ")"
                : "new " + typeFqn + "(" + value + ")";

        // The propsChanged fold, one prop at a time. The factory can snapshot every prop, assign them
        // all and diff once, because it knows where the assignments end; a setter chain does not, so
        // each folding setter accumulates its own delta and the parent fires the single notification
        // when its Render() returns (Component.RenderForLive). Same EqualityComparer semantics, and the
        // non-folding props (Key, delegates, carriers) emit no call at all — see FoldsIntoPropsChanged.
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

        // An `internal` component cannot appear in a `public` signature (CS0050/CS0051), so the
        // setter's accessibility tracks its component's — the same rule the factory emission uses.
        sb.Append("    ").Append(visibility).Append(" static ");
        if (generic)
        {
            sb.Append("T ").Append(EscapeIdentifier(setterName)).Append("<T>(this T __c, ").Append(paramType)
                .Append(" value) where T : ").Append(receiver);
            sb.Append(" { ").Append(track).Append("__c.").Append(EscapeIdentifier(name)).Append(" = ")
                .Append(assigned).AppendLine("; return __c; }");
            return;
        }

        sb.Append(receiver).Append(' ').Append(EscapeIdentifier(setterName)).Append(typeParameters)
            .Append("(this ").Append(receiver).Append(" __c, ").Append(paramType).Append(" value)")
            .Append(constraints);
        sb.Append(" { ").Append(track).Append("__c.").Append(EscapeIdentifier(name)).Append(" = ")
            .Append(assigned).AppendLine("; return __c; }");
    }

    // Maps a carrier prop (Handler / HandlerAsync / Carrier<TDelegate>) to the delegate it carries.
    // Every generated *parameter* for such a prop is typed as the delegate, not the carrier: a lambda or
    // method group cannot reach the carrier (that would need a delegate conversion followed by a
    // user-defined one, which C# will not chain), and the implicit conversion turns it back at the
    // assignment. Null for a prop that is not a carrier.
    private static string? CarrierDelegate(string typeFqn)
    {
        var t = StripNullable(typeFqn);
        switch (t)
        {
            case "global::Rask.Core.Handler":
                return "global::Rask.Core.Callback?";
            case "global::Rask.Core.HandlerAsync":
                return "global::Rask.Core.CallbackAsync?";
        }

        if (!t.EndsWith(">", StringComparison.Ordinal))
        {
            return null;
        }

        // The argument-taking carriers name their ARGUMENT, not their delegate (Element's whole event
        // surface is declared with them), so the delegate is rebuilt around it.
        foreach (var (open, delegateOpen) in CarrierShapes)
        {
            if (t.StartsWith(open, StringComparison.Ordinal))
            {
                var inner = t.Substring(open.Length, t.Length - open.Length - 1);
                return delegateOpen is null ? inner + "?" : delegateOpen + inner + ">?";
            }
        }

        return null;
    }

    // Open generic → the delegate a carrier of that shape carries. `Carrier<TDelegate>` names the
    // delegate itself (null); the Handler pair names the argument.
    private static readonly (string Open, string? DelegateOpen)[] CarrierShapes =
    {
        ("global::Rask.Core.Carrier<", null),
        ("global::Rask.Core.HandlerAsync<", "global::Rask.Core.CallbackAsync<"),
        ("global::Rask.Core.Handler<", "global::Rask.Core.Callback<"),
    };

    // The type a generated factory parameter uses for a property: the carried delegate for a carrier
    // prop, the property's own type otherwise.
    private static string ParamType(PropInfo p) => CarrierDelegate(p.TypeFqn) ?? p.TypeFqn;

    // The expression a generated ASSIGNMENT uses to put a delegate-typed argument into a property:
    // `Handler.From(value)` for a carrier prop, the value itself otherwise.
    //
    // Never the bare implicit conversion, and never `new Handler(value)`: the conversion accepts a null
    // delegate, so an omitted argument would land as a non-NULL carrier wrapping null. A component that
    // asks about its own callback — `AutoHideMs is > 0 && OnClose is not null` (BsToast),
    // `OnSortChange is not null` (BsDataGrid), `OnChange is null && OnChangeAsync is null`
    // (BsRadioGroup) — would then answer true for a handler nobody wired. From maps null → null, so a
    // carrier prop reads back exactly as unset as the raw delegate prop it replaced. Allocation-free
    // either way (a struct, and Nullable<> of one).
    //
    // Only applied to a NULLABLE prop: the carrier of a required prop cannot hold From's `Handler?`,
    // and a non-nullable carrier is already rejected upstream (RASK001 / CS9040).
    private static string AssignExpr(PropInfo p, string valueExpr) =>
        p.IsNullable && CarrierDelegate(p.TypeFqn) is not null
            ? StripNullable(p.TypeFqn) + ".From(" + valueExpr + ")"
            : valueExpr;

    private static bool IsCarrierProp(PropInfo p) => CarrierDelegate(p.TypeFqn) is not null;

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
        bool IsRequired);

    // The names already declared on Rask.Core.Component, when THIS compilation is the one declaring it.
    // An entry whose name matches an existing member would be CS0102 ("already contains a definition"),
    // so those are skipped — `Head` is the real case: Component.Head is the head-asset contribution.
    // Empty (and NotHost) for every other compilation.
    private static ComponentHost GetComponentHost(Compilation compilation)
    {
        var assembly = SanitizeIdentifier(compilation.AssemblyName ?? "Rask");
        var component = compilation.Assembly.GetTypeByMetadataName(ComponentFullName);
        if (component is null)
        {
            return new ComponentHost(false, assembly, new EquatableArray<string>(Array.Empty<string>()));
        }

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

        return new ComponentHost(true, assembly, new EquatableArray<string>(names.ToArray()));
    }

    private static void EmitBuilderEntries(
        SourceProductionContext spc,
        ImmutableArray<Candidate> candidates,
        bool enabled,
        ComponentHost host)
    {
        if (!enabled || !host.DeclaresComponent || candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var taken = new HashSet<string>(host.MemberNames, StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Rask.Core;");
        sb.AppendLine();
        sb.AppendLine("public abstract partial class Component");
        sb.AppendLine("{");

        foreach (var c in EntryCandidates(spc, candidates, taken))
        {
            // A property may not be generic, so a generic component's entry has to be a static METHOD —
            // legal alongside its own type name by the invocable-member rule. Only a generic FORM CONTROL
            // gets one: its `Bind` argument is what infers the value type. A generic component with no
            // such argument has nothing to infer from and keeps the factory.
            if (c.TypeParameters.Length != 0)
            {
                EmitBoundEntry(sb, c, taken, c.IsPublic ? "protected" : "private protected", indent: "    ",
                    host.AssemblyName);
                continue;
            }

            // An internal component cannot surface through a `protected` member of the public
            // Component (CS0053); `private protected` keeps it to derived types in this assembly.
            sb.Append(c.IsPublic ? "    protected static " : "    private protected static ")
                .Append(c.FullyQualifiedName).Append(' ')
                .Append(EscapeIdentifier(c.TypeName)).Append(NeedsDiEntry(c) ? " => EntryDi<" : " => Entry<")
                .Append(c.FullyQualifiedName).Append(">(");
            EmitResetArguments(sb, c, host.AssemblyName);
            sb.AppendLine(");");
        }

        sb.AppendLine("}");
        spc.AddSource("RaskBuilderEntries.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
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
    //  * a `required` member — the entry's `new T()` cannot even compile (CS9040);
    //  * a RASK001-required prop (non-nullable, no member initializer). The factory makes it a required
    //    PARAMETER, so every render names it and every render re-assigns it. An entry hands back the
    //    same instance and there is nothing to reset it to, so the prop keeps LAST render's value —
    //    `Widget.Title("x")` on one render and a bare `Widget` on the next still has the title — and on
    //    the very first render it is `null!`. Both silently. Those components stay on their factory
    //    until the chain-walking required-props analyzer can enforce them at the call site.
    //
    // …and so does a name Component already declares (`Head`), which would be CS0102.
    private static bool CanHaveEntry(Candidate c, HashSet<string> taken) =>
        (c.TypeParameters.Length == 0
            ? c.HasParameterlessCtor || c.HasDIConstructor
            : c.FormControl is not null && c.HasParameterlessCtor)
        && !c.Properties.Any(BlocksEntry)
        && !taken.Contains(c.TypeName);

    // A prop the caller must name on every render. `required` has to be set at construction; the rest
    // are the factory's required parameters, which have no default for anything to put back.
    private static bool BlocksEntry(PropInfo p) =>
        p.UserMarkedRequired
        || (IsParamProperty(p) && !p.IsBoundInterfaceProp && IsRequiredFactoryParam(p));

    // The entries to emit, with same-name collisions removed and reported.
    //
    // Entries are all flattened onto ONE type — Rask.Core.Component, or each consumer component — and
    // keyed by SIMPLE NAME, while factories live in a per-namespace `Generated` class. So
    // `Features.Products.Card` and `Features.Orders.Card` both have a factory and cannot both have an
    // entry. Dropping the loser silently is the worst of the options: it compiles, and whichever one
    // the sort happened to put second simply has no entry (and, once the factory is deleted, no way to
    // be built at all). A collision between two types is not resolvable here — it is the author's to
    // resolve — so neither gets an entry and RASK037 says why.
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
            spc.ReportDiagnostic(Diagnostic.Create(Rask037, MakeDeclLocation(c), c.TypeName, names));
        }
    }

    // The generic form control's method entry. It takes ONE parameter — the bind expression — because
    // that is what infers the value type (`Input(() => model.Age)` → `Input<int>`); the validator and the
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
    }

    private static void EmitBoundEntry(
        StringBuilder sb, Candidate c, HashSet<string> taken, string visibility, string indent,
        string assemblyName, string hostTypeParameters = "")
    {
        // No bind expression to infer from, no `new T()` to build, or a member that must be set at
        // construction (CS9040): no entry — the factory stays the way in.
        if (c.FormControl is not { } fc || !c.HasParameterlessCtor || taken.Contains(c.TypeName)
            || c.Properties.Any(static p => p.UserMarkedRequired))
        {
            return;
        }

        // A method's type parameter may not reuse an enclosing type's name (CS0693), and the consumer
        // entries are injected INTO components — including generic ones (`BsDataGrid<T>` hosting the
        // entry for `Input<T>`). Rename only the colliding ones, so the common case reads unchanged.
        var typeParameters = c.TypeParameters;
        var constraints = c.TypeParameterConstraints;
        var self = c.FullyQualifiedName;
        var valueType = fc.ValueTypeFqn;
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
                valueType = RenameTypeParameter(valueType, name, renamed);
            }
        }

        // Bound mode: Bind is the only parameter, and it is what infers the value type.
        sb.Append(indent).Append(visibility).Append(" static ").Append(self).Append(' ')
            .Append(EscapeIdentifier(c.TypeName)).Append(typeParameters)
            .Append("(global::System.Linq.Expressions.Expression<global::System.Func<").Append(valueType)
            .Append(">> Bind)").Append(constraints).AppendLine();
        sb.Append(indent).Append("    => EntryBound<").Append(self).Append(", ")
            .Append(valueType).Append(">(Bind, ");
        EmitResetArguments(sb, c, assemblyName, typeParameters);
        sb.AppendLine(");");

        // Plain / controlled mode: nothing to infer from, so the caller writes the type argument
        // (`Input<string>().Value(v).Change(h)`). This is the method form of the property entry every
        // non-generic component gets.
        sb.Append(indent).Append(visibility).Append(" static ").Append(self).Append(' ')
            .Append(EscapeIdentifier(c.TypeName)).Append(typeParameters).Append("()").Append(constraints)
            .AppendLine();
        sb.Append(indent).Append("    => Entry<").Append(self).Append(">(");
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
    private static void EmitConsumerEntries(
        SourceProductionContext spc,
        ImmutableArray<Candidate> candidates,
        bool enabled,
        ComponentHost host)
    {
        if (!enabled || host.DeclaresComponent || candidates.IsDefaultOrEmpty)
        {
            return;
        }

        var entries = EntryCandidates(spc, candidates, EmptyNames);
        if (entries.Count == 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");

        foreach (var host2 in DistinctByType(candidates))
        {
            if (host2.IsNested)
            {
                // Injecting into a nested type would need every enclosing type to be partial too.
                continue;
            }

            if (!host2.IsPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(Rask036, MakeDeclLocation(host2), host2.TypeName));
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

            sb.Append("partial class ").Append(host2.TypeName).AppendLine(host2.TypeParameters);
            sb.AppendLine("{");
            foreach (var e in entries)
            {
                // A member may not share its enclosing type's name (CS0542) — and a component never
                // needs an entry for itself anyway.
                if (string.Equals(e.TypeName, host2.TypeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (e.TypeParameters.Length != 0)
                {
                    EmitBoundEntry(sb, e, EmptyNames, "private", indent: "    ", host.AssemblyName,
                        host2.TypeParameters);
                    continue;
                }

                sb.Append("    private static ").Append(e.FullyQualifiedName).Append(' ')
                    .Append(EscapeIdentifier(e.TypeName))
                    .Append(NeedsDiEntry(e) ? " => EntryDi<" : " => Entry<")
                    .Append(e.FullyQualifiedName).Append(">(");
                EmitResetArguments(sb, e, host.AssemblyName);
                sb.AppendLine(");");
            }

            sb.AppendLine("}");
            if (hasNs)
            {
                sb.AppendLine("}");
            }
        }

        spc.AddSource("RaskBuilderConsumerEntries.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    // Nothing is "taken" inside a consumer's own partial: the CS0542 self-name case is filtered before
    // the entry is emitted, and a user member that collides is the RASK0xx `new` story, not this one.
    private static readonly HashSet<string> EmptyNames = new(StringComparer.Ordinal);

    // `new T()` needs a public parameterless ctor; anything else goes through ActivatorUtilities —
    // the same split the factory emission makes via canUseObjectInit.
    private static bool NeedsDiEntry(Candidate c) => !c.HasParameterlessCtor;

    private static Location MakeDeclLocation(Candidate c) =>
        string.IsNullOrEmpty(c.DeclFilePath)
            ? Location.None
            : Location.Create(
                c.DeclFilePath,
                new TextSpan(c.DeclSpanStart, c.DeclSpanLength),
                new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0)));

    private readonly record struct ComponentHost(
        bool DeclaresComponent,
        string AssemblyName,
        EquatableArray<string> MemberNames);

    // Collect the distinct, ordered factory namespaces declared by [assembly: RaskFactoryNamespace(ns)] on
    // this compilation's own assembly and every referenced assembly. Ordered for deterministic output.
    private static ImmutableArray<string> ScanFactoryMarkerNamespaces(Compilation compilation)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        CollectFactoryMarkers(compilation.Assembly, result);
        foreach (var reference in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            CollectFactoryMarkers(reference, result);
        }

        return result.Count == 0 ? ImmutableArray<string>.Empty : result.ToImmutableArray();
    }

    private static void CollectFactoryMarkers(IAssemblySymbol assembly, SortedSet<string> into)
    {
        foreach (var attr in assembly.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString() != "Rask.Core.RaskFactoryNamespaceAttribute")
            {
                continue;
            }

            if (attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is string ns
                && !string.IsNullOrEmpty(ns))
            {
                into.Add(ns);
            }
        }
    }

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
            classDecl.Identifier.GetLocation().SourceTree?.FilePath ?? string.Empty,
            classDecl.Identifier.Span.Start,
            classDecl.Identifier.Span.Length);
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

    // Same question as IsAutoRerenderDelegate, asked of a property whose delegate may sit inside a
    // carrier struct (Handler / HandlerAsync / Carrier<TDelegate>). Without the unwrap every carrier
    // prop would look like a plain struct and silently lose its auto-rerender wrapping.
    private static bool IsAutoRerenderProp(ITypeSymbol type)
    {
        var t = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } lifted
            ? lifted.TypeArguments[0]
            : type;

        if (t is INamedTypeSymbol named)
        {
            switch (named.OriginalDefinition.ToDisplayString())
            {
                case "Rask.Core.Handler":
                case "Rask.Core.HandlerAsync":
                case "Rask.Core.Handler<TArgs>":
                case "Rask.Core.HandlerAsync<TArgs>":
                    // The carriers over Callback/CallbackAsync — the shape a parent↔child event
                    // callback has. Element's own events reach here too, but never with this answer:
                    // the caller gates on !isElement first, because a DOM handler is forwarded raw.
                    return true;
                case "Rask.Core.Carrier<TDelegate>":
                    return IsAutoRerenderDelegate(named.TypeArguments[0]);
            }
        }

        return IsAutoRerenderDelegate(t);
    }

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
                var isAutoRerenderDelegate =
                    !isElement && !isBoundInterfaceProp && IsAutoRerenderProp(prop.Type);

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
                    IsSharedSurfaceType(current)));
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

    private static void Emit(SourceProductionContext spc, ImmutableArray<Candidate> candidates, bool emitNavigation)
    {
        if (candidates.IsDefaultOrEmpty)
        {
            return;
        }

        // Report per-property diagnostics first.
        foreach (var c in candidates)
        {
            foreach (var p in c.Properties)
            {
                var location = MakeLocation(p);
                // RASK002 only fires when the generated factory genuinely cannot honor `required`.
                // A DI ctor alone is fine: with no parameterless ctor the factory builds via
                // ActivatorUtilities.CreateInstance (reflection bypasses the CS9035 check) and then
                // post-assigns every factory param, so a required no-initializer prop IS set. The one
                // broken shape is a parameterless ctor present *and* a required prop carrying a member
                // initializer: the factory then emits `new T() { … }` whose object initializer excludes
                // the initializer-carrying prop (IsParamProperty), so the consumer build hits CS9035.
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

        var byNamespace = candidates
            .GroupBy(c => c.Namespace)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in byNamespace)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();

            var hasNs = !string.IsNullOrEmpty(group.Key);
            if (hasNs)
            {
                sb.Append("namespace ").Append(group.Key).AppendLine(";");
                sb.AppendLine();
            }

            sb.AppendLine("public static partial class Generated");
            sb.AppendLine("{");

            // Dedupe by name AND type-parameter list, so same-named generic overloads of different arity
            // (e.g. BsSelect<TItem> and BsSelect<TValue, TItem>) each get a factory instead of the second
            // being dropped — while genuine duplicates (a partial class seen twice) still collapse.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in group.OrderBy(c => c.TypeName + c.TypeParameters, StringComparer.Ordinal))
            {
                if (!seen.Add(c.TypeName + c.TypeParameters))
                {
                    continue;
                }

                EmitFactory(sb, c, emitNavigation);
                sb.AppendLine();

                if (c.FormControl is { } fc)
                {
                    EmitBoundFactory(sb, c, fc, emitNavigation);
                    sb.AppendLine();
                }

                if (c.GenericFactory is { } gf)
                {
                    EmitGenericFactoryOverload(sb, c, gf, emitNavigation);
                    sb.AppendLine();
                }

                foreach (var f in c.Forwarders)
                {
                    EmitForwarderFactory(sb, c, f, emitNavigation);
                    sb.AppendLine();
                }
            }

            sb.AppendLine("}");

            var hint = hasNs ? $"{group.Key}.Generated.g.cs" : "Generated.g.cs";
            spc.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    // Children is delivered via the `Component this[params Component[]]` indexer on Component
    // itself — the factory has no Children parameter. This helper exists only to recognize
    // the standard Children collection shapes while filtering them out in GetFactoryProperties.
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

    // Emits the per-factory header trivia: a `<see cref>` doc breadcrumb that links to the
    // component type (Quick-Doc / hover navigation; gated by RaskFactoryNavigation) and an
    // always-on `[DebuggerStepThrough]` so the debugger steps over the factory into user code.
    // F12 still lands on the generated method — stock Roslyn/ReSharper navigate a generated
    // symbol to its generated document; "Navigate to Type of Symbol" jumps to the component.
    private static void EmitMethodHeader(StringBuilder sb, Candidate c, bool emitNavigation)
    {
        if (emitNavigation)
        {
            // cref uses `{T}` (not `<T>`) for generic arity — the doc-comment cref syntax — so it
            // resolves to the component type and renders as a navigable link.
            var cref = c.FullyQualifiedName.Replace('<', '{').Replace('>', '}');
            sb.Append("    /// <summary>Factory for the <see cref=\"").Append(cref)
                .AppendLine("\"/> component.</summary>");
        }

        sb.AppendLine("    [global::System.Diagnostics.DebuggerStepThrough]");
    }

    private static void EmitFactory(StringBuilder sb, Candidate c, bool emitNavigation)
    {
        var visibility = c.IsPublic ? "public" : "internal";
        // Bound-mode IFormControl members are excluded from the controlled factory — they appear on the
        // synthesized bound factory (EmitBoundFactory) instead.
        var paramProps = c.Properties.Where(p => IsParamProperty(p) && !p.IsBoundInterfaceProp).ToList();
        var requiredProps = paramProps.Where(IsRequiredFactoryParam).ToList();
        var optionalProps = paramProps.Where(p => !IsRequiredFactoryParam(p)).ToList();

        // Key (Component-level, Blazor @key parity) is a reconciliation IDENTITY, not a reactive
        // prop: it's a factory param and is assigned to the instance, but it's excluded from the
        // propsChanged diff. That keeps a propertyless component on the `propsChanged: false` fast
        // path, and means a Key change never fires OnPropsChanged (a different key is a different
        // logical item, which mounts fresh rather than re-rendering the old instance).
        var hasKeyProp = paramProps.Any(IsKeyProp);

        // nonKeyProps need re-assignment every render (drives the full-vs-fast path choice).
        // foldProps is the subset that participates in the propsChanged diff: it also excludes
        // auto-wrapped event-callback delegates, which are a fresh wrapper closure every render
        // (never meaningfully equal — diffing them is pure noise that would defeat the
        // `propsChanged: false` fast path for any callback-receiving component). They are still
        // assigned each render, just not folded.
        var nonKeyProps = paramProps.Where(p => !IsKeyProp(p)).ToList();
        var foldProps = nonKeyProps
            .Where(p => !p.IsAutoRerenderDelegate && !p.IsDelegate && !IsCarrierProp(p)).ToList();

        // Prefer the parameterless ctor + object-initializer path whenever it's available.
        // Even if the component declares additional ctors that take services (DI) or
        // primitives (Text/Raw's string-arg ctor), the generated factory only needs the
        // parameterless one and then assigns properties — no ActivatorUtilities required.
        var canUseObjectInit = c.HasParameterlessCtor || !c.HasDIConstructor;

        // Signature.
        EmitMethodHeader(sb, c, emitNavigation);
        sb.Append("    ").Append(visibility).Append(" static ").Append(c.FullyQualifiedName).Append(' ')
            .Append(c.TypeName).Append(c.TypeParameters).Append('(');
        var first = true;
        foreach (var p in requiredProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(ParamType(p)).Append(' ').Append(p.Escaped);
        }

        foreach (var p in optionalProps)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append(ParamType(p)).Append(' ').Append(p.Escaped).Append(" = ")
                .Append(DefaultLiteralFor(p));
        }

        sb.Append(')').AppendLine(c.TypeParameterConstraints);
        sb.AppendLine("    {");

        if (nonKeyProps.Count == 0)
        {
            // Legacy parameterless factory shape preserved (Key, if present, is assigned but not
            // diffed — so this fast path still emits propsChanged: false).
            sb.Append("        if (").Append(ContextFullName).AppendLine(".Current is { } __ctx)");
            sb.AppendLine("        {");
            sb.Append("            var __c = __ctx.GetOrCreate<").Append(c.FullyQualifiedName).AppendLine(">(");
            if (canUseObjectInit)
            {
                // Prefer the parameterless ctor: a context with no service provider (tests
                // calling RenderAsLiveRoot() without a ServiceProvider) would otherwise NRE
                // inside ActivatorUtilities. The DI-ctor branch below stays as a fallback for
                // components whose only constructors take injected services.
                sb.Append("                static _ => new ").Append(c.FullyQualifiedName).AppendLine("());");
            }
            else
            {
                sb.Append(
                        "                static __sp => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<")
                    .Append(c.FullyQualifiedName).AppendLine(">(__sp));");
            }

            if (hasKeyProp)
            {
                sb.AppendLine("            __c.Key = Key;");
            }

            sb.AppendLine("            __ctx.NotifyParameters(__c, propsChanged: false);");
            sb.AppendLine("            return __c;");
            sb.AppendLine("        }");
            if (c.HasParameterlessCtor)
            {
                if (hasKeyProp)
                {
                    sb.Append("        var __cf = new ").Append(c.FullyQualifiedName).AppendLine("();");
                    sb.AppendLine("        __cf.Key = Key;");
                    sb.AppendLine("        return __cf;");
                }
                else
                {
                    sb.Append("        return new ").Append(c.FullyQualifiedName).AppendLine("();");
                }
            }
            else
            {
                sb.Append("        throw new global::System.InvalidOperationException(\"Component '")
                    .Append(c.FullyQualifiedName)
                    .AppendLine(
                        "' has no parameterless constructor; it can only be instantiated inside a LiveRenderContext (e.g. via MapRask<TApp>).\");");
            }

            sb.AppendLine("    }");
            return;
        }

        // Has factory-param properties. Construct, then re-apply props every render so cached instances get fresh values.
        var hasRequired = requiredProps.Count > 0;
        sb.Append("        ").Append(c.FullyQualifiedName).AppendLine(" __c;");
        sb.Append("        if (").Append(ContextFullName).AppendLine(".Current is { } __ctx)");
        if (canUseObjectInit && hasRequired)
        {
            // Has `required` members: they MUST be set at construction, so capture the args in an object
            // initializer. This closure allocates a display class per render, but `required` is rare.
            sb.Append("            __c = __ctx.GetOrCreate<").Append(c.FullyQualifiedName).AppendLine(">(");
            sb.Append("                __sp => new ").Append(c.FullyQualifiedName).Append("()");
            EmitInitializerBody(sb, paramProps);
            sb.AppendLine(");");
        }
        else if (canUseObjectInit)
        {
            // No `required` members → a STATIC, capture-free factory. Every prop is re-applied by the
            // assignment pass below (which runs each render for cache reuse anyway), so seeding them in
            // the lambda would be redundant — and capturing the args would allocate a display-class
            // closure per render that scales with the parameter count. With the universal
            // GlobalEventHandlers surface adding ~50 delegate params to every element factory, that
            // closure dominated the render-hotpath allocation; a static lambda removes it entirely.
            sb.Append("            __c = __ctx.GetOrCreate<").Append(c.FullyQualifiedName).AppendLine(">(");
            sb.Append("                static __sp => new ").Append(c.FullyQualifiedName).AppendLine("());");
        }
        else
        {
            sb.Append("            __c = __ctx.GetOrCreate<").Append(c.FullyQualifiedName).AppendLine(">(");
            sb.Append(
                    "                static __sp => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<")
                .Append(c.FullyQualifiedName).AppendLine(">(__sp));");
        }

        sb.AppendLine("        else");
        if (canUseObjectInit && hasRequired)
        {
            sb.Append("            __c = new ").Append(c.FullyQualifiedName).Append("()");
            EmitInitializerBody(sb, paramProps);
            sb.AppendLine(";");
        }
        else if (canUseObjectInit)
        {
            // No-context fallback: bare construct; the assignment pass below applies every prop.
            sb.Append("            __c = new ").Append(c.FullyQualifiedName).AppendLine("();");
        }
        else
        {
            sb.Append("            throw new global::System.InvalidOperationException(\"Component '")
                .Append(c.FullyQualifiedName)
                .AppendLine(
                    "' has no parameterless constructor; it can only be instantiated inside a LiveRenderContext (e.g. via MapRask<TApp>).\");");
        }

        EmitSnapshotsAndAssignments(sb, paramProps, foldProps);
        sb.Append("        if (").Append(ContextFullName).AppendLine(".Current is { } __ctx2)");
        sb.AppendLine("            __ctx2.NotifyParameters(__c, __propsChanged);");
        sb.AppendLine("        return __c;");
        sb.AppendLine("    }");
    }

    // For an IFormControl<T> component, synthesizes the Bind-first bound factory in none/sync/async
    // validator flavors. It builds the instance through the same GetOrCreate/NotifyParameters path as the
    // controlled factory and sets the bound-mode members (Bind/Validate/ValidateAsync/AfterBind/
    // AfterBindAsync); the controlled members (Value/OnChange/OnChangeAsync) are left at their defaults
    // (= bound mode). This replaces the hand-written `[GenerateForwarderFactory(Validator="Validate")] Bound`.
    private static void EmitBoundFactory(StringBuilder sb, Candidate c, FormControlInfo fc, bool emitNavigation)
    {
        EmitBoundOverload(sb, c, fc, emitNavigation, ValidatorShape.None);
        sb.AppendLine();
        EmitBoundOverload(sb, c, fc, emitNavigation, ValidatorShape.Sync);
        sb.AppendLine();
        EmitBoundOverload(sb, c, fc, emitNavigation, ValidatorShape.Async);
    }

    private static void EmitBoundOverload(
        StringBuilder sb, Candidate c, FormControlInfo fc, bool emitNavigation, ValidatorShape shape)
    {
        var visibility = c.IsPublic ? "public" : "internal";
        var canUseObjectInit = c.HasParameterlessCtor || !c.HasDIConstructor;

        PropInfo Member(string name) => c.Properties.First(p => p.Name == name);
        var bind = Member("Bind");
        var afterBind = Member("AfterBind");
        var afterBindAsync = Member("AfterBindAsync");

        // Shared/display props: everything that's a factory param and not an IFormControl member.
        var controlled = new[] { "Value", "OnChange", "OnChangeAsync" };
        var shared = c.Properties
            .Where(p => IsParamProperty(p) && !p.IsBoundInterfaceProp && Array.IndexOf(controlled, p.Name) < 0)
            .ToList();
        var sharedRequired = shared.Where(IsRequiredFactoryParam).ToList();
        var sharedOptional = shared.Where(p => !IsRequiredFactoryParam(p)).ToList();

        var validatorType = shape == ValidatorShape.Sync
            ? "global::Rask.Core.Forms.Validate<" + fc.ValueTypeFqn + ">"
            : "global::Rask.Core.Forms.ValidateAsync<" + fc.ValueTypeFqn + ">";

        // Signature: Bind (required) → shared required (e.g. Options) → validator (sync/async, required) →
        // AfterBind/AfterBindAsync (optional) → shared optional (display).
        EmitMethodHeader(sb, c, emitNavigation);
        sb.Append("    ").Append(visibility).Append(" static ").Append(c.FullyQualifiedName).Append(' ')
            .Append(c.TypeName).Append(c.TypeParameters).Append('(');
        sb.Append(StripNullable(bind.TypeFqn)).Append(" Bind");
        foreach (var p in sharedRequired)
        {
            sb.Append(", ").Append(ParamType(p)).Append(' ').Append(p.Escaped);
        }

        if (shape != ValidatorShape.None)
        {
            sb.Append(", ").Append(validatorType).Append(" Validate");
        }

        sb.Append(", ").Append(ParamType(afterBind)).Append(' ').Append(afterBind.Escaped).Append(" = null");
        sb.Append(", ").Append(ParamType(afterBindAsync)).Append(' ').Append(afterBindAsync.Escaped)
            .Append(" = null");
        foreach (var p in sharedOptional)
        {
            sb.Append(", ").Append(ParamType(p)).Append(' ').Append(p.Escaped).Append(" = ")
                .Append(DefaultLiteralFor(p));
        }

        sb.Append(')').AppendLine(c.TypeParameterConstraints);
        sb.AppendLine("    {");

        // Bound-mode member assignments (raw — never auto-wrapped; AfterBind is a post-bind hook, not an
        // event callback). The validator param (named Validate either way) sets Validate for the sync
        // overload and ValidateAsync for the async overload.
        // Each carrier member goes through AssignExpr, so an omitted validator/hook stays an unset
        // carrier instead of a non-null one wrapping null (a bare `null` literal already converts
        // straight, but the supplied-argument branch would otherwise run the implicit conversion).
        var validate = Member("Validate");
        var validateAsync = Member("ValidateAsync");
        var validateExpr = shape == ValidatorShape.Sync ? AssignExpr(validate, "Validate") : "null";
        var validateAsyncExpr = shape == ValidatorShape.Async ? AssignExpr(validateAsync, "Validate") : "null";
        var assigns = new List<(string Esc, string Expr)>
        {
            ("Bind", "Bind"),
            ("Validate", validateExpr),
            ("ValidateAsync", validateAsyncExpr),
            (afterBind.Escaped, AssignExpr(afterBind, afterBind.Escaped)),
            (afterBindAsync.Escaped, AssignExpr(afterBindAsync, afterBindAsync.Escaped)),
        };
        foreach (var p in shared)
        {
            assigns.Add((p.Escaped, AssignExpr(p, p.Escaped)));
        }

        // Fold (propsChanged): only the shared value props participate — the bound members are fresh
        // expressions/delegates each render (folding them would force propsChanged: true every frame).
        var foldProps = shared
            .Where(p => !IsKeyProp(p) && !p.IsAutoRerenderDelegate && !p.IsDelegate && !IsCarrierProp(p))
            .ToList();

        void EmitInit(string indent)
        {
            sb.AppendLine();
            sb.Append(indent).AppendLine("{");
            for (var i = 0; i < assigns.Count; i++)
            {
                sb.Append(indent).Append("    ").Append(assigns[i].Esc).Append(" = ").Append(assigns[i].Expr);
                sb.AppendLine(i < assigns.Count - 1 ? "," : string.Empty);
            }

            sb.Append(indent).Append('}');
        }

        sb.Append("        ").Append(c.FullyQualifiedName).AppendLine(" __c;");
        sb.Append("        if (").Append(ContextFullName).AppendLine(".Current is { } __ctx)");
        if (canUseObjectInit)
        {
            sb.Append("            __c = __ctx.GetOrCreate<").Append(c.FullyQualifiedName).AppendLine(">(");
            sb.Append("                __sp => new ").Append(c.FullyQualifiedName).Append("()");
            EmitInit("                ");
            sb.AppendLine(");");
            sb.AppendLine("        else");
            sb.Append("            __c = new ").Append(c.FullyQualifiedName).Append("()");
            EmitInit("            ");
            sb.AppendLine(";");
        }
        else
        {
            sb.Append("            __c = __ctx.GetOrCreate<").Append(c.FullyQualifiedName).AppendLine(">(");
            sb.Append(
                    "                static __sp => global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<")
                .Append(c.FullyQualifiedName).AppendLine(">(__sp));");
            sb.AppendLine("        else");
            sb.Append("            throw new global::System.InvalidOperationException(\"Component '")
                .Append(c.FullyQualifiedName)
                .AppendLine(
                    "' has no parameterless constructor; it can only be instantiated inside a LiveRenderContext (e.g. via MapRask<TApp>).\");");
        }

        foreach (var p in foldProps)
        {
            sb.Append("        var __old_").Append(p.Name).Append(" = __c.").Append(p.Escaped).AppendLine(";");
        }

        foreach (var (esc, expr) in assigns)
        {
            sb.Append("        __c.").Append(esc).Append(" = ").Append(expr).AppendLine(";");
        }

        if (foldProps.Count == 0)
        {
            sb.AppendLine("        var __propsChanged = false;");
        }
        else if (foldProps.Count == 1)
        {
            var p = foldProps[0];
            sb.Append("        var __propsChanged = !global::System.Collections.Generic.EqualityComparer<")
                .Append(p.TypeFqn).Append(">.Default.Equals(__old_").Append(p.Name).Append(", ").Append(p.Escaped)
                .AppendLine(");");
        }
        else
        {
            sb.AppendLine("        var __propsChanged =");
            for (var i = 0; i < foldProps.Count; i++)
            {
                var p = foldProps[i];
                sb.Append("            !global::System.Collections.Generic.EqualityComparer<").Append(p.TypeFqn)
                    .Append(">.Default.Equals(__old_").Append(p.Name).Append(", ").Append(p.Escaped).Append(')');
                sb.AppendLine(i < foldProps.Count - 1 ? " ||" : ";");
            }
        }

        sb.Append("        if (").Append(ContextFullName).AppendLine(".Current is { } __ctx2)");
        sb.AppendLine("            __ctx2.NotifyParameters(__c, __propsChanged);");
        sb.AppendLine("        return __c;");
        sb.AppendLine("    }");
    }

    private static void EmitForwarderFactory(StringBuilder sb, Candidate c, ForwarderInfo f, bool emitNavigation)
    {
        // No validator configured → one verbatim forwarder (the original behavior).
        if (f.ValidatorParam is null)
        {
            EmitForwarderOverload(sb, c, f, emitNavigation, ValidatorShape.None, fanOut: false);
            return;
        }

        // Validator configured → fan into none/sync/async, exactly like the [FactoryGeneric] validator
        // fan-out, but forwarding to the hand-written source method instead of building the component.
        EmitForwarderOverload(sb, c, f, emitNavigation, ValidatorShape.None, fanOut: true);
        EmitForwarderOverload(sb, c, f, emitNavigation, ValidatorShape.Sync, fanOut: true);
        EmitForwarderOverload(sb, c, f, emitNavigation, ValidatorShape.Async, fanOut: true);
    }

    private static void EmitForwarderOverload(
        StringBuilder sb, Candidate c, ForwarderInfo f, bool emitNavigation, ValidatorShape shape, bool fanOut)
    {
        var visibility = c.IsPublic ? "public" : "internal";

        // The generated factory method carries BOTH the component's and the method's type parameters: a
        // generic method on a non-generic component (Input.Bound<TProp>) contributes the method's; a
        // non-generic method on a generic component (MultiSelect<TItem>.Bound) contributes the component's.
        // The call receiver is c.FullyQualifiedName (already constructed with the component's args), and the
        // method is invoked with only its own type args (f.TypeParameters) — so both shapes forward right.
        var factoryTypeParams = MergeTypeParams(c.TypeParameters, f.TypeParameters);
        var factoryConstraints = (c.TypeParameterConstraints + f.TypeParameterConstraints);

        // Signature: `public static {Component} {ComponentName}<...>(<params>) <constraints>`
        EmitMethodHeader(sb, c, emitNavigation);
        sb.Append("    ").Append(visibility).Append(" static ").Append(c.FullyQualifiedName).Append(' ')
            .Append(c.TypeName).Append(factoryTypeParams).Append('(');
        var first = true;
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            var p = f.Parameters[i];
            var isValidator = fanOut && p.Name == f.ValidatorParam;

            // The None overload drops the validator parameter entirely (it's forwarded as null below).
            if (isValidator && shape == ValidatorShape.None)
            {
                continue;
            }

            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            if (p.IsParams)
            {
                sb.Append("params ");
            }

            if (isValidator && shape == ValidatorShape.Sync)
            {
                sb.Append("global::Rask.Core.Forms.Validate<").Append(f.ValidatorTypeArg).Append("> ").Append(p.Name);
            }
            else if (isValidator && shape == ValidatorShape.Async)
            {
                sb.Append("global::Rask.Core.Forms.ValidateAsync<").Append(f.ValidatorTypeArg).Append("> ")
                    .Append(p.Name);
            }
            else
            {
                sb.Append(p.TypeFqn).Append(' ').Append(p.Name);
                if (p.DefaultLiteral.Length > 0)
                {
                    sb.Append(" = ").Append(p.DefaultLiteral);
                }
            }
        }

        sb.Append(')').AppendLine(factoryConstraints);

        // Body forwards to the source method. Non-fan-out keeps positional forwarding (verbatim shape);
        // fan-out forwards by name so the omitted validator can be passed as null at its real position.
        sb.Append("        => ").Append(c.FullyQualifiedName).Append('.').Append(f.MethodName)
            .Append(f.TypeParameters).Append('(');
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var p = f.Parameters[i];
            if (!fanOut)
            {
                sb.Append(p.Name);
            }
            else if (p.Name == f.ValidatorParam && shape == ValidatorShape.None)
            {
                sb.Append(p.Name).Append(": null");
            }
            else
            {
                sb.Append(p.Name).Append(": ").Append(p.Name);
            }
        }

        sb.AppendLine(");");
    }

    private static void EmitGenericFactoryOverload(StringBuilder sb, Candidate c, GenericFactoryConfig gf,
        bool emitNavigation)
    {
        var visibility = c.IsPublic ? "public" : "internal";
        var typedSet = new HashSet<string>(StringComparer.Ordinal);
        var typedDelegates = new List<string>();
        foreach (var name in gf.TypedDelegateProperties)
        {
            if (string.IsNullOrEmpty(name) || !typedSet.Add(name))
            {
                continue;
            }

            typedDelegates.Add(name);
        }

        var typedValidators = new List<string>();
        var validatorSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in gf.TypedValidatorProperties)
        {
            if (string.IsNullOrEmpty(name) || !typedSet.Add(name))
            {
                continue;
            }

            typedValidators.Add(name);
            validatorSet.Add(name);
        }

        var modelProperty = gf.ModelProperty;
        var paramProps = c.Properties.Where(IsParamProperty).ToList();

        if (typedValidators.Count == 0)
        {
            EmitOneOverload(sb, c, gf, visibility, typedSet, typedDelegates, typedValidators, validatorSet,
                modelProperty, paramProps, ValidatorShape.None, emitNavigation);
            return;
        }

        // Fan out into three overloads. Overload resolution at the call site disambiguates:
        //   - no `Validate:` arg          → None overload (Validate forwarded as null)
        //   - one-arg lambda `v => …`     → Sync overload (typed Validate<T>)
        //   - two-arg lambda `(v, ct) => …` → Async overload (typed ValidateAsync<T>)
        // Both Sync and Async overloads make the validator parameter required so the No
        // overload remains the unambiguous match when the caller passes neither.
        EmitOneOverload(sb, c, gf, visibility, typedSet, typedDelegates, typedValidators, validatorSet,
            modelProperty, paramProps, ValidatorShape.None, emitNavigation);
        EmitOneOverload(sb, c, gf, visibility, typedSet, typedDelegates, typedValidators, validatorSet,
            modelProperty, paramProps, ValidatorShape.Sync, emitNavigation);
        EmitOneOverload(sb, c, gf, visibility, typedSet, typedDelegates, typedValidators, validatorSet,
            modelProperty, paramProps, ValidatorShape.Async, emitNavigation);
    }

    private static void EmitOneOverload(
        StringBuilder sb,
        Candidate c,
        GenericFactoryConfig gf,
        string visibility,
        HashSet<string> typedSet,
        List<string> typedDelegates,
        List<string> typedValidators,
        HashSet<string> validatorSet,
        string modelProperty,
        List<PropInfo> paramProps,
        ValidatorShape validatorShape,
        bool emitNavigation)
    {
        EmitMethodHeader(sb, c, emitNavigation);
        sb.Append("    ").Append(visibility).Append(" static ").Append(c.FullyQualifiedName).Append(' ')
            .Append(c.TypeName).Append('<').Append(gf.TypeParameter).Append(">(");

        var first = true;

        // Required: TModel Model — replaces ModelProperty's optional position with a typed,
        // mandatory parameter. The non-generic factory's `object? Model = null` accepts the
        // TModel value via implicit reference conversion (the `class` constraint ensures it).
        if (modelProperty.Length > 0)
        {
            first = false;
            sb.Append(gf.TypeParameter).Append(' ').Append(modelProperty);
        }

        // Typed validator parameter — required, no default. Position is right after Model so
        // it stays prominent in IntelliSense. Sync vs async is fixed per overload; the body
        // forwards the lambda into the non-generic factory's `Delegate?` slot.
        if (validatorShape == ValidatorShape.Sync)
        {
            foreach (var vp in typedValidators)
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;
                sb.Append("global::Rask.Core.Forms.Validate<").Append(gf.TypeParameter).Append("> ").Append(vp);
            }
        }
        else if (validatorShape == ValidatorShape.Async)
        {
            foreach (var vp in typedValidators)
            {
                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;
                sb.Append("global::Rask.Core.Forms.ValidateAsync<").Append(gf.TypeParameter).Append("> ").Append(vp);
            }
        }

        // Typed delegates: `Action<TModel>? X = null` then `Func<TModel, Task>? XAsync = null`,
        // grouped by side (sync first then async) for readability.
        foreach (var dp in typedDelegates)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append("global::Rask.Core.Callback<").Append(gf.TypeParameter).Append(">? ").Append(dp).Append(" = null");
        }

        foreach (var dp in typedDelegates)
        {
            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            sb.Append("global::Rask.Core.CallbackAsync<").Append(gf.TypeParameter)
                .Append(">? ").Append(dp).Append("Async = null");
        }

        // Remaining props in declaration order, skipping the Model and typed-delegate names
        // already covered above. Children is excluded by GetFactoryProperties.
        foreach (var p in paramProps)
        {
            if (p.Name == modelProperty || typedSet.Contains(p.Name))
            {
                continue;
            }

            if (!first)
            {
                sb.Append(", ");
            }

            first = false;
            // ParamType, not TypeFqn: this overload forwards to the ordinary factory, whose parameter
            // for a carrier prop is the DELEGATE. Typing the pass-through as the carrier would emit an
            // argument the target cannot take — the implicit conversion only runs delegate → carrier.
            sb.Append(ParamType(p)).Append(' ').Append(p.Name);
            if (!IsRequiredFactoryParam(p))
            {
                sb.Append(" = ").Append(DefaultLiteralFor(p));
            }
        }

        sb.Append(") where ").Append(gf.TypeParameter).Append(" : ").AppendLine(gf.Constraint);
        sb.AppendLine("    {");

        foreach (var dp in typedDelegates)
        {
            // Wrap the typed sync/async delegates so invoking them re-renders the providing component
            // (parent→child callback parity). The base factory's prop is `Delegate?`, which isn't
            // auto-wrapped, so the wrapping must happen here on the concrete Action<T>/Func<T,Task>.
            sb.Append("        var __").Append(dp)
                .Append(" = (global::System.Delegate?)global::Rask.Core.AutoCallback.Wrap(").Append(dp)
                .Append(") ?? global::Rask.Core.AutoCallback.Wrap(").Append(dp).AppendLine("Async);");
        }

        sb.Append("        return ").Append(c.TypeName).AppendLine("(");
        var argLines = new List<string>();
        foreach (var p in paramProps)
        {
            string forward;
            if (p.Name == modelProperty)
            {
                forward = $"{p.Name}: {modelProperty}";
            }
            else if (validatorSet.Contains(p.Name))
            {
                forward = validatorShape == ValidatorShape.None
                    ? $"{p.Name}: null"
                    : $"{p.Name}: {p.Name}";
            }
            else if (typedSet.Contains(p.Name))
            {
                forward = $"{p.Name}: __{p.Name}";
            }
            else
            {
                forward = $"{p.Name}: {p.Name}";
            }

            argLines.Add(forward);
        }

        for (var i = 0; i < argLines.Count; i++)
        {
            sb.Append("            ").Append(argLines[i]);
            if (i < argLines.Count - 1)
            {
                sb.Append(',');
            }

            sb.AppendLine();
        }

        sb.AppendLine("        );");
        sb.AppendLine("    }");
    }

    private static void EmitInitializerBody(StringBuilder sb, IEnumerable<PropInfo> props)
    {
        sb.AppendLine();
        sb.AppendLine("            {");
        var entries = props.ToList();
        for (var i = 0; i < entries.Count; i++)
        {
            var p = entries[i];
            sb.Append("                ").Append(p.Escaped).Append(" = ").Append(AssignExpr(p, p.Escaped));
            if (i < entries.Count - 1)
            {
                sb.Append(',');
            }

            sb.AppendLine();
        }

        sb.Append("            }");
    }

    // assignProps: every factory param re-applied to the (possibly cached) instance each render —
    // includes Key and auto-wrapped delegates. foldProps: the subset that participates in the
    // propsChanged diff — excludes Key (a reconciliation identity) and auto-wrapped delegates
    // (a fresh wrapper closure each render).
    private static void EmitSnapshotsAndAssignments(StringBuilder sb,
        IReadOnlyList<PropInfo> assignProps, IReadOnlyList<PropInfo> foldProps)
    {
        // Snapshot prior values of the diff-participating props (typed via the property's FQN so
        // nullable annotations round-trip).
        foreach (var p in foldProps)
        {
            // __old_<Name> is a fresh local (raw Name is a valid identifier even when Name is a
            // keyword); the property access __c.<Name> must be '@'-escaped.
            sb.Append("        var __old_").Append(p.Name).Append(" = __c.").Append(p.Escaped).AppendLine(";");
        }

        // Re-apply ALL params (including Key) so cached instances see fresh values. Event-callback
        // delegates are wrapped so invoking them re-renders the owning component (see AutoCallback).
        foreach (var p in assignProps)
        {
            string value;
            if (p.IsAutoRerenderDelegate)
            {
                // Wrap returns a nullable delegate (null in → null out); a non-nullable prop never
                // passes null, so the null-forgiving `!` is safe and silences CS8601. A carrier prop
                // takes the `?` back off through From, so it never needs the suppression.
                value = "global::Rask.Core.AutoCallback.Wrap(" + p.Escaped + ")";
                if (!p.IsNullable)
                {
                    value += "!";
                }
            }
            else
            {
                value = p.Escaped;
            }

            sb.Append("        __c.").Append(p.Escaped).Append(" = ").Append(AssignExpr(p, value)).AppendLine(";");
        }

        if (foldProps.Count == 0)
        {
            sb.AppendLine("        var __propsChanged = false;");
            return;
        }

        // Fold per-prop equality into a single __propsChanged bool. EqualityComparer<T>.Default
        // gives ref-equality for ref types unless the type overrides Equals, and structural for
        // primitives — same semantics Blazor uses for [Parameter] equality.
        if (foldProps.Count == 1)
        {
            var p = foldProps[0];
            sb.Append("        var __propsChanged = !global::System.Collections.Generic.EqualityComparer<")
                .Append(p.TypeFqn).Append(">.Default.Equals(__old_").Append(p.Name).Append(", ").Append(p.Escaped)
                .AppendLine(");");
            return;
        }

        sb.AppendLine("        var __propsChanged =");
        for (var i = 0; i < foldProps.Count; i++)
        {
            var p = foldProps[i];
            sb.Append("            !global::System.Collections.Generic.EqualityComparer<").Append(p.TypeFqn)
                .Append(">.Default.Equals(__old_").Append(p.Name).Append(", ").Append(p.Escaped).Append(')');
            sb.AppendLine(i < foldProps.Count - 1 ? " ||" : ";");
        }
    }

    private static bool IsKeyProp(PropInfo p) => string.Equals(p.Name, "Key", StringComparison.Ordinal);

    private static void EmitGlobalUsings(
        SourceProductionContext spc,
        ImmutableArray<Candidate> candidates,
        bool enabled,
        EquatableArray<string> markerNamespaces)
    {
        if (!enabled)
        {
            return;
        }

        var namespaces = candidates.IsDefaultOrEmpty
            ? Array.Empty<string>()
            : candidates
                .Select(c => c.Namespace)
                .Where(ns => !string.IsNullOrEmpty(ns))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(ns => ns, StringComparer.Ordinal)
                .ToArray();

        var emitted = new HashSet<string>(StringComparer.Ordinal)
        {
            // The framework's own factory namespaces — always emitted (below), so skip them everywhere else.
            "Rask.Core.Components",
            "Rask.Core.Routing",
        };

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        // The framework's own factory namespaces — always make them globally visible to
        // consumers, even if this assembly defines no user components of its own (and so
        // `namespaces` is empty).
        sb.AppendLine("global using static global::Rask.Core.Components.Generated;");
        sb.AppendLine("global using static global::Rask.Core.Routing.Generated;");
        foreach (var ns in namespaces)
        {
            if (emitted.Add(ns))
            {
                sb.Append("global using static global::").Append(ns).AppendLine(".Generated;");
            }
        }

        // Satellite factory families from referenced assemblies marked with [assembly: RaskFactoryNamespace].
        // Emission is conditional on the marker being present in the reference graph, so a pure-Core / Server /
        // Wasm consumer gets no dangling `using` while a consumer that references Rask.Native gets its factories.
        foreach (var ns in markerNamespaces)
        {
            if (!string.IsNullOrEmpty(ns) && emitted.Add(ns))
            {
                sb.Append("global using static global::").Append(ns).AppendLine(".Generated;");
            }
        }

        spc.AddSource("RaskGlobalUsings.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

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
        // File path + span rather than a Location: Location is not value-equatable, so caching it on
        // the candidate would defeat the incremental generator's comparison (same reason PropInfo
        // stores DeclaringFilePath/Span and rebuilds via MakeLocation).
        string DeclFilePath,
        int DeclSpanStart,
        int DeclSpanLength);

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
        bool IsSharedSurfaceProp)
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
