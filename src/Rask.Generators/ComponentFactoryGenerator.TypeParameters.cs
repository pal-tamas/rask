using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Rask.Generators;

public sealed partial class ComponentFactoryGenerator
{
    // A type parameter's [DynamicallyAccessedMembers] has to travel with it into the generated chain.
    // The chain is the ONLY way to build a component, so an annotation the generated seed, stage or
    // setter does not repeat is an annotation the trimmer never gets to act on — worse, the component
    // then declares a requirement its own call sites cannot satisfy, which is IL2091 on every one of
    // them. Rendered once per parameter here, in declaration order, and repeated by AnnotateDecl
    // wherever that parameter is DECLARED (never where it is passed as a type argument — an attribute
    // is not legal there).
    //
    // The enum value is emitted as a cast integer rather than a flags expression: the member names are
    // an open set, and reconstructing "PublicProperties | PublicFields" correctly for every combination
    // buys nothing a cast does not already say.
    private static ImmutableArray<string> ReadTypeParameterAnnotations(
        ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        if (typeParameters.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(typeParameters.Length);
        foreach (var tp in typeParameters)
        {
            builder.Add(DamAnnotation(tp));
        }

        return builder.MoveToImmutable();
    }

    private static string DamAnnotation(ITypeParameterSymbol typeParameter)
    {
        foreach (var attr in typeParameter.GetAttributes())
        {
            if (!string.Equals(
                    attr.AttributeClass?.ToDisplayString(),
                    "System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (attr.ConstructorArguments.Length != 1 || attr.ConstructorArguments[0].Value is not { } value)
            {
                continue;
            }

            var flags = Convert.ToInt32(value, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
            return "[global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers("
                   + "(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes)"
                   + flags + ")] ";
        }

        return string.Empty;
    }

    // Re-renders a type parameter LIST for a declaration position, putting each parameter's annotation
    // back in front of it. Returns the list unchanged when nothing is annotated, which is every
    // component but the handful that reflect over a type argument.
    private static string AnnotateDecl(Candidate c, string typeParameterList)
    {
        var annotations = c.TypeParameterAnnotations;
        if (typeParameterList.Length == 0 || annotations.Count == 0)
        {
            return typeParameterList;
        }

        var declared = OrderedTypeParameters(c.TypeParameters);
        var subset = OrderedTypeParameters(typeParameterList);
        if (subset.Count == 0)
        {
            return typeParameterList;
        }

        var annotated = false;
        var parts = new List<string>(subset.Count);
        foreach (var name in subset)
        {
            // Positional lookup against the component's own list. A renamed parameter (the collision
            // dodge in EmitReset/EmitEntryForwarder) simply will not match, and falls back to no
            // annotation rather than putting the wrong one on.
            var index = declared.IndexOf(name);
            var annotation = index >= 0 && index < annotations.Count ? annotations[index] : string.Empty;
            annotated |= annotation.Length != 0;
            parts.Add(annotation + name);
        }

        return annotated ? "<" + string.Join(", ", parts) + ">" : typeParameterList;
    }

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
            // Nothing is pinned yet, so a step whose argument cannot supply a type parameter itself —
            // a lambda, whose parameter types would have nowhere to come from — cannot open a chain.
            // It can still COMPLETE one below.
            if (LambdaInputTypeParameters(first.ParamTypeFqn, names).Count != 0 || !seen.Add(first.ParamName))
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
                    || !mentioned.Overlaps(unpinned)
                    || LambdaInputTypeParameters(next.ParamTypeFqn, names).Overlaps(unpinned))
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
            // A delegate is offered only when a lambda passed to it can infer something — see
            // LambdaInputTypeParameters. Whether it may pin HERE is the caller's question, and both
            // callers ask it the same way: never first, and only once its inputs are pinned.
            if (p.IsInitOnly || p.IsSharedSurfaceProp || !IsParamProperty(p) || p.IsBoundInterfaceProp
                || (p.IsDelegate && !IsFuncLikeDelegate(p.TypeFqn)))
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
                FoldsIntoPropsChanged(p.Name, p.IsDelegate, p.IsAutoRerenderDelegate),
                Bit(bits, p.Name),
                p.Summary);
        }
    }

    // A REAL delegate whose last type argument is a return, so a lambda passed to it can infer that
    // argument once the ones before it are known.
    //
    // `System.Func<…>` and nothing else. Rask's own `Fn<TIn, TOut>` reads like one and is not: it is a
    // readonly STRUCT that a lambda reaches through a user-defined conversion, and C# infers no type
    // argument through one of those (CS0411). A carrier can therefore never pin, which is why every
    // `Fn`/`Callback` prop stays excluded here.
    private static bool IsFuncLikeDelegate(string typeFqn) =>
        typeFqn.StartsWith("global::System.Func<", StringComparison.Ordinal);

    // The type parameters an argument of this type cannot itself supply, and so must already be pinned
    // before a step taking it can pin anything.
    //
    // For an ordinary value there are none: `IEnumerable<T>` is written out at the call site, so it
    // supplies T. For a delegate the answer is its PARAMETER positions, because the caller writes a
    // lambda and those types appear nowhere in what they wrote — `r => r.Id` states neither.
    //
    // This is the whole rule for when a delegate may pin. `Func<TItem, TValue>` cannot OPEN a chain:
    // the lambda's parameter has no type and its body cannot be bound, which is the BsSelect trap the
    // greedy loop below carries a note about. The same property as a LATER pin, with TItem fixed by the
    // step before it, infers TValue perfectly well — which is what `Ui.DataGrid.Data(rows).RowKey(r =>
    // r.Id)` needs, and what kept a REQUIRED delegate step from being able to pin anything at all.
    // A delegate whose inputs are all concrete — `Fn<UiGridRequest, Task<UiGridPage<T>>>` — opens one
    // perfectly well, so the test is the inputs rather than the delegate-ness.
    private static HashSet<string> LambdaInputTypeParameters(string typeFqn, HashSet<string> names)
    {
        // The nullable annotation an optional prop carries — `Fn<T, Component?>?` — is not part of the
        // type argument list, and leaving it on made every optional delegate look like something this
        // could not read, which is the opposite of the truth about it.
        var bare = typeFqn.TrimEnd('?');
        var open = bare.IndexOf('<');
        if (!IsFuncLikeDelegate(bare) || open < 0 || !bare.EndsWith(">", StringComparison.Ordinal))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        var inner = bare.Substring(open + 1, bare.Length - open - 2);
        var depth = 0;
        var lastComma = -1;
        for (var i = 0; i < inner.Length; i++)
        {
            switch (inner[i])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ',' when depth == 0: lastComma = i; break;
                default: break;
            }
        }

        // No comma at the top level means there is only a return and nothing to supply — `Func<T>`.
        return lastComma < 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : MentionedTypeParameters(inner.Substring(0, lastComma), names);
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

    // "<TValue, TItem>" → { "TValue", "TItem" }; empty for a non-generic type.
    // The subset of a component's constraint clauses that applies to the type parameters a generated
    // struct actually DECLARES.
    //
    // A stage declares only what its opening step pinned — `RaskStage_UiDataGrid_Selected<TKey>` — so the
    // component's whole clause would name type parameters the struct does not have (CS0699), while no
    // clause at all drops a constraint the component requires the moment the struct names the component
    // again (CS8714). Both of those were live: the constraint was being written onto the STEP inside the
    // stage, where TKey is not the method's to constrain.
    //
    // Nothing had a CONSTRAINED type parameter pinned by a step until UiDataGrid<T, TKey>'s `where TKey :
    // notnull`, which is why a generator this heavily tested had no reason to have got it right.
    private static string ConstraintsDeclaredBy(Candidate c, string typeParameters)
    {
        if (c.TypeParameterConstraints.Length == 0 || typeParameters.Length == 0)
        {
            return string.Empty;
        }

        var declared = ParseTypeParameters(typeParameters);
        var kept = new StringBuilder();
        foreach (var clause in c.TypeParameterConstraints.Split(
                     [" where "], StringSplitOptions.RemoveEmptyEntries))
        {
            var space = clause.IndexOf(' ');
            if (space > 0 && declared.Contains(clause.Substring(0, space)))
            {
                kept.Append(" where ").Append(clause);
            }
        }

        return kept.ToString();
    }

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
}
