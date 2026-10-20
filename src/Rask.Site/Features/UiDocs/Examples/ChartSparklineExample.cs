namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ChartSparklineExample : Component
{
    protected override Component? Render() =>
        Div.Class("flex items-start gap-8")[
            Ui.Chart.Key("sparkline").Value([15, 18, 16, 19, 22, 25, 28, 25, 29, 28, 32, 35]).Class("aspect-3/1 w-20")[
                Ui.ChartSvg.Gutter("0")[Ui.ChartLine.Class("text-green-500 dark:text-green-400")]
            ],
            Ui.Card.Key("stat").Class("min-w-48 overflow-hidden")[
                Ui.Text["Revenue"],
                Ui.Heading.Xl.Class("mt-2 tabular-nums")["$12,345"],
                Ui.Chart.Value([10, 12, 11, 13, 15, 14, 16, 18, 17, 19, 21, 20]).Class("-mx-6 -mb-6 h-12")[
                    Ui.ChartSvg.Gutter("4 0 0 0")[
                        Ui.ChartLine.Class("text-sky-200 dark:text-sky-400"),
                        Ui.ChartArea.Class("text-sky-100 dark:text-sky-400/30")
                    ]
                ]
            ]
        ];
}
