using Microsoft.JSInterop;
using Rask.Ui;

namespace Rask.Site;

/// <summary>
///     The site's theme picker: every theme the kit ships, and the one a reader chose is the one they
///     get back on the next page and the next visit.
/// </summary>
/// <remarks>
///     <para>
///         It replaces <c>UiThemeDropdown</c> in the two headers. That component is daisyUI's CSS-only
///         <c>theme-controller</c> — a radio group the stylesheet matches with no script at all, which is
///         elegant and cannot remember anything: there is nothing to write a choice with, and the radio
///         renders <c>checked=false</c> on every pass, so the next render dropped the selection. On this
///         site "the next render" is the WASM first frame or any navigation, so the theme reset roughly
///         the moment it was picked. <c>UiThemePicker</c> says as much in its own remarks and points an
///         app that wants it remembered at exactly this: own the value.
///     </para>
///     <para>
///         <b>C# owns the value; the document attribute stays with the boot script.</b> Choosing calls
///         <c>window.raskSetTheme</c> (see <c>App.ThemeInitJs</c>), which writes
///         <c>localStorage['rask-theme']</c> and stamps <c>data-theme</c> on <c>&lt;html&gt;</c>. That
///         split is what keeps it flicker-free: the script is the only code that runs before the first
///         paint, so a saved dark theme reaches the very first frame instead of correcting itself once
///         C# had read storage — and the script's <c>raskAfterMorph</c> hook re-applies it after a
///         full-document morph, which strips <c>&lt;html&gt;</c>'s attributes.
///     </para>
///     <para>
///         The current theme is read back once, after the first render, so the list can mark it. Reading
///         it late is harmless — it decides a checkmark, not a palette — and it is the only way to learn
///         it: the value lives in the reader's browser, and on a prerendered page nothing in C# has seen
///         it yet.
///     </para>
///     <para>
///         The disclosure is left UNCONTROLLED, and closing after a pick is a consequence rather than an
///         omission: a native <c>&lt;details&gt;</c> sets its own <c>open</c> attribute, this renders
///         none, and the re-render the click causes morphs that attribute away. A 35-item list left
///         hanging over the page after a choice reads as the click not having registered.
///     </para>
/// </remarks>
public sealed partial class ThemeMenu(IJSRuntime js) : Component
{
    /// <summary>Alignment, as one of daisyUI's placement classes (for example <c>dropdown-end</c>).</summary>
    public string? Placement { get; set; }

    // What the reader has chosen, as daisyUI names it. Starts on the same default the boot script falls
    // back to, so the two never disagree before the read below lands.
    private string _theme = UiTheme.Value(UiThemeName.Light);

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        // Checked against the kit's own list rather than trusted: this is the reader's localStorage, and
        // it decides which control draws itself as active. A junk value leaves the default marked.
        var saved = await js.InvokeAsync<string>("raskTheme");
        if (!string.IsNullOrEmpty(saved)
            && saved != _theme
            && UiTheme.All.Any(theme => UiTheme.Value(theme) == saved))
        {
            _theme = saved;
            StateHasChanged();
        }
    }

    private async Task Choose(UiThemeName theme)
    {
        _theme = UiTheme.Value(theme);
        await js.InvokeVoidAsync("raskSetTheme", _theme);
    }

    /// <inheritdoc />
    protected override Component? Render() =>
        Details.Class(Placement is null ? "dropdown" : "dropdown " + Placement)[
            Summary.Class("btn btn-sm")[
                UiIcon.Name(UiIconName.Sparkles).Class("size-4 shrink-0"),
                Span["Theme"]
            ],
            Ul.Class("menu dropdown-content z-1 max-h-96 w-52 flex-nowrap overflow-y-auto rounded-box "
                     + "bg-base-100 p-2 shadow-sm")[
                UiTheme.All.Select(theme =>
                {
                    var value = UiTheme.Value(theme);
                    var chosen = value == _theme;

                    // Keyed by the theme's own name — RASK022 holds every list to identity rather than
                    // position, and the name is the identity here.
                    return Li.Key(value)[
                        Button
                            .Type("button")
                            .Class(chosen
                                ? "menu-active flex w-full justify-between"
                                : "flex w-full justify-between")
                            .Aria(new Dictionary<string, string?>
                            {
                                ["pressed"] = chosen ? "true" : "false"
                            })
                            .OnClickAsync(() => Choose(theme))[
                            Span[value],
                            // The palette itself, as four chips — the point of a theme list is seeing
                            // what you are choosing. `data-theme` on the swatch renders each row in its
                            // OWN theme's colours, off the same attribute the page uses.
                            Span
                                .Class("flex shrink-0 items-center gap-1")
                                .Attributes(("data-theme", value))[
                                Span.Class("size-2 rounded-full bg-primary"),
                                Span.Class("size-2 rounded-full bg-secondary"),
                                Span.Class("size-2 rounded-full bg-accent"),
                                Span.Class("size-2 rounded-full bg-neutral")
                            ]
                        ]
                    ];
                })
            ]
        ];
}
