using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/toggle</c>, example for example, as the page loads: every toggle at rest.
/// </summary>
/// <remarks>
///     <para>
///     Most toggles on that page carry a tooltip, which is the kit's own <c>Ui.Tooltip</c> around the button, as
///     Flux wraps it. The rendered "Color" example shows five hues where its markdown lists three.
///     </para>
///     <para>
///     "Custom icons" hands the <c>icon</c> slot two Lucide drawings with classes of the app's own, which
///     are written here as an app would write them.
///     </para>
/// </remarks>
public sealed partial class ToggleParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes "Custom icons" hands its two drawings.
    private const string AppUtilities =
        "<style>.parity-icon{width:20px;height:20px;flex-shrink:0}"
        + ".parity-on{display:none;color:var(--color-fx-accent-content)}"
        + "[data-checked] .parity-on{display:block}[data-checked] .parity-off{display:none}"
        + ".parity-off{color:color-mix(in oklab,oklch(55.2% .016 285.938) 85%,transparent)}"
        + "@media (prefers-color-scheme:dark){.parity-off{color:color-mix(in oklab,oklch(87.1% .006 286.286) 80%,transparent)}}</style>";

    private const string Lucide =
        " xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\""
        + " stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" data-slot=\"icon\">";

    private const string Shrink =
        "<svg data-ui-icon class=\"parity-icon parity-on\"" + Lucide
        + "<path d=\"m15 15 6 6m-6-6v4.8m0-4.8h4.8\"/><path d=\"M9 19.8V15m0 0H4.2M9 15l-6 6\"/>"
        + "<path d=\"M15 4.2V9m0 0h4.8M15 9l6-6\"/><path d=\"M9 4.2V9m0 0H4.2M9 9 3 3\"/></svg>";

    private const string Expand =
        "<svg data-ui-icon class=\"parity-icon parity-off\"" + Lucide
        + "<path d=\"m15 15 6 6\"/><path d=\"m15 9 6-6\"/><path d=\"M21 16v5h-5\"/><path d=\"M21 8V3h-5\"/>"
        + "<path d=\"M3 16v5h5\"/><path d=\"m3 21 6-6\"/><path d=\"M3 8V3h5\"/><path d=\"M9 9 3 3\"/></svg>";

    public override string Page => "toggle";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Alone(Ui.Toggle.Icon(Ui.IconName.Bolt).Tooltip("Fast mode")));

        yield return ("saving-changes", Alone(Ui.Toggle.Icon(Ui.IconName.LightBulb).Tooltip("Active project")));

        yield return ("sizes", Centred(
            Each(Ui.Toggle.Icon(Ui.IconName.Bolt).Tooltip("Fast mode")),
            Each(Ui.Toggle.Icon(Ui.IconName.Bolt).Sm.Tooltip("Fast mode")),
            Each(Ui.Toggle.Icon(Ui.IconName.Bolt).Xs.Tooltip("Fast mode"))));

        yield return ("variants", Centred(
            Each(Ui.Toggle.Icon(Ui.IconName.Bolt).Tooltip("Default")),
            Each(Ui.Toggle.Icon(Ui.IconName.Bolt).Filled.Tooltip("Filled")),
            Each(Ui.Toggle.Icon(Ui.IconName.Bolt).Ghost.Tooltip("Ghost")),
            Each(Ui.Toggle.Icon(Ui.IconName.Bolt).Subtle.Tooltip("Subtle"))));

        yield return ("color", Centred(
            Each(Ui.Toggle.Icon(Ui.IconName.Bolt).Color(Ui.Color.Blue).Checked().Tooltip("Fast mode")),
            Each(Ui.Toggle.Icon(Ui.IconName.Heart).Color(Ui.Color.Red).Checked().Tooltip("Favorite")),
            Each(Ui.Toggle.Icon(Ui.IconName.Bell).Color(Ui.Color.Amber).Checked().Tooltip("Notifications")),
            Each(Ui.Toggle.Icon(Ui.IconName.CheckCircle).Color(Ui.Color.Green).Checked().Tooltip("Approved")),
            Each(Ui.Toggle.Icon(Ui.IconName.Sparkles).Color(Ui.Color.Violet).Checked().Tooltip("Enhance"))));

        yield return ("with-label", Alone(Ui.Toggle.Icon(Ui.IconName.Bolt).Label("Fast mode")));

        yield return ("stateful-icon", Alone(
            Ui.Toggle.OnIcon(Ui.IconName.SpeakerWave).OffIcon(Ui.IconName.SpeakerXMark).Tooltip("Toggle sound")));

        yield return ("custom-icons", Alone(
            Raw.Value(AppUtilities),
            Ui.Toggle.Icon(Raw.Value(Shrink + Expand)).Tooltip("Toggle fullscreen")));

        yield return ("stateful-label", Alone(Ui.Toggle.OnLabel("Sound on").OffLabel("Sound off")));
    }

    private static Component Centred(params Component[] items) =>
        Div.Style("width:606px;display:flex;justify-content:center")[
            Div.Style("display:flex;gap:12px;align-items:center")[items]
        ];

    // One toggle stands in a plain box, where it keeps the display it was given; several share a flex row.
    private static Component Alone(params Component[] items) =>
        Div.Style("width:606px;display:flex;justify-content:center")[Div[items]];

    // The docs page keeps each of several toggles in a box of no size of its own.
    private static Component Each(Component toggle) => Div.Style("display:contents")[toggle];
}
