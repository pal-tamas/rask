namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToggleStatefulIconExample : Component
{
    private bool _sound;

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-3")[
            Ui.Toggle.OnIcon(Ui.IconName.SpeakerWave).OffIcon(Ui.IconName.SpeakerXMark).Tooltip("Toggle sound")
                .Checked(_sound).OnChange(on => { _sound = on; }),
            Ui.Text.Data("testid", "ui-toggle-sound")[_sound ? "Playing" : "Muted"]
        ];
}
