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
    // What Tailwind's preflight gives every app the kit is used in; the kit ships none of its own. In the
    // `base` layer, as preflight is: unlayered, it would outrank every padding and margin utility the kit writes.
    private const string Reset =
        "@layer base{*,::before,::after{box-sizing:border-box;border:0 solid;margin:0;padding:0}"
        + "html{line-height:1.5;-webkit-text-size-adjust:100%;font-family:Inter,sans-serif}"
        + "button,input,select,textarea{font:inherit;letter-spacing:inherit;color:inherit;background:transparent;border-radius:0}"
        + "a{color:inherit;text-decoration:inherit}svg,img{display:block;vertical-align:middle}"
        + "h1,h2,h3,h4,h5,h6{font-size:inherit;font-weight:inherit}ol,ul,menu{list-style:none}"
        + "}[data-preview-wrapper]{padding:64px 24px}";

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

    private static string Render(FluxParity parity)
    {
        var page = new StringBuilder()
            .Append("<!doctype html><html lang=\"en\" ").Append(UiStylesheet.ThemeScopeAttribute).Append("><head>")
            .Append("<meta charset=\"utf-8\"><title>").Append(parity.Page).Append(" · parity</title>")
            .Append("<link href=\"https://fonts.bunny.net/css?family=inter:400,500,600&display=swap\" rel=\"stylesheet\">")
            .Append("<style>").Append(Reset).Append("</style>")
            .Append("<style>").Append(UiStylesheet.Css).Append("</style>")
            // Flux's own switch: a `dark` class on the root. The measurer asks for each scheme in turn.
            .Append("<script>if(matchMedia('(prefers-color-scheme: dark)').matches)")
            .Append("document.documentElement.classList.add('dark')</script>")
            .Append("</head><body>");

        foreach (var (section, example) in parity.Examples())
        {
            page.Append("<div data-preview-wrapper data-section=\"").Append(section).Append("\">")
                .Append(example.ToHtml())
                .Append("</div>");
        }

        return page.Append("</body></html>").ToString();
    }
}
