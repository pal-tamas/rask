using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Rask.Core.Globalization;
using static Rask.Markup;

namespace Rask;

/// <summary>
///     What <c>.Choices(…)</c> makes of the rows it is handed, for <see cref="UiSelect{T}" /> and
///     <see cref="UiPillbox{T}" /> alike: options in the reader's alphabet, a search that answers with them, the
///     placeholder from the catalog, and whether nothing chosen is an answer.
/// </summary>
internal static class UiChoices
{
    // Reading a reference type's `?` is reflection that is not thread-safe (NullabilityInfoContext), so it is asked
    // once a member, under a lock.
    private static readonly NullabilityInfoContext Nullability = new();

    private static readonly ConcurrentDictionary<MemberInfo, bool> Annotated = new();

    // A pillbox row's detail: after the words, in the ink of a select option's description.
    private const string PillDetail = "ms-2 text-zinc-500 dark:text-white/70";

    /// <summary>What a select or pillbox given choices shows while nothing is chosen, unless the page says.</summary>
    internal static string Placeholder => RaskStrings.Get(RaskString.SelectChoose, "Choose...");

    /// <summary>The rows, held whole, as the options the select draws in the reader's alphabet.</summary>
    /// <param name="choices">The rows, in any order.</param>
    /// <param name="mayBeEmpty">Whether choosing nothing is an answer.</param>
    /// <param name="before">What the last render's <c>.Choices(…)</c> made, to compare with.</param>
    /// <param name="changed">Whether the options are not the ones <paramref name="before" /> drew.</param>
    /// <param name="pills">True for a pillbox's rows, whose detail follows the words.</param>
    internal static UiChosen Held<T>(IEnumerable<Ui.Choice<T>> choices, bool mayBeEmpty, UiChosen? before, out bool changed, bool pills = false)
    {
        var sorted = Sorted(choices, TextOrder.Current);
        changed = before is null || before.MayBeEmpty != mayBeEmpty || !Same(before.Said as Ui.Choice<T>[], sorted);

        return new UiChosen(sorted, Options(sorted, pills), null, mayBeEmpty);
    }

    /// <summary>
    ///     A search that puts what is typed to <paramref name="find" /> and answers with its rows in the reader's
    ///     alphabet, and the rows the value holds, drawn before the first answer.
    /// </summary>
    /// <param name="find">The rows for what was typed.</param>
    /// <param name="held">The rows the value holds when the page is drawn.</param>
    /// <param name="mayBeEmpty">Whether choosing nothing is an answer.</param>
    /// <param name="before">What the last render's <c>.Choices(…)</c> made, to compare with.</param>
    /// <param name="changed">Whether the search or the held rows are not the ones <paramref name="before" /> had.</param>
    /// <param name="pills">True for a pillbox's rows, whose detail follows the words.</param>
    internal static UiChosen Asked<T>(
        Func<string, Task<IReadOnlyList<Ui.Choice<T>>>> find, IEnumerable<Ui.Choice<T>> held, bool mayBeEmpty, UiChosen? before, out bool changed,
        bool pills = false)
    {
        ArgumentNullException.ThrowIfNull(find);

        // Taken while the page is drawn: the answer comes after the render, where no culture is the page's.
        var order = TextOrder.Current;
        var shown = Sorted(held, order);
        changed = before is null || before.MayBeEmpty != mayBeEmpty
            || before.Said is not Search<T> last || !last.Find.Equals(find) || !Same(last.Held, shown);

        var asking = new Fn<string, Task<IEnumerable<Component?>>>(async typed =>
            Options(Sorted(await find(typed).ConfigureAwait(false) ?? [], order), pills));

        return new UiChosen(new Search<T>(find, shown), Options(shown, pills), asking, mayBeEmpty);
    }

    // A stable sort: rows of the same words stay in the order they came in.
    private static Ui.Choice<T>[] Sorted<T>(IEnumerable<Ui.Choice<T>> choices, StringComparer order)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return [.. choices.OrderBy(choice => choice.Name, order)];
    }

    // The option a row is: keyed by its value in its own list, its detail searched too. A select draws the detail as the
    // option's line under its words; a pillbox's row has none (Flux's pillbox option takes no description), so there it
    // is a muted word after them, in the description's ink, and the pill says the words alone.
    private static Component Option<T>(Ui.Choice<T> choice, bool pills)
    {
        var option = Ui.SelectOption.Key(choice.Value).Value(choice.Value).Keywords(choice.Detail);
        if (!pills || choice.Detail is not { } detail)
        {
            return option.Description(choice.Detail)[choice.Name];
        }

        return option.SelectedLabel(choice.Name)[Span[choice.Name], Span.Class(PillDetail)[detail]];
    }

    private static Component?[] Options<T>(Ui.Choice<T>[] sorted, bool pills) => [.. sorted.Select(choice => Option(choice, pills))];

    private static bool Same<T>(Ui.Choice<T>[]? before, Ui.Choice<T>[] now) =>
        before is not null && before.AsSpan().SequenceEqual(now);

    /// <summary>
    ///     Whether choosing nothing is an answer for a select of <paramref name="type" /> bound with
    ///     <paramref name="bound" />: its member may hold nothing (<c>int?</c>, <c>string?</c>) and the model does not
    ///     require it — the question <c>aria-required</c> asks. A select the page holds the value of
    ///     (<c>Value</c>) asks its type alone.
    /// </summary>
    internal static bool MayBeEmpty(LambdaExpression? bound, Type type)
    {
        if (Nullable.GetUnderlyingType(type) is null && type.IsValueType)
        {
            return false;
        }

        if (bound is null)
        {
            return true;
        }

        if (UiRequired.Is(bound))
        {
            return false;
        }

        return Nullable.GetUnderlyingType(type) is not null || (Member(bound) is { } member && IsAnnotated(member));
    }

    private static MemberInfo? Member(LambdaExpression bound) => bound.Body switch
    {
        MemberExpression { Member: PropertyInfo or FieldInfo } member => member.Member,
        UnaryExpression { NodeType: ExpressionType.Convert, Operand: MemberExpression { Member: PropertyInfo or FieldInfo } member } => member.Member,
        _ => null,
    };

    // A `string?` member. Unknown — a model compiled without nullable annotations, or an app trimmed of them — is
    // read as not nullable, as the binding reads it.
    private static bool IsAnnotated(MemberInfo member) =>
        Annotated.GetOrAdd(member, static m =>
        {
            lock (Nullability)
            {
                try
                {
                    var info = m is PropertyInfo property ? Nullability.Create(property) : Nullability.Create((FieldInfo)m);
                    return info.WriteState == NullabilityState.Nullable;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }
        });
}

// What a searching `.Choices(…)` was handed, for the next render to compare with.
file sealed record Search<T>(Func<string, Task<IReadOnlyList<Ui.Choice<T>>>> Find, Ui.Choice<T>[] Held);
