namespace Rask.Site.Features.UiDocs;

/// <summary>
///     A component page's Behaviour section: what the browser, the runtime and the page each do, after the examples
///     and before the reference.
/// </summary>
/// <remarks>Its parts are <see cref="UiDocsTopic" />s given as children.</remarks>
internal sealed partial class UiDocsBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Section.Id("behaviour").Data("testid", "ui-behaviour").Class("mt-12 space-y-3")[
            Ui.Heading.Level(2).Xl["Behaviour"],
            Children
        ];
}
