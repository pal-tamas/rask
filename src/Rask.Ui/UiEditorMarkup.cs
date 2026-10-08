using System.Globalization;
using static Rask.Markup;

namespace Rask;

/// <summary>
///     The markup the editor's toolbar items share: the button, a toggle with its tooltip, and a select.
/// </summary>
/// <remarks>
///     Every control carries <c>data-editor="…"</c>, as Flux's do: it is the one thing the engine
///     (<c>Resources/editor/ui-editor.ts</c>) finds a control by. Written from the measurements of
///     fluxui.dev/components/editor.
/// </remarks>
internal static class UiEditorMarkup
{
    /// <summary>A toolbar button's look: 24px square around a 20px icon.</summary>
    internal const string ButtonClass =
        "flex items-center justify-center p-0.5 rounded-sm text-sm font-medium text-zinc-400 "
        + "hover:text-zinc-800 hover:bg-zinc-200 focus:text-zinc-800 "
        + "dark:hover:text-white dark:hover:bg-white/10 dark:focus:text-white "
        + "disabled:opacity-75 " + ButtonStates;

    // Measured in use: an open control and one whose formatting the caret is in are inked, and only a pressed
    // toggle is filled — the link button matches without being pressed, the blockquote button says neither.
    private const string ButtonStates =
        "data-open:bg-transparent data-open:text-zinc-800 data-match:text-zinc-800 aria-pressed:bg-zinc-200 "
        + "dark:data-open:bg-transparent dark:data-open:text-white dark:data-match:text-white dark:aria-pressed:bg-white/10";

    private const string OptionsClass =
        "fixed min-w-[160px] p-[5px] rounded-lg shadow-xs overflow-y-auto border border-zinc-200 bg-white "
        + "dark:border-zinc-600 dark:bg-zinc-700";

    private const string OptionClass =
        "flex h-8 cursor-default items-center gap-2 rounded-lg px-2 text-sm font-medium text-zinc-800 dark:text-white "
        + OptionActive;

    // Measured with the list open: the option the keyboard or the pointer is on, and its picture.
    private const string OptionActive =
        "data-active:bg-zinc-50 dark:data-active:bg-zinc-600 [&>svg]:shrink-0 [&>svg]:text-zinc-400 "
        + "data-active:[&>svg]:text-zinc-800 dark:data-active:[&>svg]:text-white";

    /// <summary>The bare button every toolbar control is: one tab stop per toolbar, the first control's.</summary>
    internal static HTMLButtonElement Button(string? name, string? extra = null)
    {
        var editor = Context.Get<UiEditorScope>();
        var stop = Context.Get<UiEditorToolbarScope>()?.TakeTabStop() ?? true;
        var button = Markup.Button.Class(UiClass.Compose(ButtonClass, extra)).TabIndex(stop ? 0 : -1).Attributes(("type", "button"));
        if (name is not null)
        {
            button = button.Data("editor", name);
        }

        return editor?.Disabled == true ? button.Attributes(("type", "button"), ("disabled", "")) : button;
    }

    /// <summary>A button that turns one kind of formatting on and off, named by its tooltip.</summary>
    internal static Component Toggle(string name, string label, string? kbd, Component icon) =>
        Ui.Tooltip.Content(label).Kbd(kbd).Class("contents")[Button(name)[icon]];

    /// <summary>
    ///     A select of the toolbar: a combobox button showing the chosen option's picture, and the listbox
    ///     the engine opens under it. The first option is the one chosen at rest.
    /// </summary>
    internal static Component Select(string name, string label, bool labelled, params (string Value, string Text, Func<Component> Icon)[] options)
    {
        var instance = UiInstanceCounter.Next().ToString(CultureInfo.InvariantCulture);
        var listId = "ui-editor-options-" + instance;
        var (first, firstText, firstIcon) = options[0];
        var trigger = Button(null, "[&_[data-value]>div]:sr-only [&_[data-value]>svg]:shrink-0")
            .Role("combobox")
            .Aria(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["controls"] = listId,
                ["haspopup"] = "listbox",
                ["expanded"] = "false",
            })[
                Div[Div.Data("value", first)[firstIcon(), Div[firstText]]],
                UiEditorIcons.Hero(Ui.IconName.ChevronDown, Ui.IconVariant.Micro)
            ];
        var list = Div.Id(listId).Class(OptionsClass).Role("listbox").TabIndex(-1).Attributes(("popover", "manual"));
        if (labelled)
        {
            list = list.Aria("label", label);
        }

        return Div.Class("relative").Data("editor", name)[
            Ui.Tooltip.Content(label).Class("contents")[trigger],
            list[options.Select((option, index) => Option(listId, index, option.Value, option.Text, option.Icon()))]
        ];
    }

    private static Component Option(string listId, int index, string value, string text, Component icon)
    {
        var option = Div.Key(value)
            .Id(listId + "-" + index.ToString(CultureInfo.InvariantCulture))
            .Class(OptionClass)
            .Role("option")
            .Aria("selected", index == 0 ? "true" : "false");
        var data = new Dictionary<string, string?>(StringComparer.Ordinal) { ["value"] = value };
        if (index == 0)
        {
            data["selected"] = null;
        }

        return option.Data(data)[icon, Div[text]];
    }
}
