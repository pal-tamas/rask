namespace Rask.Ui;

/// <summary>An inline notice: a confirmation to answer, or the result of an action just taken.</summary>
public sealed partial class UiNotice : Component
{
    /// <summary>One of <c>danger</c>, <c>warn</c>, <c>info</c>. Anything else reads as neutral.</summary>
    public string? Tone { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Role("alert")
            .Class($"mb-4 flex flex-wrap items-center gap-3 rounded-xl border px-4 py-3 text-sm {Palette()}")[
            Children ?? []
        ];

    private string Palette() => Tone switch
    {
        "danger" => "border-ui-danger/30 bg-error/5 text-error",
        "warn" => "border-ui-warn/40 bg-warning/10 text-base-content",
        "info" => "border-ui-brand/30 bg-primary/5 text-base-content",
        _ => "border-base-300 bg-base-200 opacity-60",
    };
}
