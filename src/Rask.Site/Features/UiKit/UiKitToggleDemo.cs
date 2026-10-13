using System.Globalization;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux UI's toggle page, example for example, drawn with <c>Ui.Toggle</c>.
/// </summary>
/// <remarks>
///     Only "Saving changes" is bound, and saves slowly enough to show the toggle waiting. The rest are
///     given nothing to bind to, and each keeps its own state as Flux's does.
/// </remarks>
public sealed partial class UiKitToggleDemo : Component
{
    private const string Lucide =
        " xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\""
        + " stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">";

    private const string Shrink =
        "<svg class=\"size-5 hidden group-data-checked:block text-fx-accent-content\"" + Lucide
        + "<path d=\"m15 15 6 6m-6-6v4.8m0-4.8h4.8\"/><path d=\"M9 19.8V15m0 0H4.2M9 15l-6 6\"/>"
        + "<path d=\"M15 4.2V9m0 0h4.8M15 9l6-6\"/><path d=\"M9 4.2V9m0 0H4.2M9 9 3 3\"/></svg>";

    private const string Expand =
        "<svg class=\"size-5 group-data-checked:hidden text-zinc-500/85 dark:text-zinc-300/80\"" + Lucide
        + "<path d=\"m15 15 6 6\"/><path d=\"m15 9 6-6\"/><path d=\"M21 16v5h-5\"/><path d=\"M21 8V3h-5\"/>"
        + "<path d=\"M3 16v5h5\"/><path d=\"m3 21 6-6\"/><path d=\"M3 8V3h5\"/><path d=\"M9 9 3 3\"/></svg>";

    private readonly Project _project = new();
    private int _saves;
    private bool _saved;
    private bool _muted;

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data(Testid("ui-toggle")).Class("space-y-6")[
            Example("basic", "Toggle", Ui.Toggle.Icon(Ui.IconName.Bolt).Tooltip("Fast mode")),
            Example(
                "saving",
                "Saving changes",
                Ui.Toggle.Key("active").Bind(() => _project.Active).OnChange(Save).Icon(Ui.IconName.LightBulb).Tooltip("Active project"),
                Span.Key("saved").Data(Testid("ui-toggle-saves")).Class("text-sm text-ui-muted")[
                    _saved ? "Saved as active" : "Saved as inactive", " · ", _saves.ToString(CultureInfo.InvariantCulture), " saves"
                ]),
            Example(
                "sizes",
                "Sizes",
                Ui.Toggle.Key("base").Icon(Ui.IconName.Bolt).Tooltip("Fast mode"),
                Ui.Toggle.Key("sm").Icon(Ui.IconName.Bolt).Tooltip("Fast mode").Sm,
                Ui.Toggle.Key("xs").Icon(Ui.IconName.Bolt).Tooltip("Fast mode").Xs),
            Example(
                "variants",
                "Variants",
                Ui.Toggle.Key("outline").Icon(Ui.IconName.Bolt).Tooltip("Default"),
                Ui.Toggle.Key("filled").Icon(Ui.IconName.Bolt).Tooltip("Filled").Filled,
                Ui.Toggle.Key("ghost").Icon(Ui.IconName.Bolt).Tooltip("Ghost").Ghost,
                Ui.Toggle.Key("subtle").Icon(Ui.IconName.Bolt).Tooltip("Subtle").Subtle),
            Example(
                "color",
                "Color",
                Ui.Toggle.Key("blue").Icon(Ui.IconName.Bolt).Color(Ui.Color.Blue).Checked().Tooltip("Fast mode"),
                Ui.Toggle.Key("red").Icon(Ui.IconName.Heart).Color(Ui.Color.Red).Checked().Tooltip("Favorite"),
                Ui.Toggle.Key("amber").Icon(Ui.IconName.Bell).Color(Ui.Color.Amber).Checked().Tooltip("Notifications")),
            Example("label", "With label", Ui.Toggle.Icon(Ui.IconName.Bolt).Label("Fast mode")),
            Example(
                "stateful-icon",
                "Stateful icon",
                Ui.Toggle.Key("sound").OnIcon(Ui.IconName.SpeakerXMark).OffIcon(Ui.IconName.SpeakerWave).Tooltip("Mute")
                    .Checked(_muted).OnChange(muted => { _muted = muted; }),
                Span.Key("says").Data(Testid("ui-toggle-sound")).Class("text-sm text-ui-muted")[_muted ? "Muted" : "Playing"]),
            Example("custom-icons", "Custom icons", Ui.Toggle.Icon(Raw.Value(Shrink + Expand)).Tooltip("Toggle fullscreen")),
            Example("stateful-label", "Stateful label", Ui.Toggle.OnLabel("Sound on").OffLabel("Sound off")),
            Example("disabled", "Disabled", Ui.Toggle.Icon(Ui.IconName.Bolt).Label("Fast mode").Disabled())
        ];

    // Flux's `wire:change="save"`: the member is written by the time this runs, and the toggle waits on it.
    private async Task Save(bool active)
    {
        await Task.Delay(600);
        _saved = active;
        _saves++;
    }

    private static Component Example(string name, string title, params Component?[] toggles) =>
        Div.Key(name).Data(Testid("ui-toggle-" + name)).Class("space-y-2")[
            H3.Class("text-sm font-medium text-ui-muted")[title],
            Div.Class("flex flex-wrap items-center gap-3")[toggles]
        ];

    private static AttrBag Testid(string value) => new("testid", value);

    // Written through Bind, which the compiler cannot see as a use of the setter from a private type.
    internal sealed class Project
    {
        public bool Active { get; set; }
    }
}
