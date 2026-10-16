using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     The rows of a <see cref="UiCommand" /> palette, which is still drawn with daisyUI's <c>menu</c> until it is
///     rebuilt on Flux's <c>flux:command.item</c>.
/// </summary>
/// <remarks>
///     A palette's children are written as <see cref="UiMenuItem" />, <see cref="UiMenuGroup" /> and
///     <see cref="UiMenuSeparator" />, which are Flux's menu rows now. Inside a palette they hand over to these,
///     so the palette keeps the rows it had: <c>option</c>s in a <c>listbox</c>, filtered by its query.
/// </remarks>
internal static class UiCommandRows
{
    /// <summary>An <c>option</c> the palette's cursor can land on, with the words its query is matched against.</summary>
    internal static Component Option(
        UiMenuLevel level,
        string text,
        Ui.IconName? icon,
        string? kbd,
        RouteUrl? href,
        bool disabled,
        Callback onClick,
        string? @class)
    {
        var scope = level.Scope;
        var ordinal = scope.Register(level.Parent, text, disabled, isSub: false);
        // Where the highlight starts: the first command that can be picked. The runtime moves it from there.
        var active = !disabled && scope.Entries.Take(ordinal).All(earlier => earlier.Disabled);
        var classes = disabled ? "menu-disabled" : null;
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { ["selected"] = active ? "true" : "false" };
        if (disabled)
        {
            aria["disabled"] = "true";
        }

        Component[] content =
        [
            icon is { } leading ? Ui.Icon.Name(leading).Class("size-4 shrink-0") : null!,
            Markup.Span.Class("grow")[text],
            kbd is null ? null! : Markup.Kbd.Class("kbd kbd-xs ui-menu-kbd")[kbd]
        ];

        Component row;
        if (href is { Path: not null } link && !disabled)
        {
            row = Markup.NavLink.Href(link).ActiveClass("").Class(classes)
                .Id(scope.ItemId(ordinal)).Data("rask-match", text).Role("option").TabIndex(-1).Aria(aria)[content];
        }
        else
        {
            var button = Markup.Button.Type(ButtonType.Button).Class(classes)
                .Id(scope.ItemId(ordinal)).Data("rask-match", text).Role("option").TabIndex(-1).Aria(aria);
            row = (disabled ? button : button.OnClick(onClick))[content];
        }

        return Markup.Li.Class(@class).Role("none")[row];
    }

    /// <summary>The line between two runs of commands. The runtime hides it while a query narrows the list.</summary>
    internal static Component Separator() => Markup.Li.Role("separator").Class("ui-menu-separator");

    /// <summary>The words above a run of commands.</summary>
    internal static Component Heading(string heading) =>
        Markup.Li.Class("menu-title").Role("presentation")[heading];
}
