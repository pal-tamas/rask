namespace Rask.Site.Features.UiDocs;

/// <summary>How the header layout behaves: the grid its parts make, the container, the inset main and the phone.</summary>
internal sealed partial class UiHeaderBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            UiDocsTopic.Key("grid").Heading("The layout grid").Paragraphs([
                "`Ui.Header`, `Ui.Sidebar` and `Ui.Main` are siblings, and whatever holds the `Ui.Main` (the body, or "
                + "a wrapper as in these examples) becomes the layout grid: the header across the top, the sidebar down "
                + "the side, the main in what is left. A header written before the sidebar runs the full width above "
                + "it; written after, it sits beside it, which is the sidebar layout. Keep the three direct siblings.",
                "Nothing is painted that Flux does not paint: the header's ground and border are your classes, "
                + "exactly as in Flux's examples. `Sticky(true)` keeps the header at the top while the page scrolls "
                + "under it, and `Container(true)` holds what is in it to the container width, centred, while its "
                + "ground runs edge to edge.",
            ]),
            UiDocsTopic.Key("inset").Heading("The inset main").Paragraphs([
                "`Ui.Main.Inset(true)` draws the content on a bordered, rounded panel with a gutter round it. From "
                + "`lg` up the grid fills the viewport (`100dvh`) and the panel scrolls inside itself; below it the "
                + "panel is the full width and the page scrolls. `Ui.Main` is a `<div>`, as Flux writes it: put a "
                + "`<main>` inside it (`Ui.Main[Main[Outlet]]`) for the landmark Rask focuses after a navigation.",
            ]),
            UiDocsTopic.Key("phone").Heading("On a phone").Paragraphs([
                "Below `lg` the header's navbar is hidden (`max-lg:hidden`, your class) and its links are a "
                + "`Ui.Sidebar.Collapsible(Ui.SidebarCollapsible.Mobile)` that `lg:hidden` keeps off a desktop. "
                + "`Ui.SidebarToggle` slides it over the page: a `<label>` for the sidebar's checkbox, with a button's "
                + "role and a tab stop, which the runtime presses on Enter and Space. From the sidebar's breakpoint up "
                + "there is nothing to slide and the toggle is not shown, so Flux's `lg:hidden` on it is not needed.",
                "The icon-only items on the right of the header take Flux's `label`: `.Label(\"Search\")` writes it on "
                + "the link exactly as Flux does, a `label` attribute and nothing else (no `aria-label`, no tooltip). "
                + "Flux writes `logo:dark` on the sidebar's brand only (`Ui.SidebarBrand.LogoDark`); in the header it "
                + "writes a second `flux:brand` for dark mode, and these examples write one, with a mark that reads on "
                + "both grounds.",
            ]),
            UiDocsTopic.Key("demos").Heading("The demos").Paragraphs([
                "A layout is a whole document, so each result above is its demo page in a frame: a desktop scaled to "
                + "the column, or the column's own width on a phone. Fullscreen opens the page itself, as Flux's "
                + "Fullscreen opens its demo.",
            ])
        ];
}
