namespace Rask.Ui;

/// <summary>
/// The bar across the top of a page.
/// </summary>
/// <remarks>
/// Three slots rather than children, because that is the shape daisyUI's navbar lays out — leading,
/// centre, trailing — and a single children list would leave the caller writing the three wrappers by
/// hand every time.
/// </remarks>
public sealed partial class UiNavbar : Component
{
    public Component? Start { get; set; }

    public Component? Center { get; set; }

    public Component? End { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(UiClass.Compose("navbar bg-base-100", Class))[
            Start is null ? null : Div.Class("navbar-start")[Start],
            Center is null ? null : Div.Class("navbar-center")[Center],
            End is null ? null : Div.Class("navbar-end")[End]
        ];
}
