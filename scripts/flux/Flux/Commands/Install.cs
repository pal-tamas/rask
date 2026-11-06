namespace Rask.Flux.Commands;

/// <summary>
///     Installs the browsers the harness launches, through Microsoft.Playwright's own installer: the revisions the pinned
///     binding expects, with no npm. <c>flux install chromium</c>, <c>flux install webkit</c>,
///     <c>flux install --with-deps chromium</c> on a fresh Linux runner.
/// </summary>
[FluxCommand("install", "the browsers, by Microsoft.Playwright's installer (chromium, webkit, --with-deps)")]
public sealed class Install : IFluxCommand
{
    /// <inheritdoc />
    public Task<int> Run(string[] args) => Task.FromResult(Microsoft.Playwright.Program.Main(["install", .. args.Length > 0 ? args : ["chromium"]]));
}
