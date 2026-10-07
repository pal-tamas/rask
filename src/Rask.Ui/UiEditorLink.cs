using System.Globalization;

namespace Rask;

/// <summary>
///     Link insertion: the <c>link</c> item of an editor's toolbar, Flux's <c>flux:editor.link</c>. Its button
///     opens a small panel with the address, a button that sets it and one that removes the link.
/// </summary>
public sealed partial class UiEditorLink : Component
{
    private static readonly UiPartMarker Dropdown = new("ui-dropdown");

    private static readonly UiPartMarker Panel = new("ui-editor-link");

    private const string PanelClass =
        "fixed min-w-[360px] p-[5px] rounded-lg shadow-xs overflow-y-auto border border-zinc-200 bg-white "
        + "dark:border-zinc-600 dark:bg-zinc-700";

    // The panel's two buttons: the toolbar button's box, and nothing on hover — measured.
    private const string ActionClass =
        "p-0.5 rounded-sm text-sm font-medium text-zinc-400";

    private readonly int _instance = UiInstanceCounter.Next();

    /// <inheritdoc />
    protected override Component? Render()
    {
        var panelId = "ui-editor-link-" + _instance.ToString(CultureInfo.InvariantCulture);
        var trigger = UiEditorMarkup.Button(null)
            .Data("match-target", null)
            .Aria(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["haspopup"] = "true",
                ["controls"] = panelId,
                ["expanded"] = "false",
            })[UiEditorIcons.Link()];

        return Div.Class("contents").Data(Dropdown.With(null, "editor", "link"))[
            Ui.Tooltip.Content("Insert link").Kbd("⌘K").Class("contents")[trigger],
            Div.Id(panelId).Class(PanelClass).TabIndex(-1).Attributes(("popover", "manual"))[
                Div.Class("flex justify-between gap-2 ps-2 pe-1").Data(Panel.With(null))[
                    Input.Value(string.Empty)
                        .Class("h-8 flex-1 text-sm outline-none")
                        .Data("editor", "link:url")
                        .Attributes(("type", "text"), ("placeholder", "https://..."), ("autofocus", "")),
                    Div.Class("flex items-center gap-2")[
                        Action("link:insert", "Insert link", UiEditorIcons.Hero(Ui.IconName.Check, Ui.IconVariant.Solid, "shrink-0")),
                        Action("link:unlink", "Unlink", UiEditorIcons.Unlink(panelId + "-clip"))
                    ]
                ]
            ]
        ];
    }

    private static Component Action(string name, string label, Component icon) =>
        Ui.Tooltip.Content(label).Class("contents")[
            Button.Class(ActionClass).Data("editor", name).Attributes(("type", "button"))[icon]
        ];
}
