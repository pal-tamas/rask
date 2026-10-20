namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ProgressControlledExample : Component
{
    private int _progress = 62;

    protected override Component? Render() =>
        Div.Class("max-w-64 space-y-6")[
            Div[
                Div.Id("progress-storage").Class("mb-3 flex text-sm font-medium")[
                    "Storage",
                    Span.Class("ms-auto tabular-nums")[$"{_progress}%"]
                ],
                Ui.Progress.Data("testid", "ui-progress-storage").Value(_progress).Aria("labelledby", "progress-storage")
            ],
            Div.Class("flex gap-2")[
                Ui.Button.Key("less").Sm.OnClick(() => { _progress = Math.Max(0, _progress - 10); })["−10"],
                Ui.Button.Key("more").Sm.OnClick(() => { _progress = Math.Min(100, _progress + 10); })["+10"]
            ]
        ];
}
