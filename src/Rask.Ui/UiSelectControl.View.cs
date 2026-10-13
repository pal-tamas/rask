using System.Globalization;

namespace Rask;

// One render's reading of the list: what is picked, which rows the search leaves, where the cursor is.
public abstract partial class UiSelectControl<T>
{
    private bool _open;

    // The row the keyboard is on: an index into the options, the option count for the create row, Unset to
    // start from the picked option, First to start from the top — where a search that just changed puts it.
    private int _cursor = Unset;

    private const int Unset = -1;

    private const int First = -2;

    // No row at all: where the pointer leaves the pillbox's cursor when it leaves the list.
    private const int None = -3;

    private sealed class View(UiWithField field, Parts parts, IReadOnlyList<T> picked, string search)
    {
        internal UiWithField Field { get; } = field;

        internal Parts Parts { get; } = parts;

        internal IReadOnlyList<T> Picked { get; } = picked;

        internal string Search { get; } = search;

        internal bool[] Shown { get; init; } = [];

        internal bool CreateShown { get; init; }

        internal int Cursor { get; set; } = Unset;

        /// <summary>
        ///     The answers that have something to show, in the order they were picked: each from its row, or
        ///     as the select last drew it once a render took the row away.
        /// </summary>
        internal Answer[] Answers { get; set; } = [];

        /// <summary>The row of the one answer held, when it is among the rows.</summary>
        internal Row? AnswerRow => Picked.Count == 1 && Answers.Length == 1 ? Answers[0].Row : null;

        /// <summary>What the one answer held shows: from its row, or as the select last drew it.</summary>
        internal UiOptionFace? Answer => Picked.Count == 1 && Answers.Length == 1 ? Answers[0].Face : null;

        /// <summary>Every row the cursor could be on: the options, then the create row.</summary>
        internal int Count => Parts.Rows.Count + (Parts.Create is null ? 0 : 1);

        internal int CreateIndex => Parts.Rows.Count;

        internal bool AnyShown => Shown.Any(shown => shown);

        internal bool IsPicked(Row row) => Picked.Contains(row.Value);

        /// <summary>Whether the cursor skips row <paramref name="index" />: filtered out, disabled, or not offered.</summary>
        internal bool Skips(int index) => index == CreateIndex
            ? !CreateShown
            : !Shown[index] || Parts.Rows[index].Option.Disabled == true;

        internal IEnumerable<Row> PickedRows => Parts.Rows.Where(IsPicked);
    }

    // The reading the page shows now. A handler asks for it when it runs and closes over nothing a render
    // makes again, so it is the same handler render after render: a press read from a page several renders old
    // — the list opening, a row lit, a row picked, all still on their way — finds it (Component.StaleEvents).
    private View? _latest;

    private View Latest => _latest ?? throw new InvalidOperationException("The select has not been drawn.");

    private View Read(UiWithField field, Parts parts)
    {
        ReadSearch(parts);
        var search = SearchText(parts);
        var filters = Filter != false && search.Length > 0;
        var view = new View(field, parts, Current(), search)
        {
            Shown = [.. parts.Rows.Select(row => !filters || row.Option.Filterable == false || Matches(row.Option, search))],
            CreateShown = parts.Create is { } create && OffersCreate(create, parts, search),
        };
        view.Cursor = Place(view);
        Remember(view);
        _answer = PickedWords(view);
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

    // Flux's own matching: anywhere in the words or the keywords, whatever the case or the accents.
    private static bool Matches(UiSelectOption option, string search) =>
        UiSelectText.Contains(option.Text, search) || (option.Keywords is { } keywords && UiSelectText.Contains(keywords, search));

    // Offered once the search is long enough and names no option already there. A listbox with no search has
    // nothing typed to make an option of: its create row is always there, and opens a form of the page's own.
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

    // Where the cursor is for this render. A row it can no longer be on sends it back to the start.
    private int Place(View view)
    {
        if (_cursor >= 0 && _cursor < view.Count && !view.Skips(_cursor))
        {
            return _cursor;
        }

        if (_cursor == None)
        {
            return Unset;
        }

        var picked = _cursor == First ? null : view.PickedRows.FirstOrDefault(row => !view.Skips(row.Index));

        return picked?.Index ?? UiSelectNav.FirstEnabled(view.Count, view.Skips);
    }
}
