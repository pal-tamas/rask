namespace Rask.Ui;

/// <summary>
/// The foot of a page.
/// </summary>
public sealed partial class UiFooter : Component
{
    /// <summary>Lays the columns out in a row rather than stacked.</summary>
    public bool? Horizontal { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Footer.Class(UiClass.Compose(
            "footer",
            Horizontal == true ? "footer-horizontal" : "",
            "bg-base-200 p-10",
            Class))[Children ?? []];
}
