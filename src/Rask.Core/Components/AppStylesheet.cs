using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Core.Components;

/// <summary>
///     The app's own compiled stylesheet (<c>css/app.css</c>), linked under its content hash — for the
///     <c>HeadAssets</c> of a hand-wired host (<c>AddRask()</c> + <c>MapRask&lt;TApp&gt;()</c>), which writes its own
///     document: <c>protected override Component? HeadAssets =&gt; [Title["Shop"], AppStylesheet];</c>
/// </summary>
/// <remarks>
///     The same <c>&lt;link rel="stylesheet" href="{base}/css/app.css?v={hash}"&gt;</c> <c>RaskApp</c> writes, so a
///     browser keeps the sheet until a rebuild changes it. On a <c>RaskApp</c> page it is that link, written once.
///     Renders nothing where the host serves no sheet of its own (a WebAssembly app, whose host links it already).
/// </remarks>
public sealed partial class AppStylesheet : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        LiveRenderContext.Current?.Services?.GetService<IAppStylesheetSource>() is { } sheet
            ? Of(sheet.Path, sheet.Hash)
            : null;

    // The link itself, for this entry and for the document RaskApp starts every page with (RootErrorBoundary).
    internal static Component Of(string path, string? hash) =>
        Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/" + path + (hash is null ? "" : "?v=" + hash));
}
