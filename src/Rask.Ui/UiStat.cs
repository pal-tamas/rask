using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>One number, with what it counts and where to go for the detail behind it.</summary>
public sealed partial class UiStat : Component
{
    public required string Value { get; set; }

    public required string Label { get; set; }

    // NULLABLE, not defaulted. A property with an initialiser is excluded from the chain altogether, and
    // one that is non-nullable without an initialiser becomes a REQUIRED step (RASK001) — so an optional
    // step is spelled by making the property nullable, and only that.
    public UiIconName? Icon { get; set; }

    public string? Caption { get; set; }

    /// <summary>
    ///     <see cref="UiTone.Error" /> for a number an operator must act on, <see cref="UiTone.Warning" /> for
    ///     one that is merely unproven. Every other tone reads as neutral.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Two levels rather than one boolean, and the distinction is the point: a tile that goes red every
    ///     time a check races replication is a tile operators learn to ignore, so "we could not prove this"
    ///     has to look different from "this is broken".
    ///     </para>
    ///     <para>
    ///     A <see cref="UiTone" />, like every other tone in the kit. It was a string matched against
    ///     <c>"danger"</c> and <c>"warn"</c> — the names nothing else here uses — so the natural
    ///     <c>"error"</c> compiled and rendered a neutral tile without a word.
    ///     </para>
    /// </remarks>
    public UiTone? Tone { get; set; }

    public RouteUrl? Href { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var tone = Tone switch
        {
            UiTone.Error => "text-error",
            UiTone.Warning => "text-warning",
            _ => null,
        };

        Component body = Div.Class("flex items-start justify-between gap-3")[
            Div.Class("min-w-0")[
                Div.Class($"truncate {UiStyles.Label}")[Label],
                Div.Class(tone is null ? UiStyles.Value : $"{UiStyles.Value} {tone}")[Value],
                Caption is null ? null : Div.Class(UiStyles.Caption)[Caption]
            ],
            UiIcon.Name(Icon ?? UiIconName.Overview)
                .Class($"size-5 shrink-0 {tone ?? "opacity-60"}")
        ];

        // A tile that leads somewhere is a link, so it is reachable by keyboard and says where it goes —
        // rather than a div with a click handler, which is neither.
        return Href is { } href
            ? NavLink.Href(href).Class($"{UiStyles.Card} block no-underline transition-colors hover:bg-base-200")[body]
            : Div.Class(UiStyles.Card)[body];
    }
}
