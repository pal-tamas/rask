using System.Globalization;

namespace Rask;

// One render's reading of the list: what is picked, and what the page knows of the search.
public abstract partial class UiSelectControl<T>
{
    private sealed class View(UiWithField field, Parts parts, IReadOnlyList<T> picked, string search)
    {
        internal UiWithField Field { get; } = field;

        internal Parts Parts { get; } = parts;

        internal IReadOnlyList<T> Picked { get; } = picked;

        /// <summary>What the page knows was typed: what it asked to hear, or what a press carried with it.</summary>
        internal string Search { get; } = search;

        /// <summary>Whether a press on the create row is taken: always, unless the page filters and its search offers none.</summary>
        internal bool CreateShown { get; init; }

        /// <summary>
        ///     The answers that have something to show, in the order they were picked: each from its row, or
        ///     as the select last drew it once a render took the row away.
        /// </summary>
        internal Answer[] Answers { get; set; } = [];

        /// <summary>The row of the one answer held, when it is among the rows.</summary>
        internal Row? AnswerRow => Picked.Count == 1 && Answers.Length == 1 ? Answers[0].Row : null;

        /// <summary>What the one answer held shows: from its row, or as the select last drew it.</summary>
        internal UiOptionFace? Answer => Picked.Count == 1 && Answers.Length == 1 ? Answers[0].Face : null;

        internal int CreateIndex => Parts.Rows.Count;

        internal bool IsPicked(Row row) => Picked.Contains(row.Value);

        /// <summary>Whether a press on row <paramref name="index" /> is refused: disabled, or not offered.</summary>
        internal bool Skips(int index) => index == CreateIndex
            ? !CreateShown
            : Parts.Rows[index].Option.Disabled == true;

        internal IEnumerable<Row> PickedRows => Parts.Rows.Where(IsPicked);
    }

    // The reading the page shows now. A handler asks for it when it runs and closes over nothing a render
    // makes again, so it is the same handler render after render: a press read from a page several renders old
    // finds it (Component.StaleEvents).
    private View? _latest;

    private View Latest => _latest ?? throw new InvalidOperationException("The select has not been drawn.");

    private View Read(UiWithField field, Parts parts)
    {
        ReadSearch(parts);
        var search = SearchText(parts);
        var view = new View(field, parts, Current(), search)
        {
            CreateShown = parts.Create is { } create && (HidesRows || OffersCreate(create, parts, search)),
        };
        Remember(view);
        _latest = view;

        return view;
    }

    /// <summary>One answer held: its value, what it shows, and its row when it is among the rows.</summary>
    private readonly record struct Answer(T Value, UiOptionFace Face, Row? Row);

    // The answers held, as the select last drew them.
    private Answer[] _kept = [];

    // Flux stamps a picked option into its trigger and leaves it while the value holds it, whatever options a
    // later render brings. Measured on fluxui.dev/components/select and /pillbox: an option taken out of a
    // listbox, every option replaced, a search the page answers with other rows — the button, a combobox's
    // input and a pillbox's pills go on saying it, the pills in the order they were picked, and a pill whose
    // option is not among the rows is still taken off by its cross. So the face of each answer is kept from the
    // last render that drew its row, for as long as the value holds that answer: a value that leaves takes its
    // face with it. A value no render ever drew a row for has no face — the placeholder shows, and no pill.
    private void Remember(View view)
    {
        if (view.Picked.Count == 0)
        {
            _kept = [];
            return;
        }

        var answers = new List<Answer>(view.Picked.Count);
        foreach (var value in view.Picked)
        {
            var row = view.Parts.Rows.Find(drawn => Same(drawn.Value, value));
            var kept = FaceKept(value);
            var face = row is null || kept?.Is(row.Option) == true ? kept : UiOptionFace.Of(row.Option);
            if (face is not null)
            {
                answers.Add(new Answer(value, face, row));
            }
        }

        _kept = [.. answers];
        view.Answers = _kept;
    }

    private UiOptionFace? FaceKept(T value) =>
        _kept.Where(kept => Same(kept.Value, value)).Select(kept => kept.Face).FirstOrDefault();

    private string SearchText(Parts parts) => parts.Search?.Value ?? parts.Input?.Value ?? _search;

    // Offered once the search is long enough and names no option already there. A listbox with no search has
    // nothing typed to make an option of: its create row is always there, and opens a form of the page's own.
    // Asked here only of a list the page filters; one whose rows the browser hides asks it there, at every key.
    private bool OffersCreate(UiSelectOptionCreate create, Parts parts, string search)
    {
        if (!Searches)
        {
            return true;
        }

        return search.Length >= Math.Max(create.MinLength ?? 1, 1)
               && !parts.Rows.Exists(row => CultureInfo.CurrentCulture.CompareInfo.Compare(
                   row.Option.Text, search, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0);
    }
}
