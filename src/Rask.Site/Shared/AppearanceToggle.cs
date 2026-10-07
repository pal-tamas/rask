namespace Rask.Site;

/// <summary>
///     The moon in the top bar: one click flips the page between light and dark, as on fluxui.dev.
/// </summary>
/// <remarks>
///     Flux's own header: a subtle <c>Ui.Button</c> holding the moon, its resting ink set to zinc-300 in
///     both schemes as Flux's docs set theirs, a tooltip below it naming the <kbd>D</kbd> shortcut. It calls
///     <c>Rask.dark</c>, which <c>Ui.AppearanceScript</c> defines — no C# handler, so it works before
///     the app has booted and costs no handler id.
/// </remarks>
internal sealed partial class AppearanceToggle : Component
{
    private const string Label = "Toggle dark mode";

    // Only at rest: under the pointer the button's own subtle hover takes over, in either scheme.
    private const string RestingInk = "not-hover:text-zinc-300";

    // SEAM: the tooltip below becomes .Tooltip(Label).TooltipKbd("D") on the button when Flux's Ui.Tooltip lands.

    // Anchored to the button's end rather than centred under it: the moon is the last thing in the bar,
    // and a centred tooltip would hang off a phone's edge.
    private const string TooltipClass =
        "pointer-events-none absolute end-0 top-full z-10 mt-[5px] hidden whitespace-nowrap rounded-md "
        + "bg-zinc-800 px-2.5 py-2 text-xs font-medium text-white group-hover:block "
        + "group-has-focus-visible:block dark:border dark:border-white/10 dark:bg-zinc-700";

    // The shortcut the tooltip advertises. Not while typing, and not with a modifier held.
    private const string Shortcut =
        "(function(){if(window.raskAppearanceKey)return;window.raskAppearanceKey=true;"
        + "document.addEventListener('keydown',function(e){var t=e.target;"
        + "if(e.key!=='d'||e.metaKey||e.ctrlKey||e.altKey||e.repeat||!window.Rask)return;"
        + "if(t&&(t.isContentEditable||/^(INPUT|TEXTAREA|SELECT)$/.test(t.tagName)))return;"
        + "window.Rask.dark=!window.Rask.dark;});})();";

    /// <inheritdoc />
    protected override Component? HeadAssets => Script[Raw.Value(Shortcut)];

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class("group relative inline-flex")[
            Ui.Button.Subtle.Icon(Ui.IconName.Moon)
                .Class(RestingInk)
                .AriaLabel(Label)
                .AriaKeyShortcuts("D")
                .Attributes(("onclick", "Rask.dark=!Rask.dark"))
                .Data(new AttrBag("appearance-toggle", "")),
            Span.Class(TooltipClass).Aria("hidden", "true")[
                Label,
                Span.Class("ps-1 text-zinc-300")["D"]
            ]
        ];
}
