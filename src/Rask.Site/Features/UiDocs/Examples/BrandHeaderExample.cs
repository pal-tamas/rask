namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class BrandHeaderExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex min-h-14 items-center rounded-lg border border-zinc-100 bg-zinc-50 px-4 dark:border-white/5 dark:bg-zinc-800")[
            Ui.Brand.Href("#").Name("Acme Inc.")
                .Logo(I.Class("font-serif font-bold")["A"])
                .LogoClass("bg-zinc-800 text-white dark:bg-white dark:text-zinc-800"),
            Ui.Navbar.Class("max-sm:hidden")[
                Ui.NavbarItem.Key("home").Href("#").Current(true)["Home"],
                Ui.NavbarItem.Key("inbox").Href("#").Badge("12")["Inbox"]
            ],
            Div.Class("flex-1"),
            Ui.Profile.Circle().Chevron(false).Avatar("/img/avatars/amber.svg")
        ];
}
