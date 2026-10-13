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

        /// <summary>The row of the one answer held, when it is among the rows.</summary>
        internal Row? AnswerRow { get; set; }

        /// <summary>What the one answer held shows: from its row, or as the select last drew it.</summary>
        internal UiOptionFace? Answer { get; set; }

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

    private View Read(UiWithField field, Parts parts)
    {
        var search = SearchText(parts);
        var filters = Filter != false && search.Length > 0;
        var view = new View(field, parts, Current(), search)
        {
            Shown = [.. parts.Rows.Select(row => !filters || row.Option.Filterable == false || Matches(row.Option, search))],
            CreateShown = parts.Create is { } create && OffersCreate(create, parts, search),
        };
        view.Cursor = Place(view);
        Remember(view);

        return view;
    }

    // The one answer held, as the select last drew it — the value it stands for and its face.
    private (T Value, UiOptionFace Face)? _kept;

    // Flux stamps the picked option into its button and leaves it while the value stands (measured on
    // fluxui.dev/components/select: an option taken out of a listbox, every option replaced, a search the page
    // answers with other rows — the button, and a combobox's input, go on saying it). So the face of the answer
    // is kept from the last render that drew its row, and is the answer's for as long as the value is that one.
    // A value no render ever drew a row for has no face: the placeholder shows, as on Flux's page as loaded.
    private void Remember(View view)
    {
        if (view.Picked.Count != 1)
        {
            _kept = null;
            return;
        }

        var value = view.Picked[0];
        foreach (var row in view.Parts.Rows)
        {
            if (!Same(row.Value, value))
            {
                continue;
            }

            if (_kept is not { } drawn || !Same(drawn.Value, value) || !drawn.Face.Is(row.Option))
            {
                _kept = (value, UiOptionFace.Of(row.Option));
            }

            view.AnswerRow = row;
            view.Answer = _kept.Value.Face;
            return;
        }

        if (_kept is { } kept && Same(kept.Value, value))
        {
            view.Answer = kept.Face;
            return;
        }

        _kept = null;
    }

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
