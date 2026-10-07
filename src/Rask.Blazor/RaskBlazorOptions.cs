using Rask.Core;

namespace Rask.Blazor;

/// <summary>Knobs for hosting Blazor components.</summary>
public sealed class RaskBlazorOptions
{
    /// <summary>
    ///     The scheme and host a hosted component's <c>NavigationManager</c> reports.
    /// </summary>
    /// <remarks>
    ///     Only the origin is read. The path and query are the page on screen, under the app's path
    ///     base — Rask's route carries no origin of its own, so this is where one comes from. Set it
    ///     when a hosted component prints absolute URLs.
    /// </remarks>
    public string BaseUri { get; set; } = "http://localhost/";

    /// <summary>
    ///     Stylesheets and scripts a hosted component library needs, added to the page's
    ///     <c>&lt;head&gt;</c> once however many islands are on it.
    /// </summary>
    /// <remarks>
    ///     Nothing about a hosted type says which package's assets it needs, so this cannot be
    ///     discovered — a Razor Class Library's <c>_content/…</c> files have to be named. Declared
    ///     once at startup rather than per island, because that is where the answer is the same for
    ///     every island in the app.
    /// </remarks>
    /// <example>
    ///     <code>
    ///     services.AddRaskBlazor(o =>
    ///         o.HeadAssets.Add(Link.Rel("stylesheet").Href("_content/MudBlazor/MudBlazor.min.css")));
    ///     </code>
    /// </example>
    public IList<Component> HeadAssets { get; } = [];
}
