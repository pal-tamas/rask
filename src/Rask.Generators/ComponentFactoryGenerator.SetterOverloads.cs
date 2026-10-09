using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    // A setter is named after the property it writes. Always — including a DELEGATE property, which used
    // to be the one exception: an extension method could not share a delegate prop's name, because C#
    // resolves `x.OnClick(fn)` against the property and reads it as an invocation (CS1593). The rule
    // dropped a leading `On` to dodge that (`OnRate` -> `.Rate(…)`), and where it could not, the property
    // had no reachable setter at all and RASK042 asked the author to wrap the delegate in a carrier.
    //
    // Both are gone because every callback property is now a carrier — `Callback`/`Callback<T>` for an
    // event, `Fn<…>` for a template or selector — a STRUCT rather than a delegate. The chain's receiver
    // is the component, so the property IS on the receiver; being non-invocable, it does not stop the
    // lookup, the call falls through to the extension setter, and the setter keeps the property's name.

    // The bound half of an IFormControl<T> control: one setter per interface member, typed from the
    // interface's T rather than from the declaring class. That matters twice — the members may be
    // inherited from a non-Element base (Ui.RadioGroup's UiRadioGroup<T> gets them from UiFormField<T>, which the
    // depth-0 rule above would skip), and it is what lets the generic entry take only `Bind`:
    //
    //     Input.Bind(() => _form.Name).Validate(ProductName.Validate).Id("name")
    //
    // replaces the factory's none/sync/async overload fan-out, whose only purpose was to make
    // Validate a required, correctly-typed parameter. Never auto-wrapped: a validator is not an event
    // callback (it RETURNS the messages, and is called during a render rather than dispatched to one),
    // and AfterBind is a post-bind hook (the bound factory has always assigned them raw).
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
            // One rule taking either shape, for the same reason AfterBind below is one hook: the carrier
            // is what gives the step its sync and async overloads (see EmitCarrierOverloads) in place of
            // the `…Async` sibling that used to sit beside it here.
            ("Validate", ValidatorFqn + "<" + t + ">?"),
            // One post-bind hook taking either shape. Typed as the CARRIER, which is what gives it the
            // sync and async step overloads (see EmitCarrierOverloads) in place of the sibling that used
            // to sit beside it here.
            ("AfterBind", CallbackFqn + "<" + t + ">"),
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
            // mode: these are the BOUND members. With the component as the receiver they are ordinary
            // setters on it, so an AfterBind step after a Value opening compiles and is simply never read —
            // what stays exclusive is the pair of OPENINGS, which live only on the seed.
            EmitSetter(sb, name, typeFqn, c.FullyQualifiedName, isDelegate: false, wrap: false, generic: false,
                fold: false, AnnotateDecl(c, c.TypeParameters), c.TypeParameterConstraints, visibility,
                pendingBit: -1, summary: summary);
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
    ///     <i>typed</i> callback into — <c>Form.OnSubmit</c> and <c>OnInvalidSubmit</c>, whose generic
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
        c.GenericFactory is { } gf && gf.TypedDelegateProperties.Contains(propName, StringComparer.Ordinal);

    // Opt-in wrapping for a callback an ELEMENT-derived component invokes itself rather than handing to
    // the DOM. Matched by name so Rask.Core's own attribute needs no symbol threaded through here.
    private static bool HasAutoCallbackAttribute(ISymbol prop) =>
        prop.GetAttributes().Any(a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), "Rask.Core.AutoCallbackAttribute", StringComparison.Ordinal));

    private static bool FoldsIntoPropsChanged(string name, bool isDelegate, bool autoRerender) =>
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

    /// <summary>
    ///     The most members an enum may have before its values stop being styles and start being names.
    ///     <c>Ui.Color</c> has 22 — Tailwind's hues — and each reads as a step, <c>Ui.Button.Primary.Blue</c>;
    ///     <c>Ui.IconName</c> has hundreds, and <c>Ui.Button.ChevronRight</c> would say the button IS a
    ///     chevron rather than that it shows one.
    /// </summary>
    private const int MaxEnumStepMembers = 24;

    /// <summary>
    ///     An enum-typed property's type and members, for the per-member steps — or empty when its values
    ///     are names rather than a small closed set of styles.
    /// </summary>
    private static (string Type, string Members) EnumStepSource(ITypeSymbol type)
    {
        // A nullable enum prop (`UiTone? Tone`) is the usual shape: the value is optional, and the step
        // supplies one. Unwrap before asking, or every Ui component would be skipped.
        var underlying = type is INamedTypeSymbol { IsGenericType: true, ConstructedFrom.SpecialType: SpecialType.System_Nullable_T } n
            ? n.TypeArguments[0]
            : type;

        if (underlying is not INamedTypeSymbol { TypeKind: TypeKind.Enum } e
            || e.DeclaredAccessibility != Accessibility.Public)
        {
            return ("", "");
        }

        // Never the BCL's. Its enums are VALUES a property takes, not descriptions of the component, and
        // a step reads as the latter: `UiDatePicker.Sunday` says the picker is Sunday rather than that its
        // week starts there. Rask's own enums — and an app's — are named to read as steps because whoever
        // wrote them was choosing words for this chain.
        if (e.ContainingAssembly?.Name is { } assembly
            && (assembly.StartsWith("System", StringComparison.Ordinal)
                || assembly.Equals("mscorlib", StringComparison.Ordinal)
                || assembly.Equals("netstandard", StringComparison.Ordinal)
                || assembly.StartsWith("Microsoft.", StringComparison.Ordinal)))
        {
            return ("", "");
        }

        // Nor the keyword enums Rask.Core's DOM build step writes from the spec (AriaLive, Loading, ReferrerPolicy): an
        // attribute's keywords, not a component's styles. As steps they would land on EVERY element — `Div.Polite`,
        // `Img.Lazy` — so the step marks each one [GeneratedCode("Rask.Dom.…")] and it keeps its plain setter.
        if (IsDomKeywordEnum(e))
        {
            return ("", "");
        }

        var members = e.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(static f => f is { IsStatic: true, HasConstantValue: true, DeclaredAccessibility: Accessibility.Public })
            .Select(static f => f.Name)
            .ToArray();

        // Flags are combined, not chosen: `.Bold.Italic` would read as two steps that each REPLACE the
        // other, because a step assigns rather than ors. So a flags enum keeps its plain setter.
        var isFlags = e.GetAttributes().Any(static a => string.Equals(a.AttributeClass?.Name, "FlagsAttribute", StringComparison.Ordinal));

        return members.Length is 0 or > MaxEnumStepMembers || isFlags
            ? ("", "")
            : (e.ToDisplayString(FullyQualifiedNullable), string.Join(",", members));
    }

    // Every enum the DOM build step writes carries the BCL's GeneratedCode with a "Rask.Dom." tool: "Rask.Dom.Aria" for
    // ARIA's, "Rask.Dom.Keywords" for an attribute's keywords and MDN's IDL enums (src/Rask.Dom.Tasks/DomKeywords.cs).
    private static bool IsDomKeywordEnum(INamedTypeSymbol e) =>
        e.GetAttributes().Any(static a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), "System.CodeDom.Compiler.GeneratedCodeAttribute", StringComparison.Ordinal)
            && a.ConstructorArguments.Length > 0
            && a.ConstructorArguments[0].Value is string tool
            && tool.StartsWith(DomBuildTool, StringComparison.Ordinal));

    private const string DomBuildTool = "Rask.Dom.";

    // What Rask.Core's DOM build step marks every typed-ARIA member it writes with — the Aria* properties on Element and
    // the keyword enums they take (src/Rask.Dom.Tasks/AriaEmitter.cs). A BCL attribute, so nothing public is added for it.
    private const string TypedAriaTool = "Rask.Dom.Aria";

    private static bool IsTypedAriaMember(ISymbol symbol) =>
        symbol.GetAttributes().Any(static a =>
            string.Equals(a.AttributeClass?.ToDisplayString(), "System.CodeDom.Compiler.GeneratedCodeAttribute", StringComparison.Ordinal)
            && a.ConstructorArguments.Length > 0
            && string.Equals(a.ConstructorArguments[0].Value as string, TypedAriaTool, StringComparison.Ordinal));

    /// <summary>
    ///     The enum steps a component may offer, with every name that could collide already taken out:
    ///     a member whose name a property of the component already uses, and every member of an enum the
    ///     component holds TWICE — an <c>Icon</c> and a <c>TrailingIcon</c> of one type have no answer to
    ///     which of them <c>.Search</c> would set, so neither gets steps.
    /// </summary>
    private static List<(string Member, string EnumType, string Setter)> EnumSteps(IEnumerable<PropInfo> props)
    {
        var all = props.ToArray();
        var taken = new HashSet<string>(all.Select(static p => p.Name), StringComparer.Ordinal);
        var countByEnum = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var enumType in all.Where(static p => p.EnumMembers.Length > 0).Select(static p => p.EnumType))
        {
            countByEnum.TryGetValue(enumType, out var n);
            countByEnum[enumType] = n + 1;
        }

        var steps = new List<(string, string, string)>();
        foreach (var p in all)
        {
            if (p.EnumMembers.Length == 0 || countByEnum[p.EnumType] > 1)
            {
                continue;
            }

            // First claim wins, deterministically: the properties arrive in declaration order, so two
            // enums sharing a member name give the step to whichever the component declares first
            // rather than to whichever the compiler happened to emit first.
            steps.AddRange(p.EnumMembers.Split(',')
                .Where(member => taken.Add(member))
                .Select(member => (member, p.EnumType, p.Name)));
        }

        return steps;
    }

    /// <summary>
    ///     One step per member of a small enum property, so a value reads as a word rather than as an
    ///     argument: <c>Ui.Button.Primary.Sm</c> beside <c>Ui.Button.Variant(Ui.ButtonVariant.Primary)</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Emitted as extension PROPERTIES, which is what lets them be written without parentheses,
    ///         and they call the property's own setter rather than assigning — so the pending-bits
    ///         bookkeeping that every setter does happens here too, unchanged.
    ///     </para>
    ///     <para>
    ///         The enum setter stays: a step can only name a value the source knows, and
    ///         <c>Tone(order.IsUrgent ? UiTone.Error : UiTone.Neutral)</c> is decided at run time.
    ///     </para>
    /// </remarks>
    private static void EmitEnumSteps(
        StringBuilder sb, Candidate c, string self, string visibility, string typeParameters, string constraints)
    {
        var steps = EnumSteps(OwnSetterProps(c));
        if (steps.Count == 0)
        {
            return;
        }

        // An extension block, rather than `this`-style methods: only a property can be written without
        // parentheses, and `UiButton.Primary()` would not be the sentence this exists for.
        //
        // A generic component's block declares that component's own type parameters and carries their
        // constraints with them — `self` is `UiInput<T>`, and a block that named T without declaring it
        // does not compile (CS0246). Non-generic components, which is nearly all of them, write neither.
        sb.Append("    extension").Append(typeParameters).Append('(').Append(self).Append(" __b)")
            .AppendLine(constraints);
        sb.AppendLine("    {");
        foreach (var (member, enumType, setter) in steps)
        {
            sb.Append("        /// <summary>Sets <c>").Append(setter).Append("</c> to <c>")
                .Append(member).AppendLine("</c>.</summary>");
            sb.Append("        ").Append(visibility).Append(' ').Append(self).Append(' ')
                .Append(EscapeIdentifier(member)).Append(" => __b.").Append(EscapeIdentifier(setter))
                .Append('(').Append(enumType).Append('.').Append(EscapeIdentifier(member)).AppendLine(");");
        }

        sb.AppendLine("    }");
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
        string summary = "")
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
        // A setter written before it landed on the provisional instance; ClaimKey replays those onto the
        // instance the key keeps (#1118), so Key may come anywhere in the chain.
        if (generic && string.Equals(name, "Key", StringComparison.Ordinal))
        {
            EmitKeySetter(sb, typeFqn, receiver, visibility, pendingBit);
            return;
        }

        var (paramType, assigned) = SetterParameter(typeFqn, wrap);
        var track = TrackStatements(name, fold, isDelegate, pendingBit);
        EmitCarrierPriority(sb, typeFqn);

        sb.Append("    ").Append(visibility).Append(" static ");
        if (generic)
        {
            var self = "T";
            sb.Append(self).Append(' ').Append(EscapeIdentifier(setterName))
                .Append("<T>").Append("(this ")
                .Append(self).Append(" __b, ").Append(paramType)
                .Append(" value) where T : ").Append(receiver);
            sb.Append(" { var __c = __b; ").Append(track).Append("__c.").Append(EscapeIdentifier(name))
                .Append(" = ").Append(AddedRule(typeFqn, name, assigned)).AppendLine("; return __b; }");
            EmitAttrBagOverloads(sb, setterName, name, typeFqn, receiver, fold, pendingBit, visibility,
                generic: true);
            EmitCarrierOverloads(sb, setterName, name, typeFqn, receiver, wrap, pendingBit, visibility,
                generic: true);
            EmitShorthandSteps(sb, setterName, typeFqn, "T", "<T>", " where T : " + receiver, visibility);
            return;
        }

        var target = receiver;
        sb.Append(target).Append(' ').Append(EscapeIdentifier(setterName))
            .Append(typeParameters)
            .Append("(this ").Append(target).Append(" __b, ").Append(paramType).Append(" value)")
            .Append(constraints);
        sb.Append(" { var __c = __b; ").Append(track).Append("__c.").Append(EscapeIdentifier(name))
            .Append(" = ").Append(AddedRule(typeFqn, name, assigned)).AppendLine("; return __b; }");
        EmitAttrBagOverloads(sb, setterName, name, typeFqn, receiver, fold, pendingBit, visibility,
            generic: false, typeParameters, constraints);
        EmitCarrierOverloads(sb, setterName, name, typeFqn, receiver, wrap, pendingBit, visibility,
            generic: false, typeParameters, constraints);
        EmitShorthandSteps(sb, setterName, typeFqn, receiver, typeParameters, constraints, visibility);
    }

    /// <summary>
    ///     The two steps that let a common value be written without its ceremony: <c>.Disabled()</c> for
    ///     <c>.Disabled(true)</c> on any <c>bool</c> prop, and <c>.Class("p-4", wide ? "w-full" : null)</c> — joined
    ///     with one space, blanks dropped — beside the one-string <c>Class</c>.
    /// </summary>
    /// <remarks>
    ///     Both forward to the setter just emitted, so the pending-bit and fold bookkeeping happens exactly once, in
    ///     one place. The flag step is a METHOD, not a property: the prop itself is the component's instance
    ///     member of that name and always wins member lookup, so <c>.Disabled</c> would read the value. A single
    ///     string still binds to the ordinary <c>Class(string?)</c> — its normal form beats this one's expanded form.
    /// </remarks>
    private static void EmitShorthandSteps(
        StringBuilder sb, string setterName, string typeFqn, string self, string typeArgs, string where,
        string visibility)
    {
        var escaped = EscapeIdentifier(setterName);
        if (typeFqn is "bool" or "bool?")
        {
            sb.Append("    /// <summary>Sets <c>").Append(setterName).AppendLine("</c> to <c>true</c>.</summary>");
            sb.Append("    ").Append(visibility).Append(" static ").Append(self).Append(' ').Append(escaped)
                .Append(typeArgs).Append("(this ").Append(self).Append(" __b)").Append(where)
                .Append(" => ").Append(escaped).AppendLine("(__b, true);");
        }

        if (string.Equals(typeFqn, "string?", StringComparison.Ordinal)
            && string.Equals(setterName, "Class", StringComparison.Ordinal))
        {
            sb.AppendLine("    /// <summary>Sets <c>Class</c> to these names joined by one space; null and blank ones are left out.</summary>");
            sb.Append("    ").Append(visibility).Append(" static ").Append(self).Append(' ').Append(escaped)
                .Append(typeArgs).Append("(this ").Append(self)
                .Append(" __b, params global::System.ReadOnlySpan<string?> parts)").Append(where)
                .Append(" => ").Append(escaped).AppendLine("(__b, global::Rask.Core.BuilderRuntime.JoinClasses(parts));");
        }
    }

    private static void EmitKeySetter(StringBuilder sb, string typeFqn, string receiver, string visibility, int pendingBit)
    {
        // Emitted ONCE, over the component's own type parameter. This used to be emitted per chain
        // SHAPE (when `Build<T>` and its siblings wrapped the component), which produced the same
        // `Key<T>` twice — CS0111.
        var self = "T";
        sb.Append("    ").Append(visibility).Append(" static ")
            .Append(self).Append(" Key").Append("<T>").Append("(this ").Append(self)
            .Append(" __b, ")
            .Append(typeFqn).Append(" value) where T : ").Append(receiver);
        sb.Append(" { var __c = global::Rask.Core.BuilderRuntime.ClaimKey(__b, value); ");
        if (pendingBit >= 0)
        {
            sb.Append("global::Rask.Core.BuilderRuntime.Written(__c, ")
                .Append(MaskLiteral(new[] { pendingBit })).Append("); ");
        }

        // Returns what ClaimKey handed back, NOT the receiver: settling identity can swap in the
        // instance the key already owns, and returning `__b` would hand on the one it discarded —
        // silently losing every step written after `.Key(…)`. Wrapping it in `new T(…)` is also not
        // available any more (CS0304: T has no new() constraint), which is what makes the mistake
        // easy to make by hand.
        sb.AppendLine("__c.Key = value; return __c; }");
    }

    // `wrap` is the AutoCallback decision, and it is per property: an Element's
    // handlers go to the DOM unwrapped (owner resolution already re-renders, and wrapping would
    // allocate per render), a non-Element component's event callbacks are wrapped, and a form
    // control's bound members (validators, post-bind hooks) are never wrapped at all.
    //
    // Wrap returns a nullable delegate (null in → null out); assigning it to a non-nullable prop
    // needs the null-forgiving `!` (CS8601), the same way the factory's assignment pass does it.
    // A CARRIER-typed setter is the pass-through — it forwards a carrier the caller already holds,
    // and that carrier was wrapped when a delegate was put into it. Wrapping here would wrap the
    // struct (which `Wrap` has no overload for) and, if it did compile, would double-wrap. The
    // delegate overloads beside it do the wrapping; see EmitCarrierOverloads.
    // The setter's parameter type and what it assigns. A non-nullable `Callback` still takes `null` at its
    // pass-through — the spelling for "no handler" that every other step accepts, and which the bare-delegate
    // overloads beside it would otherwise make ambiguous (CS0121). A null is the unset slot, which is what
    // `default` already is.
    private static (string ParamType, string Assigned) SetterParameter(string typeFqn, bool wrap) =>
        IsNonNullableCallback(typeFqn)
            ? (typeFqn + "?", "value.GetValueOrDefault()")
            : (typeFqn, AssignedValue(typeFqn, wrap));

    private static string AssignedValue(string typeFqn, bool wrap)
    {
        var suppression = typeFqn.EndsWith("?", StringComparison.Ordinal) ? string.Empty : "!";
        return wrap && CarrierDelegates(typeFqn).Count == 0
            ? "global::Rask.Core.AutoCallback.Wrap(value)" + suppression
            : "value";
    }

    // An `internal` component cannot appear in a `public` signature (CS0050/CS0051), so the
    // setter's accessibility tracks its component's — the same rule the factory emission uses.
    // The receiver is the component itself. That is the whole reason a callback property is a
    // CARRIER (`Callback`/`Callback<T>`/`Fn<…>`), never an ordinary delegate: C# stops at a
    // delegate-typed property when it resolves `x.OnClick(fn)` and reads the call as an invocation
    // (CS1593), never reaching an extension method. A struct is not invocable, so the lookup falls
    // through and the setter binds. See Rask.Core.Callback.
    // A carrier-typed prop gets bare-delegate overloads beside this setter, and `null` converts to
    // every one of them. Priority makes the carrier-typed one win that call rather than CS0121.
    private static void EmitCarrierPriority(StringBuilder sb, string typeFqn)
    {
        if (CarrierDelegates(typeFqn).Count > 0)
        {
            sb.AppendLine("    [global::System.Runtime.CompilerServices.OverloadResolutionPriority(2)]");
        }
    }

    private static string TrackStatements(string name, bool fold, bool isDelegate, int pendingBit)
    {
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

        return track;
    }

    // `.Validate(a).Validate(b)` runs a, then b if a let the value through: the one step that adds to what
    // an earlier step wrote, where every other replaces it. A rule is non-folding, so the entry has already
    // put it back to nothing and each render composes from empty (BuilderRuntime.Then).
    private static string AddedRule(string typeFqn, string property, string value) =>
        typeFqn.StartsWith(ValidatorFqn + "<", StringComparison.Ordinal) && typeFqn.EndsWith("?", StringComparison.Ordinal)
            ? "global::Rask.Core.BuilderRuntime.Then<" + typeFqn.Substring(ValidatorFqn.Length + 1, typeFqn.Length - ValidatorFqn.Length - 3)
              + ">(__c." + EscapeIdentifier(property) + ", " + value + ")"
            : value;

    private const string CallbackFqn = "global::Rask.Core.Callback";

    private static bool IsNonNullableCallback(string typeFqn) =>
        !typeFqn.EndsWith("?", StringComparison.Ordinal)
        && (string.Equals(typeFqn, CallbackFqn, StringComparison.Ordinal) || typeFqn.StartsWith(CallbackFqn + "<", StringComparison.Ordinal));

    private const string FnFqn = "global::Rask.Core.Fn";

    private const string ValidatorFqn = "global::Rask.Core.Validator";

    private const string TaskFqn = "global::System.Threading.Tasks.Task";

    // A forwarded callback — `.OnClick(() => OnRate.Invoke(i))` — returns the ValueTask Invoke hands back. Without
    // this shape the lambda still compiled, as an Action that dropped the ValueTask unawaited.
    private const string ValueTaskFqn = "global::System.Threading.Tasks.ValueTask";

    /// <summary>
    ///     The delegate shapes a carrier-typed property accepts, or empty when the type is not a carrier.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A carrier holds a delegate without BEING one, which is what lets a step keep the property's
    ///         name — see <c>Rask.Core.Callback</c>. The cost is that a lambda can never reach it: a lambda
    ///         has no type, so no user-defined conversion applies to it (CS1660). The step therefore has to
    ///         offer the bare delegate shapes as OVERLOADS, and this is the list of them.
    ///     </para>
    ///     <para>
    ///         Keyed off the type, never off the property's name — the same rule as <see cref="IsAttrBag" />
    ///         and for the same reason: a naming convention is a thing someone has to remember, and the one
    ///         who forgets gets a step that silently is not there.
    ///     </para>
    /// </remarks>
    private static List<string> CarrierDelegates(string typeFqn)
    {
        var bare = typeFqn.EndsWith("?", StringComparison.Ordinal)
            ? typeFqn.Substring(0, typeFqn.Length - 1)
            : typeFqn;

        var open = bare.IndexOf('<');
        var name = open < 0 ? bare : bare.Substring(0, open);
        var args = open < 0
            ? new List<string>()
            : SplitGenericArguments(bare.Substring(open + 1, bare.Length - open - 2));

        // `Callback` is the sync/async pair: one name, two overloads. `Validator` is the same pair for a
        // rule: the sync one and the async one, whose token is ambient. `Fn` returns a
        // value and so has no async twin — its last
        // type argument is the return type, which is why it takes one overload rather than two.
        // A Callback<T> also takes a handler that ignores its argument — a parameterless OnClick lambda
        // beside one that reads the event's ClientX — stored as it is, so that form allocates nothing.
        return name switch
        {
            CallbackFqn when args.Count == 1 => [
                Generic("global::System.Action", args),
                Generic("global::System.Func", [.. args, TaskFqn]),
                Generic("global::System.Func", [.. args, ValueTaskFqn]),
                "global::System.Action",
                Generic("global::System.Func", [TaskFqn]),
                Generic("global::System.Func", [ValueTaskFqn]),
            ],
            CallbackFqn => [
                Generic("global::System.Action", args),
                Generic("global::System.Func", [.. args, TaskFqn]),
                Generic("global::System.Func", [.. args, ValueTaskFqn]),
            ],
            ValidatorFqn when args.Count == 1 => [
                Generic("global::Rask.Core.Forms.Validate", args),
                Generic("global::System.Func", [.. args, ValidationTaskFqn]),
            ],
            FnFqn when args.Count > 0 => [Generic("global::System.Func", args)],
            _ => [],
        };

        static string Generic(string open, List<string> arguments) =>
            arguments.Count == 0 ? open : open + "<" + string.Join(", ", arguments) + ">";
    }

    // Top-level commas only: `Fn<Exception, Action, string>` has three arguments, but
    // `Callback<IReadOnlyList<string>>` has one and its comma-free inner text must not be split on a
    // comma that a nested argument list owns.
    private static List<string> SplitGenericArguments(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(text.Substring(start, i - start).Trim());
                    start = i + 1;
                    break;
            }
        }

        parts.Add(text.Substring(start).Trim());
        return parts;
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
    ///     One extra step per delegate shape a carrier-typed property accepts, so the call site writes the
    ///     handler it means — <c>.OnClick(Refresh)</c> or <c>.OnClick(SaveAsync)</c> — under one name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These are not a convenience. A lambda has no type, so it can never reach the carrier through
    ///         the carrier's own constructor (CS1660): without an overload taking the bare delegate there is
    ///         no way to write a handler at all. The carrier-typed setter stays as the pass-through, for
    ///         forwarding a carrier a component already holds.
    ///     </para>
    ///     <para>
    ///         The pass-through carries <c>[OverloadResolutionPriority(2)]</c> — above the Task shapes at 1 — because <c>null</c> converts
    ///         to every one of these and would otherwise be ambiguous (CS0121) — and passing <c>null</c> for
    ///         a handler is a spelling the framework has always blessed. Priority filtering runs AFTER
    ///         applicability and a lambda is never applicable to the struct, so the sync and async lambdas
    ///         still bind to their own overloads.
    ///     </para>
    /// </remarks>
    private static void EmitCarrierOverloads(
        StringBuilder sb, string setterName, string propertyName, string typeFqn, string receiver,
        bool wrap, int pendingBit, string visibility, bool generic,
        string typeParameters = "", string constraints = "")
    {
        var shapes = CarrierDelegates(typeFqn);
        if (shapes.Count == 0)
        {
            return;
        }

        var escaped = EscapeIdentifier(setterName);
        var prop = EscapeIdentifier(propertyName);
        var carrier = typeFqn.EndsWith("?", StringComparison.Ordinal)
            ? typeFqn.Substring(0, typeFqn.Length - 1)
            : typeFqn;

        // A carrier is non-folding — it holds a delegate, and two delegates are practically never equal —
        // so there is no Track call here, only the pending-bit bookkeeping and the callback flag.
        var track = pendingBit >= 0
            ? "global::Rask.Core.BuilderRuntime.Written(__c, " + MaskLiteral(new[] { pendingBit }) + "); "
            : "global::Rask.Core.BuilderRuntime.MarkCallbacks(__c); ";

        var self = (generic ? "T" : receiver);
        var typeArgs = (generic ? "<T>" : typeParameters);
        var where = generic ? " where T : " + receiver : constraints;

        foreach (var shape in shapes)
        {
            // Wrapped exactly as the pass-through setter's own value is: an Element's handlers go to the
            // DOM unwrapped, a non-Element component's are wrapped so invoking one re-renders its owner.
            var value = wrap ? "global::Rask.Core.AutoCallback.Wrap(value)!" : "value";

            // The parameter is NULLABLE, and a null clears the slot rather than filling it with a carrier
            // wrapping nothing. Forwarding an optional handler a component already holds
            // (`.OnClick(OnClick)` where its own is `Action?`) is ordinary, and requiring the caller to
            // null-check first would be ceremony the chain exists to remove. `null` on its own still
            // reaches the carrier-typed pass-through, which outranks these.
            // An `async` lambda converts to Func<…, Task> and Func<…, ValueTask> equally well (CS0121), so the Task
            // shape outranks the ValueTask one. A lambda that RETURNS a ValueTask — `() => OnRate.Invoke(i)` — cannot
            // reach the Task shape at all, and between the two left, Func<ValueTask> beats Action on its own.
            if (shape.EndsWith(TaskFqn + ">", StringComparison.Ordinal))
            {
                sb.AppendLine("    [global::System.Runtime.CompilerServices.OverloadResolutionPriority(1)]");
            }

            sb.Append("    ").Append(visibility).Append(" static ").Append(self).Append(' ').Append(escaped)
                .Append(typeArgs).Append("(this ").Append(self).Append(" __b, ").Append(shape)
                .Append("? value)").Append(where);
            // `null` only where the property can hold it. A REQUIRED carrier — a template a component
            // cannot render without — is a non-nullable struct, and `default` is its unset value.
            var empty = typeFqn.EndsWith("?", StringComparison.Ordinal) ? "null" : "default";

            var wrapped = "value is null ? " + empty + " : new " + carrier + "(" + value + ")";
            sb.Append(" { var __c = __b; ").Append(track).Append("__c.").Append(prop)
                .Append(" = ").Append(AddedRule(typeFqn, propertyName, wrapped)).AppendLine("; return __b; }");
        }
    }

    // The three bag overloads' parameter lists, the AttrBag each builds, and its doc.
    private static (string Parameters, string Expression, string Doc)[] BagOverloads(
        string propertyName, string setterName)
    {
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

        return
        [
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
        ];
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
        bool fold, int pendingBit, string visibility, bool generic,
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

        var self = (generic ? "T" : receiver);
        // The component's own type parameters are carried here too — a generic component's bag setter that
        // declared none would not compile. Non-generic components (every one that has a bag prop today)
        // are unaffected: the list is empty either way.
        var typeArgs = (generic ? "<T>" : typeParameters);
        // …and with them their CONSTRAINTS, or a constrained generic component with a bag prop emits
        // `Foo<TValue>(this Widget<TValue> …)` with no `where TValue : …` and fails to
        // compile (CS0314). Carrying the parameters without the constraints was half a fix.
        var where = generic ? " where T : " + receiver : constraints;

        // The body is assigned here rather than forwarded to the dictionary overload. Every component's
        // setters are extension methods in one static class, so a forwarding `Data(__b, …)`
        // is resolved against ALL of them and binds to whichever component's overload wins — it picked
        // FullscreenTrigger's for a Trigger.EyeDropper. Assigning the property directly has no name
        // to resolve.
        foreach (var (parameters, expression, doc) in BagOverloads(propertyName, setterName))
        {
            sb.Append("    /// ").AppendLine(doc);
            sb.Append("    ").Append(visibility).Append(" static ").Append(self).Append(' ').Append(escaped)
                .Append(typeArgs).Append("(this ").Append(self).Append(" __b, ").Append(parameters).Append(')')
                .Append(where)
                .Append(" { var __c = __b; var __bag = ").Append(expression).Append("; ").Append(track)
                .Append("__c.").Append(prop).AppendLine(" = __bag; return __b; }");
        }
    }

    // THE CHAIN'S RECEIVER IS THE COMPONENT. There is one shape, and a step hands back exactly what it
    // was called on.
    //
    // Four types used to wrap it — `Build<T>`, the mode-carrying `Build<T, TMode>`, `FormBuild<T>` and
    // `GridBuild<T, TKey>` — and each existed to carry in the TYPE something the component could not: a
    // form control's bound/controlled mode, a form's submit-state children indexer, a grid's row key.
    // What replaced each one is worth stating, because none of it is the same mechanism:
    //
    //   - the two INDEXERS are declared on the components themselves (Form and UiDataGrid<T>), which
    //     scopes them exactly as well and costs no type parameter. An indexer cannot be constrained,
    //     which is the only reason they ever needed a shape of their own;
    //   - BIND-VERSUS-VALUE still does not compile, and needs no diagnostic to say so. The two openings
    //     are declared on the SEED and neither is emitted as a setter on the control, so taking one
    //     hands back the control and the other is simply not a member of it (CS1929). The mode types
    //     were never what ruled the pair out — the seed was, and the seed is still here.
    //
    // The GRID's row key is the one guarantee that really was lost, and it is worth being honest about:
    // `GridBuild<T, TKey>` opened carrying NoKey and declared the selection steps only over a pinned one,
    // so a grid that never said what identifies a row was not a grid whose selection was rejected — it
    // was one where selection was not offered. `Selected` and `OnSelect` are now ordinary
    // extensions on UiDataGrid<TRow>, reachable before any RowKey step, and SelectionOf fabricates a
    // strategy with no selector installed when they are.
    //
    // With the component as the receiver a generic self-type does the rest: `T` infers to whatever it was
    // called on and returns exactly that. Emitting a shared step over more than one shape is CS0111 —
    // they all collapse to the same signature — so one shape is load-bearing, not tidying.

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
            if (c.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal)) is not { Name.Length: > 0 } p)
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
                    !isBind && FoldsIntoPropsChanged(p.Name, p.IsDelegate, p.IsAutoRerenderDelegate),
                    isBind ? -1 : Bit(bits, p.Name),
                    p.Summary),
            ]);

            // A control over a non-nullable value type binds a nullable property too: every property of a
            // generated form model is nullable, so `Ui.Checkbox.Bind(() => model.InStock)` over a `bool?` has
            // to open the chain as surely as over a `bool`. A second overload rather than a wider parameter,
            // so a `bool` property still takes the exact one (C# prefers the identity return conversion) and
            // the controlled half, Value and OnChange, keeps its plain `bool`. A null reads as the control's
            // empty state, and a write boxes a `bool`, which either property takes.
            if (isBind && c.FormControl is { LiftsToNullable: true } lifted)
            {
                openings.Add([
                    new EntryInference(
                        p.Name,
                        "global::System.Linq.Expressions.Expression<global::System.Func<" + lifted.ValueTypeFqn + "?>>",
                        p.Name,
                        Track: false,
                        PendingBit: -1,
                        p.Summary,
                        Lift: "global::Rask.Core.Forms.ExpressionAccessor.NonNullable"),
                ]);
            }
        }

        return WithCollectionShapes(c, openings);
    }

    // The collection types a model realistically declares for a multi-value field. Each gets its own opening
    // so the exact-type overload lands on the collection-valued control rather than the single-valued one.
    private static string[] CollectionShapes(string item) =>
    [
        "global::System.Collections.Generic.List<" + item + ">",
        "global::System.Collections.Generic.IList<" + item + ">",
        "global::System.Collections.Generic.HashSet<" + item + ">",
        "global::System.Collections.ObjectModel.Collection<" + item + ">",
        "global::System.Collections.ObjectModel.ObservableCollection<" + item + ">",
        item + "[]",
    ];

    // Whether a form control actually has a mode to choose. A control that implements IFormControl<T>
    // EXPLICITLY exposes neither Bind nor Value as a settable property, so there is no opening to build
    // and forcing a seed on it would emit one with no members at all — leaving the control unreachable
    // from markup with nothing reported. Such a control keeps the ordinary entry it always had.
    private static bool HasModeOpening(Candidate c) =>
        c.FormControl is not null && c.Properties.Any(static p => p.Name is "Bind" or "Value");

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

    // Every type a prop, parameter or entry is DECLARED with goes through here. A Rask.Data entity's
    // generated model (ProductModel) is emitted by another generator into this same compilation, so to this
    // one it is an unresolved error type — and an error type displays as the bare name its author wrote,
    // which binds from the author's file (it has the using) and not from RaskBuilderSetters.g.cs (it does
    // not). Byte-identical for every type that involves no generated model.
    private static string DisplayTypeName(ITypeSymbol type, SymbolDisplayFormat format, Compilation compilation) =>
        Shared.GeneratedModelShape.DisplayName(type, format, compilation);

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
        string Summary = "",
        bool IsTypedAria = false);
}
