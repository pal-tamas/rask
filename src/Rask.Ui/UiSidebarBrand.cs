using Rask.Core.Routing;

namespace Rask;

/// <summary>Flux's <c>flux:sidebar.brand</c>: the logo and the name at the top of a <see cref="UiSidebar" />.</summary>
/// <remarks>Narrowed to the rail it is the logo alone, and gives its place to the collapse control on hover.</remarks>
public sealed partial class UiSidebarBrand : Component
{
    private const string Root =
        "flex h-10 min-w-0 items-center gap-2 px-2 sidebar-rail:w-full sidebar-rail:justify-center sidebar-rail:px-0 "
        + "sidebar-rail:has-[+[data-ui-sidebar-collapse]]:group-hover/sidebar:opacity-0";

    private const string Image = "h-6 min-w-6";

    /// <summary>Where the brand leads. A generated route navigates inside the app.</summary>
    public RouteUrl? Href { get; set; }

    /// <summary>The logo image's URL.</summary>
    public string? Logo { get; set; }

    /// <summary>The logo shown in dark mode instead. Flux's <c>logo:dark</c>.</summary>
    public string? LogoDark { get; set; }

    /// <summary>The name beside the logo.</summary>
    public string? Name { get; set; }

    /// <summary>Classes for the brand link.</summary>
    public string? Class { get; set; }

    private Component Mark(string logo) =>
        Div.Class("flex size-6 min-w-6 shrink-0 items-center justify-center overflow-hidden rounded-sm")[
            Img.Src(logo).Alt("").Class(LogoDark is null ? Image : "h-6 min-w-6 dark:hidden"),
            LogoDark is { Length: > 0 } dark ? Img.Src(dark).Alt("").Class("hidden h-6 min-w-6 dark:block") : null
        ];

    /// <inheritdoc />
    protected override Component? Render()
    {
        Component[] content =
        [
            Logo is { Length: > 0 } logo ? Mark(logo) : null!,
            Name is { Length: > 0 } name
                ? Div.Class("min-w-0 truncate text-sm font-medium text-zinc-800 sidebar-rail:hidden dark:text-zinc-100")[name]
                : null!
        ];

        var classes = UiClass.Compose(Root, Class);
        (string, string?)[] marks = [("data-ui-sidebar-brand", "")];
        return Href switch
        {
            null => Div.Class(classes).Attributes(marks)[content],
            { PageType: null } url => A.Href(url.ToString()).Class(classes).Attributes(marks)[content],
            { } url => NavLink.Href(url).ActiveClass("").Class(classes).Attributes(marks)[content],
        };
    }
}
