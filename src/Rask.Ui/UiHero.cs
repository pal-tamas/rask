namespace Rask.Ui;

/// <summary>
/// A full-width banner with its content centred.
/// </summary>
public sealed partial class UiHero : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("hero", Class))[
            Div.Class("hero-content text-center")[Children ?? []]
        ];
}
