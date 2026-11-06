using System.Globalization;
using Rask.Core.Forms;
using Rask.Core.Globalization;

namespace Rask;

/// <summary>
///     A long form's tabs, inside the form: <c>Form.Model(m).OnSubmit(Save)[Ui.FormTabs[Ui.FormTab…, Ui.FormTab…], …]</c>.
///     A tab holding fields with messages says how many in a red badge, and a refused save opens the first tab
///     that holds one.
/// </summary>
/// <remarks>
///     <para>
///     Flux's tabs (<see cref="UiTabGroup" />, <see cref="UiTabs" />, <see cref="UiTab" />, <see cref="UiTabPanel" />)
///     with Flux's badge on each tab, red: the keyboard, the ARIA and the look are theirs. The row scrolls sideways
///     rather than widening a phone's page.
///     </para>
///     <para>
///     It reads the form it is in — the enclosing <c>Form</c>'s state — so the page hands it nothing: the counts
///     follow the messages as fields are corrected, and every refused submit (the rules failed, or the save refused
///     with field failures) opens the first tab holding an invalid field, wherever the reader had gone. A field no
///     tab holds is counted by none.
///     </para>
///     <para>
///     Which tab is open is the browser's, as a <see cref="UiTabs" />' is: a press shows its panel at once and sends
///     nothing, and the choice travels with the reader's next action. Every panel stays in the form, so what was
///     typed on a tab that is not shown is submitted with the rest.
///     </para>
/// </remarks>
public sealed partial class UiFormTabs : Component
{
    // The open tab, bound to the row: null until the reader or a refusal chooses one, so the first tab opens.
    private string? _open;

    // The last refusal count this row answered, and the form it was counted on.
    private int _refusals;
    private EditContext? _form;

    // The counts the row last drew, so a change of messages outside a render is drawn only when it moves one.
    private int[] _drawn = [];

    public string? Class { get; set; }

    private string Open
    {
        get => _open ?? string.Empty;
        set => _open = value;
    }

    // What it reads is the form's messages, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var tabs = (Children ?? []).OfType<UiFormTab>().ToList();
        if (tabs.Count == 0)
        {
            return null;
        }

        // Drawn again on its own (a message changed outside a render, below) it is outside the form's scope, and
        // still the form's: the one it last read is the one it is in.
        var form = EditContextScope.Current ?? _form;
        Watch(form);
        var counts = form is null ? new int[tabs.Count] : Counts(tabs, form);
        if (form is not null && form.Refusals != _refusals)
        {
            _refusals = form.Refusals;
            var first = Array.FindIndex(counts, count => count > 0);
            if (first >= 0)
            {
                _open = tabs[first].Name;
            }
        }

        _drawn = counts;

        // A tab the page no longer draws (one shown only for a saved record) leaves the first open.
        if (_open is null || !tabs.Exists(tab => string.Equals(tab.Name, _open, StringComparison.Ordinal)))
        {
            Open = tabs[0].Name;
        }

        return Ui.TabGroup.Class(Class)[
            // Scrollable: a long form's row of tabs is wider than a phone.
            Ui.Tabs.Bind(() => Open).Scrollable()[
                tabs.Select((tab, i) => Ui.Tab.Key(tab.Name).Name(tab.Name)[tab.Label, Count(counts[i])])
            ],
            tabs
        ];
    }

    /// <inheritdoc />
    protected override Task OnUnmount()
    {
        Watch(null);
        return Task.CompletedTask;
    }

    // How many invalid fields each tab holds, in the order of the tabs.
    private static int[] Counts(List<UiFormTab> tabs, EditContext form)
    {
        var invalid = form.InvalidFields();
        var counts = new int[tabs.Count];
        if (invalid.Count == 0)
        {
            return counts;
        }

        for (var i = 0; i < tabs.Count; i++)
        {
            var held = tabs[i].FieldsOn(form);
            counts[i] = invalid.Count(held.Holds);
        }

        return counts;
    }

    // The red count on a tab, and the sentence a screen reader hears in its place: a bare "2" after the label
    // would be read as part of its name.
    private static Component? Count(int count)
    {
        if (count == 0)
        {
            return null;
        }

        var number = count.ToString(CultureInfo.CurrentCulture);
        return Span.Data("ui-form-tab-count", number)[
            Span.Aria("hidden", "true")[Ui.Badge.Sm.Color(Ui.Color.Red)[number]],
            Span.Class("sr-only")[RaskStrings.Get(RaskString.FormTabsInvalid, "Fields to correct: {0}", number)]
        ];
    }

    // A field corrected in a part of the form that draws itself (an editor of its own) re-renders that part and
    // not this row: the form's messages changing is what tells the row, and only a count that moved draws it.
    private void Watch(EditContext? form)
    {
        if (ReferenceEquals(form, _form))
        {
            return;
        }

        if (_form is not null)
        {
            _form.ValidationStateChanged -= MessagesChanged;
        }

        _form = form;
        if (form is not null)
        {
            // Refusals counted before the row first saw this form are not this row's to answer.
            _refusals = form.Refusals;
            form.ValidationStateChanged += MessagesChanged;
        }
    }

    private void MessagesChanged(object? sender, EventArgs e)
    {
        if (_form is not { } form || (Children ?? []).OfType<UiFormTab>().ToList() is not { Count: > 0 } tabs)
        {
            return;
        }

        if (!Counts(tabs, form).AsSpan().SequenceEqual(_drawn))
        {
            StateHasChanged();
        }
    }
}
