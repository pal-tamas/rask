using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
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

                if (string.Equals(p.Name, "Children", StringComparison.Ordinal) || !seen.Add(p.Name))
                {
                    continue;
                }

                // Carriers count as delegates here for the same reason as the per-component pass: a
                // `Callback` (or `Callback?`) is a struct, so the TypeKind test alone would fold it and defeat the render
                // cache for every element that carries a handler.
                var isDelegate = p.Type.TypeKind == TypeKind.Delegate
                                 || CarrierDelegates(DisplayTypeName(p.Type, FullyQualifiedNullable, compilation))
                                     .Count > 0;
                var (defaultLiteral, isRequired) = DefaultLiteralFor(p, compilation);
                shared.Add(new SharedSetter(
                    p.Name,
                    DisplayTypeName(p.Type, FullyQualifiedNullable, compilation),
                    owner,
                    isDelegate,
                    // Same rule as ResetLiteralFor: a required prop has no default on either surface, so
                    // the reset writes the constructed state and `!` is what lets a non-nullable
                    // reference prop take it under warnings-as-errors.
                    isRequired ? defaultLiteral + "!" : defaultLiteral,
                    string.Equals(owner, elementFqn, StringComparison.Ordinal),
                    isRequired,
                    DeclaresDerivedSetter(p),
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
        // as constrained generic extensions over the component itself. Being generic they already cover every component
        // in the graph, so an assembly that is only a component LIBRARY re-emits an identical set into the
        // same global namespace and makes `.Key(id)` ambiguous to infer (CS0411). A component LIBRARY is
        // that shape, and opts out through the same switch that stops it injecting its own entries.
        var sharedBits = SharedPendingBits(host);
        if (emitSharedSurface)
        {
            EmitSharedSetters(spc, sb, host, sharedBits);
        }

        // One Candidate per component TYPE. A partial class whose declarations each carry a base list
        // (`partial class Foo : Component` in one file, `partial class Foo : IMarker` in another) reaches
        // the syntax provider twice, and emitting its setters twice is CS0111.
        EmitComponentSetters(sb, candidates);

        EmitCandidateResets(sb, candidates);

        sb.AppendLine("}");
        spc.AddSource("RaskBuilderSetters.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));

        if (host.Shared.Count != 0)
        {
            EmitSharedResets(spc, host, sharedBits);
        }
    }

    private static void EmitSharedSetters(
        SourceProductionContext spc, StringBuilder sb, SetterHost host, Dictionary<string, int> sharedBits)
    {
        ReportSharedBitOverflow(spc, host, sharedBits);
        foreach (var s in host.Shared)
        {
            // No reachability skip, and none needed: every event property on the shared surface is a
            // `Callback` carrier — a struct, not a delegate — so it cannot swallow its own setter even
            // though the setter's receiver is the component itself.
            //
            // Once, over the component: there is one chain shape now (see "THE CHAIN'S RECEIVER IS THE
            // COMPONENT" below), so `Input.Bind(…).Class("x")` and `Ui.DataGrid.Data(…).RowKey(…).Class("x")`
            // hand back exactly the control or grid they were called on, and the next step still knows
            // its type. A form control or a grid that could not say `.Class(…)` would be no trade at all.
            //
            // The GRID shape takes only the COMPONENT-owned half, and that is a measurement rather
            // than a policy: of the 121 shared members, 120 are constrained `where T : Element` and a
            // column host is a Component — a grid renders a table, it is not one. Emitting those over
            // the grid shape put 120 extensions no grid can call into every compilation that
            // references Rask.Core, and 240 unreachable entries into its recorded public API. The one
            // that remains is Key, which every chain needs.
            EmitSetter(sb, s.Name, s.TypeFqn, s.Owner, s.IsDelegate, wrap: false, generic: true,
                fold: FoldsIntoPropsChanged(s.Name, s.IsDelegate, autoRerender: false),
                pendingBit: Bit(sharedBits, s.Name), summary: s.Summary);
        }
    }

    private static void EmitComponentSetters(StringBuilder sb, ImmutableArray<Candidate> candidates)
    {
        foreach (var c in DistinctByType(candidates))
        {
            // FullyQualifiedName already carries the type arguments (`Input<T>`); appending
            // TypeParameters again would emit `Input<T><T>`.
            var self = c.FullyQualifiedName;
            var visibility = c.IsPublic ? "public" : "internal";
            var ownBits = OwnPendingBits(c);
            // OwnSetterProps is everything the component does not inherit from Rask.Core's
            // Element/Component chain — its own props AND those it inherits from an intermediate base
            // (HtmlMediaElement, UiElement, UiFormField<T>, a consumer's own base). The shared chain is
            // emitted once as constrained generic extensions above and must not be duplicated per tag;
            // an intermediate base has no such emission, so skipping it left those props with no setter
            // at all (every kit control's Label/Size, every media element's Src). The receiver
            // stays the CONCRETE component so the chain keeps its type — a `UiFormField<T>`-typed
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
                    !folded && FoldsIntoPropsChanged(p.Name, p.IsDelegate, p.IsAutoRerenderDelegate),
                    AnnotateDecl(c, c.TypeParameters), c.TypeParameterConstraints, visibility, Bit(ownBits, p.Name),
                    p.Summary);
            }

            EmitEnumSteps(sb, c, self, visibility, AnnotateDecl(c, c.TypeParameters), c.TypeParameterConstraints);
            EmitBoundSetters(sb, c, visibility);
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
        foreach (var s in host.Shared.Where(s => next < OwnPendingBit && FoldsIntoPropsChanged(s.Name, s.IsDelegate, autoRerender: false)))
        {
            bits[s.Name] = next++;
        }

        return bits;
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
            !p.IsSharedSurfaceProp && !p.IsInitOnly && !string.Equals(p.Name, "Children", StringComparison.Ordinal) && !p.IsBoundInterfaceProp
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
                           || !FoldsIntoPropsChanged(p.Name, p.IsDelegate, p.IsAutoRerenderDelegate))
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

            AppendSharedEagerReset(sb, kind, receiver, props, bits, elementOwned);

            AppendSharedPendingReset(sb, kind, receiver, pending, bits, elementOwned);

            if (!elementOwned)
            {
                EmitCopyWritten(
                    sb,
                    "    public static void CopyComponentWritten(",
                    receiver,
                    baseCall: null,
                    pending.Select(s => (s.Name, s.TypeFqn, bits[s.Name])).ToList(),
                    props.Where(s => !bits.ContainsKey(s.Name)).Select(s => s.Name).ToList(),
                    constraints: string.Empty);
            }

            sb.Append("    /// <summary>Every folding bit <c>").Append(kind).AppendLine("</c> owns.</summary>");
            sb.Append("    public const ulong Shared").Append(kind).Append("Pending = ")
                .Append(MaskLiteral(pending.Select(s => bits[s.Name]))).AppendLine(";");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        spc.AddSource("RaskBuilderReset.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void AppendSharedEagerReset(
        StringBuilder sb, string kind, string receiver, List<SharedSetter> props, Dictionary<string, int> bits, bool elementOwned)
    {
        sb.Append("    /// <summary>Puts <c>").Append(kind)
            .AppendLine("</c>'s non-folding props back where a fresh component holds them.</summary>");
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
    }

    private static void AppendSharedPendingReset(
        StringBuilder sb, string kind, string receiver, List<SharedSetter> pending, Dictionary<string, int> bits, bool elementOwned)
    {
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
    private static bool DeclaresDerivedSetter(IPropertySymbol p)
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

            AppendOwnEagerReset(sb, c, visibility, eager);

            sb.Append("    ").Append(visibility).Append(" static void ").Append(PendingResetName(c))
                .Append(AnnotateDecl(c, c.TypeParameters)).Append("(global::Rask.Core.Component __c0, ulong __p)")
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

            // An element keeps positional identity and is never claimed by a key, so it needs no copy.
            if (!c.IsElement)
            {
                EmitCopyWritten(
                    sb,
                    "    " + visibility + " static void " + CopyWrittenName(c) + AnnotateDecl(c, c.TypeParameters) + "(",
                    c.FullyQualifiedName,
                    baseCall: "global::Rask.Core.BuilderRuntime.CopyComponentWritten",
                    pending.Select(p => (p.Name, p.TypeFqn, bits[p.Name])).ToList(),
                    eager.Select(static p => p.Name).ToList(),
                    c.TypeParameterConstraints);
            }
        }
    }

    private static void AppendOwnEagerReset(StringBuilder sb, Candidate c, string visibility, List<PropInfo> eager)
    {
        sb.Append("    ").Append(visibility).Append(" static void ").Append(EagerResetName(c))
            .Append(AnnotateDecl(c, c.TypeParameters)).Append("(global::Rask.Core.Component __c0)")
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
    }

    // Replays the steps a chain wrote before its Key step onto the instance that key claimed (#1118). The
    // FOLDING props copy only where their bit says the chain named them, through Track so the fold still
    // reports the change; the rest copy unconditionally, because the provisional instance holds exactly what
    // the entry's eager reset left there plus whatever the chain wrote — which is what the kept instance
    // would hold had the entry built it.
    private static void EmitCopyWritten(
        StringBuilder sb,
        string declaration,
        string receiver,
        string? baseCall,
        List<(string Name, string TypeFqn, int Bit)> folding,
        List<string> others,
        string constraints)
    {
        sb.Append(declaration).Append("global::Rask.Core.Component __f0, global::Rask.Core.Component __t0, ulong __w)")
            .AppendLine(constraints);
        sb.AppendLine("    {");
        if (baseCall is not null)
        {
            sb.Append("        ").Append(baseCall).AppendLine("(__f0, __t0, __w);");
        }

        if (folding.Count != 0 || others.Count != 0)
        {
            var cast = string.Equals(receiver, "global::Rask.Core.Component", StringComparison.Ordinal)
                ? string.Empty
                : "(" + receiver + ")";
            sb.Append("        var __f = ").Append(cast).AppendLine("__f0;");
            sb.Append("        var __t = ").Append(cast).AppendLine("__t0;");
            foreach (var (name, typeFqn, bit) in folding)
            {
                var escaped = EscapeIdentifier(name);
                sb.Append("        if ((__w & ").Append(MaskLiteral(new[] { bit })).AppendLine(") != 0UL)");
                sb.AppendLine("        {");
                sb.Append("            global::Rask.Core.BuilderRuntime.Track<").Append(typeFqn).Append(">(__t, __t.")
                    .Append(escaped).Append(", __f.").Append(escaped).AppendLine(");");
                sb.Append("            __t.").Append(escaped).Append(" = __f.").Append(escaped).AppendLine(";");
                sb.AppendLine("        }");
            }

            foreach (var name in others)
            {
                var escaped = EscapeIdentifier(name);
                sb.Append("        __t.").Append(escaped).Append(" = __f.").Append(escaped).AppendLine(";");
            }
        }

        if (baseCall is null)
        {
            sb.AppendLine("        if (global::Rask.Core.BuilderRuntime.HasCallbacks(__f0))");
            sb.AppendLine("        {");
            sb.AppendLine("            global::Rask.Core.BuilderRuntime.MarkCallbacks(__t0);");
            sb.AppendLine("        }");
        }

        sb.AppendLine("    }");
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

    private static string CopyWrittenName(Candidate c) => "__RaskCopyWritten_" + ResetSuffix(c);

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
    private static List<Candidate> DistinctByType(ImmutableArray<Candidate> candidates) =>
        candidates
            .GroupBy(static c => c.FullyQualifiedName, StringComparer.Ordinal)
            .Select(static g => g.First())
            .OrderBy(static c => c.FullyQualifiedName, StringComparer.Ordinal)
            .ToList();
}
