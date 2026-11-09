using Rask.Core.Live;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     Tells the prerender pass what the app frame demo's <c>drivers/{Id}</c> expands to.
/// </summary>
/// <remarks>
///     The drivers list links every driver's page, and the site is a static publish: a parameterised route the pass
///     is not told about is no file, so each of those links would land on nothing. Built from
///     <see cref="AppFrameDemoDrivers.All" />, the list the table is drawn from, so a driver added there is written too.
/// </remarks>
public sealed class AppFrameDemoPrerenderPaths : IPrerenderPaths
{
    /// <inheritdoc />
    public IEnumerable<string> Paths() =>
        AppFrameDemoDrivers.All.Select(driver => (string)Routes.AppFrameDriverDemoPage(driver.Id));
}
