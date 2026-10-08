using System.Text;

namespace Rask.UiTests.Flux;

/// <summary>
///     Writes one static page per <see cref="FluxParity" />, for <c>scripts/flux/parity.mjs</c> to measure
///     against Flux UI's live documentation.
/// </summary>
/// <remarks>
///     The page is the kit and nothing else: the shipped stylesheet inlined, the reset a Tailwind app
///     already has, and Inter — the face Flux's docs are set in, and widths are measured in it. It lands in
///     <c>artifacts/flux-parity/rask/</c>, which is ignored.
/// </remarks>
public sealed class FluxParityPages
{
    // What Tailwind's preflight gives every app the kit is used in; the kit ships none of its own. In
    // `@layer base`, where preflight is: unlayered, `*{margin:0;padding:0}` would beat every utility the kit
    // writes. What the DOCS page hands an example by inheritance (ink, font, line height) is not here:
    // parity.mjs copies it from each example's Flux twin before measuring.
    private const string Reset =
        "@layer base{*,::before,::after{box-sizing:border-box;border:0 solid;margin:0;padding:0}"
        + "html{line-height:1.5;-webkit-text-size-adjust:100%;font-family:Inter,sans-serif}"
        + "button,input,select,textarea{font:inherit;letter-spacing:inherit;color:inherit;background:transparent;border-radius:0}"
        + "a{color:inherit;text-decoration:inherit}svg,img{display:block;vertical-align:middle}img{max-width:100%;height:auto}"
        + "h1,h2,h3,h4,h5,h6{font-size:inherit;font-weight:inherit}ol,ul,menu{list-style:none}"
        + "table{text-indent:0;border-color:inherit;border-collapse:collapse}"
        + "div[data-preview-wrapper]{padding:64px 24px}"
        // FluxParity.SpaceY: Tailwind's space-y-*, which the examples on Flux's pages are stacked with.
        + "[data-space-y='2']>:not(:last-child){margin-bottom:8px}[data-space-y='3']>:not(:last-child){margin-bottom:12px}"
        + "[data-space-y='4']>:not(:last-child){margin-bottom:16px}[data-space-y='6']>:not(:last-child){margin-bottom:24px}"
        + "[data-space-y='8']>:not(:last-child){margin-bottom:32px}}";

    public static string Directory { get; } = Path.Combine(RepoRoot.FullPath, "artifacts", "flux-parity", "rask");

    [Fact]
    public void Every_parity_page_is_written_for_the_browser_to_measure()
    {
        var parities = typeof(FluxParity).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(FluxParity)))
            .Select(type => (FluxParity)Activator.CreateInstance(type)!)
            .ToList();

        foreach (var parity in parities)
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path.Combine(Directory, parity.Page + ".html"), Render(parity));
        }

        Assert.All(parities, parity => Assert.NotEmpty(parity.Examples()));
    }

    [Fact]
    public void Every_layout_demo_is_written_as_a_document_of_its_own()
    {
        var layouts = typeof(FluxLayoutParity).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(FluxLayoutParity)))
            .Select(type => (FluxLayoutParity)Activator.CreateInstance(type)!)
            .ToList();

        foreach (var layout in layouts)
        {
            var folder = System.IO.Directory.CreateDirectory(Path.Combine(Directory, layout.Page)).FullName;
            foreach (var (demo, bodyClass, body) in layout.Demos())
            {
                File.WriteAllText(
                    Path.Combine(folder, demo + ".html"),
                    Head(layout.Page + " · " + demo)
                        .Append("<style>").Append(layout.AppUtilities).Append("</style>")
                        .Append("</head><body class=\"").Append(bodyClass).Append("\">")
                        .Append(body.ToHtml())
                        .Append("</body></html>").ToString());
            }
        }

        Assert.All(layouts, layout => Assert.NotEmpty(layout.Demos()));
    }

    private static string Render(FluxParity parity)
    {
        var page = Head(parity.Page).Append("</head><body>");

        foreach (var (section, example) in parity.Examples())
        {
            page.Append("<div data-preview-wrapper data-section=\"").Append(section).Append("\">")
                .Append(example.ToHtml())
                .Append("</div>");
        }

        return page.Append("</body></html>").ToString();
    }

    private static StringBuilder Head(string title) =>
        new StringBuilder()
            .Append("<!doctype html><html lang=\"en\" ").Append(UiStylesheet.ThemeScopeAttribute).Append("><head>")
            .Append("<meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>").Append(title).Append(" · parity</title>")
            .Append("<link href=\"https://fonts.bunny.net/css?family=inter:400,500,600&display=swap\" rel=\"stylesheet\">")
            // The kit's sheet first: it states the layer order, and `base` has to take its place in it.
            .Append("<style>").Append(UiStylesheet.Css).Append("</style>")
            .Append("<style>").Append(Reset).Append("</style>")
            // Flux's own switch: a `dark` class on the root. The measurer asks for each scheme in turn.
            .Append("<script>if(matchMedia('(prefers-color-scheme: dark)').matches)")
            .Append("document.documentElement.classList.add('dark')</script>");
}
