namespace Rask;

/// <summary>A group of related fields under one heading.</summary>
/// <remarks>
/// Flux UI's fieldset: a real <c>&lt;fieldset&gt;</c>, so a screen reader announces the heading with each
/// control in it. Give the heading as <see cref="Legend" />, or place a <see cref="UiLegend" /> yourself.
/// </remarks>
public sealed partial class UiFieldset : Component
{
    // A legend over a description sits closer to it, and the description keeps the legend's distance.
    private const string Look =
        "m-0 min-w-0 border-0 p-0 [&>[data-ui-legend]:has(+[data-ui-description])]:mb-2 "
        + "[&>[data-ui-legend]+[data-ui-description]]:mb-4";

    /// <summary>The fieldset's heading.</summary>
    public string? Legend { get; set; }

    /// <summary>Help text under the heading.</summary>
    public string? Description { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Fieldset.Class(UiClass.Compose(Look, Class)).Data("ui-fieldset", "")[
            // A boundary: the heading's description is the fieldset's, not that of a field around it.
            Context.Provide(UiFieldScope.None)[
                Legend is null ? null : Ui.Legend[Legend],
                Description is null ? null : Ui.Description[Description],
                Children ?? []
            ]
        ];
}
