namespace Rask.Ui;

/// <summary>The rule between two crumbs.</summary>
public sealed partial class UiCrumbSeparator : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class("select-none text-sm text-ui-line").Attributes(("aria-hidden", "true"))["/"];
}
