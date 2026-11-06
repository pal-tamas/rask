using Microsoft.Playwright;

namespace Rask.Flux.Preparations;

/// <summary>
///     The composer as an app has it, for <c>flux parity composer --prepare composer-grown</c>: with the runtime's hooks,
///     which fit each box to its text as it arrives (rask-grow.ts). Flux's script fits its boxes on load, a wrapped
///     placeholder included — 60px at 390 wide, 32px on a desktop — and a page without the hooks is a box of its rows.
///     Nothing is done to either page; both are measured as loaded.
/// </summary>
[ParityPreparation("composer-grown")]
public sealed class ComposerGrown : IParityPreparation
{
    /// <inheritdoc />
    public string Flux(string slug) => "composer";

    /// <inheritdoc />
    public IReadOnlyList<string> Runtime => ["rask-hooks.ts"];

    /// <inheritdoc />
    public async Task Prepare(IPage page, string scheme, string side) =>
        // Fitted on arrival: two frames for the hooks to have read every box.
        await page.Run("() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done)))");
}
