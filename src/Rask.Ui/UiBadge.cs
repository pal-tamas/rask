namespace Rask.Ui;

/// <summary>A small status pill.</summary>
public sealed partial class UiBadge : Component
{
    public required string Label { get; set; }

    /// <summary>One of <c>danger</c>, <c>warn</c>, <c>info</c>, <c>ok</c>. Anything else reads as neutral.</summary>
    public string? Tone { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class(
            $"inline-flex items-center rounded-md px-1.5 py-0.5 text-xs font-medium {Palette()} {Class}")[
            Label
        ];

    private string Palette() => Tone switch
    {
        "danger" => "bg-error/10 text-error",
        "warn" => "bg-warning/15 text-warning",
        "info" => "bg-primary/10 text-primary",
        "ok" => "bg-success/10 text-success",
        _ => "bg-base-200 opacity-60",
    };
}
