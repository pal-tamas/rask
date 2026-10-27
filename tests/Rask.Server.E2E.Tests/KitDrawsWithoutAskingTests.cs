using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Every kit component on a live SERVER page, put through what a reader does to it, with the socket counted:
///     a gesture that only changes what the kit DRAWS sends the server nothing.
/// </summary>
/// <remarks>
///     Flux UI handles a pointer over something, a cursor moving, a popup opening or closing and a view being
///     paged in its own script, and Livewire hears only a bound value changing or an action the app wrote. The
///     kit is held to the same: a frame is owed to a value (<see cref="Sends" />) or to a callback the page gave
///     the component, and to nothing else. What still breaks that is in <see cref="Known" /> with the frames it
///     costs today — a list that can only shrink, since a gesture that stops sending fails until it is taken out.
///     <para>
///         One class a page, so the pages are walked side by side: a theory's rows run one after another.
///     </para>
/// </remarks>
public abstract partial class KitDrawsWithoutAskingTests(PlaywrightFixture playwright)
{
    /// <summary>The components drawn on <typeparamref name="TPage" />.</summary>
    protected static TheoryData<string> Drawn<TPage>() => [.. Families.Where(family => family.Value.Page == typeof(TPage)).Select(family => family.Key)];

    /// <summary>Puts one component through its gestures and holds each to its budget.</summary>
    protected async Task HoldAsync(string component)
    {
        var family = Families[component];

        var counted = await family.Count(playwright);
        KitFrameCount.Report(component, counted);

        var wrong = counted
            .Select(row => row.Error is { } error ? $"{component}: {row.Gesture} — could not be made: {error}" : Judge(component + ": " + row.Gesture, row.Frames))
            .OfType<string>().ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>The budgets that name no gesture any family counts: a row left behind by a rename.</summary>
    internal static IReadOnlyList<string> Strays()
    {
        var counted = Families.SelectMany(family => family.Value.Gestures.Select(gesture => family.Key + ": " + gesture)).ToHashSet(StringComparer.Ordinal);
        return [.. Sends.Keys.Concat(Known.Keys).Concat(Unsteady.Keys).Where(key => !counted.Contains(key))];
    }

    /// <summary>The gestures that are in more than one of the lists.</summary>
    internal static IReadOnlyList<string> Doubles() =>
        [.. Sends.Keys.Intersect(Known.Keys).Concat(Unsteady.Keys.Intersect(Known.Keys.Concat(Sends.Keys)))];

    /// <summary>The pages a family is drawn on, for the check that each has a class that walks it.</summary>
    internal static IReadOnlySet<Type> Pages() => Families.Values.Select(family => family.Page).ToHashSet();

    /// <summary>Flux's pages whose component a reader does nothing to, so no gesture of theirs can cost a frame.</summary>
    internal static readonly Dictionary<string, string> NothingToDo = new(StringComparer.Ordinal)
    {
        ["brand"] = "a logo and a name: a link at most, which is the page's",
        ["flag"] = "an image",
        ["heading"] = "words",
        ["icon"] = "an image",
        ["text"] = "words",
    };

    /// <summary>Flux's pages whose family the kit names after its component rather than the page.</summary>
    internal static readonly Dictionary<string, string> CalledHere = new(StringComparer.Ordinal)
    {
        ["otp-input"] = "otp",
    };

    /// <summary>
    ///     The pages of Flux's catalogue with no family here and no reason for none: a component Flux added, put through
    ///     nothing. A family is the page's slug, or the slug and a variant (<c>select-bound</c>).
    /// </summary>
    internal static IReadOnlyList<string> Unwalked(IEnumerable<string> catalogue) =>
        [.. catalogue.Where(slug => !NothingToDo.ContainsKey(slug)).Where(slug =>
        {
            var family = CalledHere.GetValueOrDefault(slug, slug);
            return !Families.Keys.Any(key => key == family || key.StartsWith(family + "-", StringComparison.Ordinal));
        })];

    // Null when the gesture cost what its row says. A count is the same on every machine (KitFrameCount), so a
    // row is exact: more is a gesture that began to send, fewer is a row to lower — or to take out, at none.
    private static string? Judge(string key, KitFrames frames)
    {
        if (Unsteady.TryGetValue(key, out var unsteady))
        {
            return frames.Sent > unsteady.AtMost ? $"{key} — sent {frames.Sent}, over the {unsteady.AtMost} it can cost: {unsteady.Why}."
                : frames.Sent == 0 ? $"{key} — sends nothing now: take it out of {nameof(Unsteady)}."
                : null;
        }

        var (budget, list) = Sends.TryGetValue(key, out var sends) ? (sends.Frames, nameof(Sends))
            : Known.TryGetValue(key, out var known) ? (known, nameof(Known))
            : (0, string.Empty);
        if (frames.Sent > budget || (budget == 0 && frames.Received > 0))
        {
            return $"{key} — sent {frames.Sent} and was answered {frames.Received} times, over a budget of {budget}.";
        }

        if (frames.Sent == budget)
        {
            return null;
        }

        return frames.Sent == 0
            ? $"{key} — sends nothing now: take it out of {list}."
            : $"{key} — sends {frames.Sent} now, not {budget}: lower its row in {list}.";
    }
}
