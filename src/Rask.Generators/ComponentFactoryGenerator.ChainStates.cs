using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
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
    ///         may be given in any order — <c>Ui.Stat.Label("…").Value("…")</c> and
    ///         <c>Ui.Stat.Value("…").Label("…")</c> are both chains and both end at the component. That costs
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

            foreach (var name in byNamespace.GroupBy(static c => c.EntryName, StringComparer.Ordinal))
            {
                EmitStateTypes(sb, [.. name], scoped ? "    " : string.Empty, assemblyName, runtimePrefix);
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
        StringBuilder sb, List<Candidate> group, string pad, string assemblyName, string runtimePrefix)
    {
        // The seed is the entry's, and every component joined to it puts its openings inside — so the
        // states below are emitted for each of them, while the seed is written once.
        //
        // The entry is NAMED after one of them, and that one owns the seed's explicit type opening:
        // `Of<T>()` takes no argument, so every joined component would emit the SAME signature and only
        // one can survive. Whichever survived would then decide what `Ui.Select.Of<string>()` builds —
        // silently, since the chain that follows is identical. So put the namesake first, however the
        // candidates happened to be sorted, and let it be the one `primary` means.
        group = [.. group.OrderBy(static x =>
            string.Equals(x.EntryName, x.TypeName, StringComparison.Ordinal) ? 0 : 1)];
        var c = group[0];
        const string hidden =
            "[global::System.ComponentModel.EditorBrowsable("
            + "global::System.ComponentModel.EditorBrowsableState.Never)]";
        var visibility = c.IsPublic ? "public" : "internal";
        var required = RequiredSteps(c);

        sb.Append(pad).Append("/// <summary>Where a ").Append(c.TypeName)
            .AppendLine(" chain starts. Take one of its steps to begin.</summary>");
        sb.Append(pad).AppendLine(hidden);
        sb.Append(pad).Append(visibility).Append(" readonly struct ").Append(SeedName(c)).AppendLine();
        sb.Append(pad).AppendLine("{");

        // Key can come FIRST — before the required steps, not merely before the optional ones (#685). It
        // decides WHICH instance the chain is building, and naming that before anything else is what reads
        // best. (Written later it is also correct since #1118: ClaimKey carries the earlier steps across.)
        //
        // The seed itself holds no component — the first step constructs one — so this step constructs,
        // claims, and hands back the state that still awaits everything. The required steps then assign
        // onto the instance the key settled on, which is the whole point.
        //
        // A generic component too (#1118). Its type arguments are still to be pinned by the opening, but the
        // step hands back the SEED, not a state, so there is nothing to name yet — and without it the only
        // sound spelling, Key first, did not compile (CS0315) and a generic control could not be keyed at all.
        var seedCarriesKey = required.Count > 0 || c.TypeParameters.Length > 0;
        if (seedCarriesKey)
        {
            sb.Append(pad).AppendLine("    private readonly object? _key;");
            sb.Append(pad).AppendLine();
            sb.Append(pad).Append("    internal ").Append(SeedName(c)).AppendLine("(object? key) => _key = key;");
            sb.Append(pad).AppendLine();
            sb.Append(pad).AppendLine(
                "    /// <summary>Sets the reconciliation identity — which item this is.</summary>");
            sb.Append(pad).Append("    public ").Append(SeedName(c)).AppendLine(" Key(object? key) => new(key);");
            sb.Append(pad).AppendLine();
        }

        foreach (var joined in group)
        {
            EmitSeedOpenings(sb, joined, pad + "    ", assemblyName, runtimePrefix, seedCarriesKey, joined == c);
        }

        sb.Append(pad).AppendLine("}");

        foreach (var joined in group)
        {
            EmitStagesAndStates(sb, joined, pad, assemblyName, runtimePrefix);
        }
    }

    // What a chain can start with, inside the entry's seed: the explicit type opening (only for the
    // component the entry is named after — a joined one would emit the same `Of<T>()` signature), then
    // one step per opening.
    private static void EmitSeedOpenings(
        StringBuilder sb, Candidate c, string pad, string assemblyName, string runtimePrefix,
        bool seedCarriesKey, bool primary)
    {
        var required = RequiredSteps(c);
        var openings = Openings(c);
        if (primary)
        {
            EmitExplicitTypeOpening(sb, c, pad, assemblyName, runtimePrefix, required, seedCarriesKey);
        }

        foreach (var opening in openings)
        {
            if (opening.Count == 0)
            {
                // Nothing to pin, so the component is constructible from the word go and any one of its
                // required properties opens the chain.
                foreach (var first in required)
                {
                    EmitBuildingStep(
                        sb, c, pad, assemblyName, runtimePrefix, first, [],
                        new HashSet<string>(StringComparer.Ordinal) { first.PropertyName },
                        seedCarriesKey);
                }

                continue;
            }

            if (opening.Count == 2)
            {
                // The stage is reached by the opening step, so it is instantiated in the mode that step
                // chose — the chain is in one from here on.
                var stageParams = TypeParametersFor(c, opening[0]);
                sb.Append(pad).Append("public ")
                    .Append(StageFqn(c, opening[0], stageParams)).Append(' ')
                    .Append(EscapeIdentifier(opening[0].ParamName)).Append(AnnotateDecl(c, stageParams)).Append('(')
                    .Append(StepParamType(opening[0])).Append(' ')
                    .Append(EscapeIdentifier(opening[0].ParamName)).Append(')')
                    .AppendLine(ConstraintsDeclaredBy(c, stageParams));
                sb.Append(pad).Append("    => new(").Append(EscapeIdentifier(opening[0].ParamName))
                    .AppendLine(seedCarriesKey ? ", _key);" : ", null);");
                continue;
            }

            EmitBuildingStep(
                sb, c, pad, assemblyName, runtimePrefix, opening[0], [],
                SatisfiedBy(c, opening), seedCarriesKey);
        }
    }

    // The stage an opening of two pins needs, and one state per reachable subset of satisfied required
    // properties — per joined component, since each keeps its own.
    private static void EmitStagesAndStates(
        StringBuilder sb, Candidate c, string pad, string assemblyName, string runtimePrefix)
    {
        const string hidden =
            "[global::System.ComponentModel.EditorBrowsable("
            + "global::System.ComponentModel.EditorBrowsableState.Never)]";
        var visibility = c.IsPublic ? "public" : "internal";
        var required = RequiredSteps(c);
        var openings = Openings(c);

        foreach (var opening in openings.Where(static o => o.Count == 2))
        {
            EmitStage(sb, c, opening, pad, assemblyName, runtimePrefix, visibility, hidden);
        }

        foreach (var state in ReachableStates(c))
        {
            EmitState(sb, c, state, required, pad, visibility, hidden);
        }
    }

    private static void EmitStage(
        StringBuilder sb, Candidate c, List<EntryInference> opening, string pad, string assemblyName, string runtimePrefix,
        string visibility, string hidden)
    {
        var stageParams = TypeParametersFor(c, opening[0]);
        sb.Append(pad).Append("/// <summary>").Append(c.TypeName).Append(" awaiting ")
            .Append(opening[1].ParamName).AppendLine(", which fixes the rest of its type.</summary>");
        sb.Append(pad).AppendLine(hidden);
        sb.Append(pad).Append(visibility).Append(" readonly struct ").Append(StageName(c, opening[0]))
            .Append(AnnotateDecl(c, stageParams))
            .AppendLine(ConstraintsDeclaredBy(c, stageParams));
        sb.Append(pad).AppendLine("{");
        sb.Append(pad).Append("    internal ").Append(StageName(c, opening[0])).Append('(')
            .Append(StepParamType(opening[0])).Append(" value, object? key)").AppendLine();
        sb.Append(pad).AppendLine("    {");
        sb.Append(pad).Append("        ").Append(opening[0].ParamName).AppendLine(" = value;");
        sb.Append(pad).AppendLine("        _key = key;");
        sb.Append(pad).AppendLine("    }");
        sb.Append(pad).AppendLine();
        // The key the seed was carrying when the opening step reached this stage, claimed by the step that
        // builds the component.
        sb.Append(pad).AppendLine("    private readonly object? _key;");
        sb.Append(pad).AppendLine();
        sb.Append(pad).Append("    private ").Append(StepParamType(opening[0])).Append(' ')
            .Append(opening[0].ParamName).AppendLine(" { get; }");
        sb.Append(pad).AppendLine();

        EmitBuildingStep(
            sb, c, pad + "    ", assemblyName, runtimePrefix, opening[1],
            [(opening[0], "this." + EscapeIdentifier(opening[0].ParamName))],
            SatisfiedBy(c, opening), carriesKey: true);

        EmitIdentityStep(sb, c, pad + "    ", assemblyName, runtimePrefix, opening);

        sb.Append(pad).AppendLine("}");
    }

    private static void EmitState(
        StringBuilder sb, Candidate c, HashSet<string> state, List<EntryInference> required, string pad,
        string visibility, string hidden)
    {
        sb.Append(pad).Append("/// <summary>").Append(c.TypeName).Append(" still awaiting ")
            .Append(string.Join(", ", required.Where(r => !state.Contains(r.PropertyName))
                .Select(r => r.ParamName)))
            .AppendLine(".</summary>");
        sb.Append(pad).AppendLine(hidden);
        sb.Append(pad).Append(visibility).Append(" readonly struct ").Append(StateName(c, state))
            .Append(AnnotateDecl(c, c.TypeParameters))
            .AppendLine(ConstraintsDeclaredBy(c, c.TypeParameters));
        sb.Append(pad).AppendLine("{");
        sb.Append(pad).Append("    internal ").Append(StateName(c, state)).Append('(')
            .Append(c.FullyQualifiedName).AppendLine(" component) => Component = component;");
        sb.Append(pad).AppendLine();
        sb.Append(pad).Append("    private ").Append(c.FullyQualifiedName)
            .AppendLine(" Component { get; }");

        // Key is offered here, not only on the finished chain, so it can come FIRST (#685): it decides
        // which instance is being built, and saying which item this is before anything else reads best.
        // Returns the same state, so it composes anywhere in the required sequence and changes nothing
        // about what is still outstanding.
        sb.Append(pad).AppendLine();
        sb.Append(pad).AppendLine(
            "    /// <summary>Sets the reconciliation identity — which item this is.</summary>");
        sb.Append(pad).Append("    public ").Append(StateName(c, state))
            .Append(c.TypeParameters)
            .AppendLine(" Key(object? key)");
        sb.Append(pad).AppendLine("    {");
        sb.Append(pad).AppendLine(
            "        var __c = global::Rask.Core.BuilderRuntime.ClaimKey(Component, key);");
        sb.Append(pad).AppendLine("        __c.Key = key;");
        sb.Append(pad).AppendLine("        return new(__c);");
        sb.Append(pad).AppendLine("    }");

        EmitRequiredSteps(sb, c, state, required, pad);

        sb.Append(pad).AppendLine("}");
    }

    private static void EmitRequiredSteps(
        StringBuilder sb, Candidate c, HashSet<string> state, List<EntryInference> required, string pad)
    {
        foreach (var step in required.Where(r => !state.Contains(r.PropertyName)))
        {
            var next = new HashSet<string>(state, StringComparer.Ordinal) { step.PropertyName };
            var done = next.Count == required.Count;
            sb.Append(pad).AppendLine();
            EmitDocComment(sb, step.Summary, pad + "    ");
            sb.Append(pad).Append("    public ")
                .Append(done
                    ? c.FullyQualifiedName
                    : StateFqn(c, next) + c.TypeParameters)
                .Append(' ').Append(EscapeIdentifier(step.ParamName)).Append('(')
                .Append(StepParamType(step)).Append(' ').Append(EscapeIdentifier(step.ParamName))
                .AppendLine(")");
            sb.Append(pad).AppendLine("    {");
            sb.Append(pad).AppendLine("        var __c = Component;");
            EmitPinAssignment(sb, step, EscapeIdentifier(step.ParamName), pad + "    ");
            // The last step hands back the COMPONENT, which is what the chain is now — so it returns
            // the receiver rather than wrapping it. An earlier one still hands on the state struct
            // that is waiting for the rest, and that is a target-typed `new` as it always was.
            sb.Append(pad).AppendLine(done ? "        return __c;" : "        return new(__c);");
            sb.Append(pad).AppendLine("    }");
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
    //
    // A generic FORM CONTROL is the exception, and it is not a special case so much as the same rule
    // read properly: a form control's openings are its MODE pins, `Bind` and `Value`, so a required step
    // of its own is never one and never gets to pin the type. `UiInput<T>` requires a `Label` — which
    // says nothing about T — so without this a controlled call site with no starting value has no way in
    // at all, and `Ui.Input.Value("")` is a value invented to satisfy the compiler rather than the field.
    private static void EmitExplicitTypeOpening(
        StringBuilder sb, Candidate c, string pad, string assemblyName, string runtimePrefix,
        List<EntryInference> required, bool carriesKey)
    {
        if (c.TypeParameters.Length == 0)
        {
            return;
        }

        // A control opened this way was given no value at all, so the parent still owns whatever it ends
        // up with: that is the controlled mode, and `Of` is the way into it for a control that wants only
        // the element half — `Input.Of<string>().Type(Search).Placeholder("…")`.
        //
        // With required steps outstanding it hands back the STATE that still owes them rather than the
        // component, so stating the type argument never skips them. That is what lets a component whose
        // type no step can pin be built at all: Ui.DataGrid's rows can arrive through `Source`, whose
        // carrier infers nothing (see IsFuncLikeDelegate), and withholding `Of` outright left that grid
        // with no way in once RowKey became required.
        var pending = required.Count != 0;

        // …but only where that state is one the chain can actually be in. A component whose OPENING is
        // its required step — `Form.Model(m)` — never owes everything at once, so no such state is
        // emitted and naming one here would be a reference to a type that does not exist.
        if (pending && !ReachableStates(c).Any(static s => s.Count == 0))
        {
            return;
        }

        var result = pending
            ? StateFqn(c, []) + c.TypeParameters
            : c.FullyQualifiedName;

        sb.Append(pad).Append("/// <summary>Opens a ").Append(c.TypeName)
            .AppendLine(" whose type argument is stated rather than inferred.</summary>");
        sb.Append(pad).Append("public ").Append(result).Append(" Of").Append(AnnotateDecl(c, c.TypeParameters))
            .Append("()").AppendLine(c.TypeParameterConstraints);
        sb.Append(pad).AppendLine("{");
        sb.Append(pad).Append("    var __c = ").Append(runtimePrefix).Append(EntryMethod(c))
            .Append(c.FullyQualifiedName).Append(">(");
        EmitResetArguments(sb, c, assemblyName, c.TypeParameters);
        sb.AppendLine(");");
        if (carriesKey)
        {
            EmitCarriedKeyClaim(sb, pad, "_key");
        }

        sb.Append(pad).AppendLine(pending ? "    return new(__c);" : "    return __c;");
        sb.Append(pad).AppendLine("}");
        sb.AppendLine();
    }

    // Claims the key a seed or a stage carried, onto the component the step just built — BEFORE any pin is
    // assigned, so every one lands on the instance the key settles on.
    private static void EmitCarriedKeyClaim(StringBuilder sb, string pad, string key)
    {
        sb.Append(pad).Append("    __c = global::Rask.Core.BuilderRuntime.ClaimKey(__c, ").Append(key).AppendLine(");");
        sb.Append(pad).Append("    if (").Append(key).Append(" is not null) { __c.Key = ").Append(key).AppendLine("; }");
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
        List<EntryInference> opening)
    {
        if (!TryIdentityShape(c, opening, out var value, out var item, out var projection))
        {
            return;
        }

        var unified = RenameTypeParameter(c.FullyQualifiedName, item, value);
        sb.Append(pad).Append("public ").Append(unified).Append(' ')
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
        // The stage carries the seed's key (#1118); claim it before the pins, as every building step does.
        EmitCarriedKeyClaim(sb, pad, "_key");
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
        sb.Append(pad).AppendLine("    return __c;");
        sb.Append(pad).AppendLine("}");
    }

    // Whether a stage can take the identity overload: two type parameters, one pinned by the stage (the
    // value) and one not (the item), with the item→value projection the only step still outstanding.
    private static bool TryIdentityShape(
        Candidate c, List<EntryInference> opening, out string value, out string item, out EntryInference projection)
    {
        value = item = string.Empty;
        projection = default;
        var names = OrderedTypeParameters(c.TypeParameters);
        if (names.Count != 2)
        {
            return false;
        }

        var pinnedByStage = MentionedTypeParameters(opening[0].ParamTypeFqn, ParseTypeParameters(c.TypeParameters));
        if (names.FirstOrDefault(pinnedByStage.Contains) is not { } pinned
            || names.FirstOrDefault(n => !pinnedByStage.Contains(n)) is not { } free)
        {
            return false;
        }

        value = pinned;
        item = free;

        // The only thing still outstanding after this step must be the projection itself.
        var satisfied = SatisfiedBy(c, opening);
        satisfied.Add(opening[1].PropertyName);
        var outstanding = RequiredSteps(c).Where(r => !satisfied.Contains(r.PropertyName)).ToList();
        if (outstanding.Count != 1)
        {
            return false;
        }

        projection = outstanding[0];
        // A step's parameter is the property's declared type, so a nullable one keeps its `?`; the shape
        // comparison is about the delegate itself.
        var expected = "global::System.Func<" + item + ", " + value + ">";
        return string.Equals(StepParamType(projection).TrimEnd('?'), expected, StringComparison.Ordinal);
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
        HashSet<string> satisfied, bool carriesKey = false)
    {
        var required = RequiredSteps(c);
        var done = satisfied.Count == required.Count;
        var result = done
            ? c.FullyQualifiedName
            : StateFqn(c, satisfied) + c.TypeParameters;
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
            .Append(EscapeIdentifier(step.ParamName)).Append(AnnotateDecl(c, methodParams)).Append('(')
            .Append(StepParamType(step)).Append(' ').Append(EscapeIdentifier(step.ParamName)).Append(')')
            .AppendLine(ConstraintsDeclaredBy(c, methodParams));
        sb.Append(pad).AppendLine("{");
        sb.Append(pad).Append("    var __c = ").Append(runtimePrefix).Append(EntryMethod(c))
            .Append(c.FullyQualifiedName).Append(">(");
        EmitResetArguments(sb, c, assemblyName, c.TypeParameters);
        sb.AppendLine(");");

        // The key the seed was carrying, applied BEFORE any property is assigned — which is the whole
        // reason the seed carries it (#685). Claiming settles which instance the chain is
        // building, so every pin below lands on that one rather than on an instance about to be
        // discarded. A default seed carries null, and a null key claims nothing.
        if (carriesKey)
        {
            EmitCarriedKeyClaim(sb, pad, "_key");
        }

        foreach (var (pin, value) in carried)
        {
            EmitPinAssignment(sb, pin, value, pad);
        }

        EmitPinAssignment(sb, step, EscapeIdentifier(step.ParamName), pad);
        // The component itself when this step completed it — the chain is the component now — otherwise
        // a target-typed `new` for the state that still wants something.
        sb.Append(pad).AppendLine(done ? "    return __c;" : "    return new(__c);");
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
            if (!IsRequiredFactoryParam(p) || p.IsInitOnly || string.Equals(p.Name, "Children", StringComparison.Ordinal) || p.IsSharedSurfaceProp)
            {
                continue;
            }

            // A form control's Value is its OPENING, never a step outstanding after one.
            //
            // It only ever looks required on a control closed over a value type — `IFormControl<T>`
            // declares `T? Value`, where `?` over an unconstrained T is a nullability annotation, so
            // `IFormControl<bool>` has a plain non-nullable `bool Value` and RASK001's rule reads it as
            // required. Left in the required set it is unsatisfiable in BOUND mode, which withdraws
            // Value on purpose: `Ui.Checkbox.Bind(() => m.Agreed)["…"]` would sit forever in a
            // pending state waiting for a step its own mode does not offer, and the only symptom is
            // that the chain has no ToHtml. Controlled mode loses nothing — opening on `Value(…)` is
            // how the value arrives there, and it is still the only way in.
            if (c.FormControl is not null && p.Name is "Bind" or "Value")
            {
                continue;
            }

            steps.Add(new EntryInference(
                p.Name,
                p.TypeFqn,
                p.Name,
                FoldsIntoPropsChanged(p.Name, p.IsDelegate, p.IsAutoRerenderDelegate),
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
        return WithCollectionShapes(c, valueFirst.Count != 0 ? valueFirst : sets);
    }

    // A control whose value IS a collection shares an entry with its single-valued sibling, and the two
    // openings have to be told apart by the argument alone. Bind manages it; Value cannot, and the
    // difference is the whole shape of this method.
    //
    // BIND takes a lambda, so the argument names a CONCRETE type — `() => model.Tags` is
    // `Expression&lt;Func&lt;List&lt;string&gt;&gt;&gt;`. Against `Expression&lt;Func&lt;T&gt;&gt;` that
    // is an exact match with T = List&lt;string&gt;, and exact beats the interface conversion to
    // `ICollection&lt;T&gt;` — so a field holding many answers would quietly get the control that holds
    // one, compiling and rendering and reporting nothing. One overload per concrete shape puts an exact
    // match on this side of the choice too, and the shapes never tie with each other because
    // `Expression&lt;T&gt;` is invariant. Each goes through a widening lift, which rebuilds the
    // expression around a Convert that ExpressionAccessor.Parse then strips, so the member it reads and
    // writes is still the model's own.
    //
    // VALUE takes the collection itself, and the two arguments people actually write — a collection
    // expression and a bare null — are target-typed: `["a", "b"]` and `null` fit EVERY shape equally, so
    // more overloads only turn one silent mistake into an ambiguity error. Nor does overload priority
    // rescue it: whatever wins `null` wins it for the single-valued control too. So the collection's
    // controlled opening is named **Values**, takes the interface alone, and collides with nothing —
    // `Ui.Select.Values(["core", "ui"])` beside `Ui.Select.Value("core")`, and `Bind` shared by both.
    private static List<List<EntryInference>> WithCollectionShapes(
        Candidate c, List<List<EntryInference>> openings)
    {
        if (c.FormControl is not { CollectionElementFqn: { } item } fc)
        {
            return openings;
        }

        var widened = new List<List<EntryInference>>();
        foreach (var opening in openings)
        {
            // Only a one-step opening. A two-step one is named after its first step, so every shape
            // would want the same stage type and they would collide rather than overload.
            if (opening.Count != 1)
            {
                widened.Add(opening);
                continue;
            }

            if (string.Equals(opening[0].PropertyName, "Value", StringComparison.Ordinal))
            {
                widened.Add([opening[0] with { ParamName = "Values" }]);
                continue;
            }

            widened.Add(opening);
            if (!string.Equals(opening[0].PropertyName, "Bind", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var shape in CollectionShapes(item))
            {
                widened.Add([
                    opening[0] with
                    {
                        ParamTypeFqn = opening[0].ParamTypeFqn.Replace(fc.ValueTypeFqn, shape),
                        Lift = "global::Rask.Core.Forms.ExpressionAccessor.AsCollection<"
                               + shape + ", " + item + ">",
                    },
                ]);
            }
        }

        return widened;
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
        var assigned = pin.Lift is null ? value : pin.Lift + "(" + value + ")";

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
}
