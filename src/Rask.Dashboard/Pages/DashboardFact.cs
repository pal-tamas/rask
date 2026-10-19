namespace Rask.Dashboard.Pages;

/// <summary>One line of <see cref="DashboardFacts" />: its name, and its value in Flux's strong text.</summary>
internal sealed partial class DashboardFact : Component
{
    public required string Label { get; set; }

    public string? Value { get; set; }

    /// <summary>The value is code — an id, a timestamp, a pragma — and is drawn in the monospace face.</summary>
    public bool? Mono { get; set; }

    /// <summary>Something is wrong: painted red.</summary>
    public bool? Danger { get; set; }

    /// <summary>Worth a look: painted amber.</summary>
    public bool? Warning { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Dt[Ui.Text[Label]],
        Dd[Ui.Text.Variant(Ui.TextVariant.Strong).Color(Ink).Class(Mono is true ? "font-mono break-all" : "break-words")[Value]]
    ];

    private Ui.Color? Ink => (Danger, Warning) switch
    {
        (true, _) => Ui.Color.Red,
        (_, true) => Ui.Color.Amber,
        _ => null,
    };
}
