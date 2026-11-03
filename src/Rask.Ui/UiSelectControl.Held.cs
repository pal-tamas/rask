using Rask.Core.Forms;

namespace Rask;

// A pick that waits for the next action, held in the browser (`held`, rask-list-held.ts).
//
// Flux's select under a plain `wire:model` sends nothing when an option is picked (measured on
// fluxui.dev/components/select and /pillbox, 2026-11-02: no Livewire request at the pick, one under
// `wire:model.live`): it draws the picked option itself and Livewire carries the value with the next action. A
// bound select that does not say `.Live()` does the same here. Its rows carry their form value and no handler,
// the browser draws the pick, and writes the answer into ONE hidden bound field that waits for the next action
// (`data-rask-bind-on="action"`, rask-input.ts) — then goes ahead of it, and the pick is written to the model
// and its rules run as they did when the press itself was sent.
//
// One answer: a listbox, searchable or not, and a combobox. A select of several answers and a pillbox draw a
// count or pills, and are still drawn by the page. So is a list the page searches itself (`Filter(false)`): the
// answer is found again by its row when it arrives, and the rows of a search the page answered may be gone by
// then — a store asked for "" once the list shut has none — where the press finds the row it was made on.
public abstract partial class UiSelectControl<T>
{
    private const string HeldValue = "data-rask-list-value";

    private const string HeldClear = "data-rask-list-clear";

    /// <summary>Whether a pick is held in the browser until the next action: bound, waiting, one answer, rows of its own.</summary>
    private bool Held => Pills is null && !IsMultiple && Filter != false && CommitDraws;

    // The bound field, first in the control: the answers the page drew, as form values (UiFormValues). The browser
    // writes the reader's pick into it, and it goes ahead of the next action. No name: what a form posts is the
    // named fields after the popup (PostedValues). While a message shows under the select, the first pick says
    // so at once (`data-rask-on-edit`), and the message goes, as it does under an input at the first key.
    private HTMLInputElement<string>? HeldField(View view)
    {
        if (!Held)
        {
            return null;
        }

        var edit = Bound is { } bound ? UiFormCommit.FirstEdit(ExpressionAccessor.Parse(bound), Form) : [];
        return Input.Key("held").Value(UiFormValues.Write(Posted(view))).Type(InputType.Hidden)
            .Attributes([.. UiRuntimeMarks.WaitsForAction, .. edit]).OnChange(HeldAsync);
    }

    // What the browser stamps into the button, as the page draws it: the placeholder, the answer the page holds
    // now — an answer whose row a render took away is still drawn from it — and the box a picked row's content is
    // copied into (`data-rask-list-face`, the class its body takes there). Keyed by all of it: a template's
    // content is no child a later diff can reach, so new content is a new template.
    private Component? HeldFaces(View view, string? placeholder)
    {
        if (!Held || IsCombobox)
        {
            return null;
        }

        var shown = placeholder is null ? null : Span.Class(UiSelectLook.Placeholder).Data("ui-select-placeholder", "")[placeholder];
        var answer = view.Answer is null ? null : PickedContent(view, placeholder);
        return Template.Key("faces:" + placeholder + ":" + view.Answer?.FormValue).Data("rask-list-faces", "")[
            shown,
            answer,
            Div.Class(UiSelectLook.Picked).Data("rask-list-face", UiListboxLook.OptionBody)
        ];
    }

    // The button's words. Held, the page draws them once and they are the browser's from then on: no diff and no
    // morph goes into them, so the pick it stamped there is not taken back by a render that has not heard it.
    private Component Shows(Component? content) =>
        Held
            ? HostedElement.Tag("div").Opaque(true).Class(UiSelectLook.Selected).Data("rask-list-shows", "")[content]
            : Div.Class(UiSelectLook.Selected)[content];

    // What a held row says: its form value, and the words the button or the input says once it is picked.
    private static void MarkHeld(Dictionary<string, string?> marks, UiSelectOption option)
    {
        marks[HeldValue] = option.FormValue;
        if (option.SelectedLabel is { } says)
        {
            marks["data-rask-list-says"] = says;
        }
    }

    // The answers the browser held, sent ahead of the next action: each form value is the row that posts it, or
    // an answer the select still shows though a render took its row away. One no row and no answer posts is not
    // the reader's, and is dropped.
    private Task HeldAsync(string field)
    {
        var view = Latest;
        var picked = new List<T>();
        foreach (var value in UiFormValues.Read(field))
        {
            if (view.Parts.Rows.Find(row => string.Equals(row.Option.FormValue, value, StringComparison.Ordinal)) is { } row)
            {
                if (row.Option.Disabled != true)
                {
                    picked.Add(row.Value);
                }

                continue;
            }

            if (Array.Find(view.Answers, answer => string.Equals(answer.Face.FormValue, value, StringComparison.Ordinal)) is { Face: not null } kept)
            {
                picked.Add(kept.Value);
            }
        }

        return CommitAsync(picked.Count > 1 ? [picked[0]] : picked);
    }
}
