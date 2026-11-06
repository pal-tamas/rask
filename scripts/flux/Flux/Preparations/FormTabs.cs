using Microsoft.Playwright;

namespace Rask.Flux.Preparations;

/// <summary>
///     <c>Ui.FormTabs</c>, the kit's own tabs for a long form, for <c>flux parity form-tabs --prepare form-tabs</c>. Flux has
///     no such part: the kit draws it of Flux's tabs, so its row is held to the tabs page's scrollable example
///     (<c>FormTabsParity</c>). Nothing is done to either page; both are measured as loaded.
/// </summary>
[ParityPreparation("form-tabs")]
public sealed class FormTabs : IParityPreparation
{
    /// <inheritdoc />
    public string Flux(string slug) => "tabs";

    /// <inheritdoc />
    public IReadOnlyList<string> Runtime => [];

    /// <inheritdoc />
    public Task Prepare(IPage page, string scheme, string side) => Task.CompletedTask;
}
