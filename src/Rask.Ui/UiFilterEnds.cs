using System.Globalization;
using System.Linq.Expressions;
using System.Numerics;
using static Rask.Markup;

namespace Rask;

/// <summary>
///     The two ends of a range filter (<c>Ui.Filter.Range(() =&gt; From, () =&gt; To)</c>): each a page member the address
///     fills, typed into a field of its own, and said together on one badge.
/// </summary>
/// <typeparam name="T">The number either end holds.</typeparam>
internal sealed class UiFilterEnds<T> : IUiFilterEnds
    where T : struct, INumber<T>
{
    private const string Control = "Ui.Filter.Range";

    // Every digit a decimal keeps after the point, and none it does not: 20.5 stays 20.5, 5000 is 5 000.
    private const string Grouped = "#,0.############################";

    private readonly Expression<Func<T?>> _from;
    private readonly Expression<Func<T?>> _to;

    internal UiFilterEnds(Expression<Func<T?>> from, Expression<Func<T?>> to)
    {
        // Read now, so a member that is no query parameter fails where the range is written.
        _ = UiFilterCell<T?>.Over(from, Control);
        _ = UiFilterCell<T?>.Over(to, Control);
        _from = from;
        _to = to;
    }

    /// <inheritdoc />
    public string Name => UiFilterCell<T?>.Over(_from, Control).Name;

    /// <inheritdoc />
    public UiFilterEntry Filter(string label)
    {
        var least = UiFilterCell<T?>.Over(_from, Control);
        var most = UiFilterCell<T?>.Over(_to, Control);
        var words = Words(least.Value, most.Value);

        return new UiFilterEntry(label, words, words is not null, least.Key) { Keys = [least.Key, most.Key] };
    }

    /// <inheritdoc />
    public Component Draw(string label, UiFilters? set) =>
        Ui.Fieldset.Class("min-w-0")[
            Ui.Legend.Class("mb-3 text-sm")[label],
            Div.Class("flex items-center gap-2")[
                End(_from, RaskStrings.Get(RaskString.FilterRangeFrom, "From"), set),
                Span.Class("text-zinc-500 dark:text-white/70").Aria("hidden", "true")["–"],
                End(_to, RaskStrings.Get(RaskString.FilterRangeTo, "To"), set)
            ]
        ];

    // One end: the empty field says which, and is called so. In a set it is bound to the address, written as typing
    // pauses, as a Ui.Input there is — by this chain's own steps, since the range draws it in its own render.
    private static UiInput<T?> End(Expression<Func<T?>> bind, string words, UiFilters? set) =>
        Ui.Input.Bind(set is null ? bind : UiFilterCell<T?>.Over(bind, Control).In(set).Bind)
            .Id(UiFieldId.Derive(null, null, bind, null))
            .Live(set is null ? null : true)
            .Placeholder(words)
            .Attributes(new Dictionary<string, string?>(StringComparer.Ordinal) { ["aria-label"] = words })
            .Class("min-w-0 flex-1");

    // "at least 5 000", "at most 7 000", "5 000 – 7 000", in the reader's grouping; nothing for no end set.
    private static string? Words(T? least, T? most) => (least, most) switch
    {
        (null, null) => null,
        ({ } low, null) => RaskStrings.Get(RaskString.FilterRangeAtLeast, "at least {0}", Number(low)),
        (null, { } high) => RaskStrings.Get(RaskString.FilterRangeAtMost, "at most {0}", Number(high)),
        ({ } low, { } high) => RaskStrings.Get(RaskString.FilterRangeBetween, "{0} – {1}", Number(low), Number(high)),
    };

    private static string Number(T value) => value.ToString(Grouped, CultureInfo.CurrentCulture);
}
