using System.Globalization;

namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ToggleSavingExample : Component
{
    private readonly Project _project = new();
    private bool _saved;
    private int _saves;

    protected override Component? Render() =>
        Div.Class("flex flex-wrap items-center gap-3")[
            Ui.Toggle.Bind(() => _project.Active).OnChange(Save).Icon(Ui.IconName.LightBulb).Tooltip("Active project"),
            Ui.Text.Data("testid", "ui-toggle-saves")[
                (_saved ? "Saved as active" : "Saved as inactive") + " · " + _saves.ToString(CultureInfo.InvariantCulture) + " saves"
            ]
        ];

    // Flux's wire:change="save": the member is written by the time this runs, and the toggle waits on it.
    private async Task Save(bool active)
    {
        await Task.Delay(600);
        _saved = active;
        _saves++;
    }

    public sealed class Project
    {
        public bool Active { get; set; }
    }
}
