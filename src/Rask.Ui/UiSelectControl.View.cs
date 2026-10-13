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

        /// <summary>The row of the one answer held, when it is among the rows.</summary>
        internal Row? AnswerRow { get; set; }

        /// <summary>What the one answer held shows: from its row, or as the select last drew it.</summary>
        internal UiOptionFace? Answer { get; set; }

        /// <summary>Every row a press can name: the options, then the create row.</summary>
        internal int Count => Parts.Rows.Count + (Parts.Create is null ? 0 : 1);

        internal int CreateIndex => Parts.Rows.Count;

        internal bool IsPicked(Row row) => Picked.Contains(row.Value);

        /// <summary>Whether a press on row <paramref name="index" /> is refused: disabled, or not offered.</summary>
        internal bool Skips(int index) => index == CreateIndex
            ? !CreateShown
            : Parts.Rows[index].Option.Disabled == true;

        internal IEnumerable<Row> PickedRows => Parts.Rows.Where(IsPicked);
    }

    private View Read(UiWithField field, Parts parts)
    {
        var search = SearchText(parts);
        var view = new View(field, parts, Current(), search)
        {
            CreateShown = parts.Create is { } create && (HidesRows || OffersCreate(create, parts, search)),
        };
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
