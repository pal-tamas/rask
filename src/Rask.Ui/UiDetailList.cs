namespace Rask.Ui;

/// <summary>The key-and-value list a detail sheet is made of.</summary>
public sealed partial class UiDetailList : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("divide-y divide-ui-line")[Children ?? []];
}
