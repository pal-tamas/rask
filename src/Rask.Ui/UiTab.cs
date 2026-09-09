using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>One tab.</summary>
public sealed partial class UiTab : Component
{
    public required string Href { get; set; }

    public new required string Label { get; set; }

    public bool? Active { get; set; }

    /// <summary>A count shown beside the label, for a tab that filters a list.</summary>
    public string? Count { get; set; }

    /// <summary>Colours the count when it is a number worth acting on.</summary>
    public bool? Alarm { get; set; }

    /// <summary>Draws it as unavailable. Still a link — use it for a view that exists but has nothing in it.</summary>
    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        NavLink
            .Href(Href)
            .Role("tab")
            .Aria(new Dictionary<string, string?> { ["selected"] = Active == true ? "true" : "false" })
            .Class(UiClass.Compose(
                "tab gap-2 whitespace-nowrap",
                Active == true ? "tab-active" : "",
                Disabled == true ? "tab-disabled" : "",
                // 44px is the smallest reliable touch target and daisyUI's tab is shorter than that on
                // a phone; the height relaxes from sm up, where there is a pointer.
                "min-h-11 sm:min-h-0",
                Class))[
            Span[Label],
            Count is null
                ? null
                : Span.Class(Alarm == true
                    ? "rounded bg-error/10 px-1.5 py-0.5 text-xs tabular-nums text-error"
                    : "rounded bg-base-200 px-1.5 py-0.5 text-xs tabular-nums opacity-60")[Count]
        ];
}
