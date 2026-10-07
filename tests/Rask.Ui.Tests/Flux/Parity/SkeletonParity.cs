using System.Globalization;
using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>Flux UI's <c>components/skeleton</c> page, example by example.</summary>
/// <remarks>
/// The classes Flux's examples put on a skeleton — <c>size-10 rounded-full</c>, <c>w-1/2</c> — are the call
/// site's, and a utility written here is in no stylesheet, so each is the same declaration as an inline style.
/// </remarks>
public sealed class SkeletonParity : FluxParity
{
    private const string Round = "border-radius:calc(infinity * 1px)";

    public override string Page => "skeleton";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Column(256,
            Ui.SkeletonGroup.Shimmer.Style("display:flex;align-items:center;gap:16px")[
                Ui.Skeleton.Style("width:40px;height:40px;" + Round),
                Div.Style("flex:1")[
                    Ui.SkeletonLine,
                    Ui.SkeletonLine.Style("width:50%")
                ]
            ]));

        yield return ("line-of-text", Column(384,
            Ui.SkeletonGroup.Shimmer[
                Ui.SkeletonLine.Style("margin-bottom:8px;width:25%"),
                Ui.SkeletonLine,
                Ui.SkeletonLine,
                Ui.SkeletonLine.Style("width:75%")
            ]));

        yield return ("animation", Column(384,
            Div.Style("display:flex;flex-direction:column;gap:24px")[
                Div[Heading("None"), Ui.Skeleton],
                Div[Heading("Shimmer"), Ui.Skeleton.Shimmer],
                Div[Heading("Pulse"), Ui.Skeleton.Pulse]
            ]));

        // style="width: {{ rand(50, 100) }}%" there: the first line of each row is a different width on every
        // load of Flux's page, so those five lines are `data-parity-skip="width"` — everything but that is held.
        yield return ("examples", Rows(
            Ui.SkeletonGroup.Shimmer[
                Ui.Table[
                    Ui.TableColumns[
                        Ui.TableColumn["Customer"],
                        Ui.TableColumn["Date"],
                        Ui.TableColumn["Status"],
                        Ui.TableColumn["Amount"]
                    ],
                    Ui.TableRows[Enumerable.Range(1, 5).Select(Order)]
                ]
            ]));

        // class="dark:bg-zinc-800" on the card there: an app's own utility, stated here.
        yield return ("examples", Rows(
            Raw.Value("<style>.dark .parity-card{background-color:oklch(0.274 0.006 286.033)}</style>"),
            Ui.Card.Class("parity-card")[
                Div.Style("display:flex;flex-direction:column;gap:24px")[
                    Div.Style("display:flex;gap:48px")[
                        Div[
                            Ui.Text["Today"],
                            Ui.Heading.Xl.Style("margin-top:8px;font-variant-numeric:tabular-nums")["$---"],
                            Ui.Text.Style("margin-top:8px;font-variant-numeric:tabular-nums")["-:-- PM"]
                        ],
                        Div[
                            Ui.Text["Yesterday"],
                            Ui.Heading.Lg.Style("margin-top:8px;font-variant-numeric:tabular-nums")["$---"]
                        ]
                    ],
                    Ui.Skeleton.Shimmer.Style("aspect-ratio:4/1;width:100%;height:100%;border-radius:8px")
                ]
            ]));
    }

    private static Component Heading(string text) => Ui.Heading.Style("margin-bottom:8px")[text];

    private static Component Order(int order) =>
        Ui.TableRow.Key(order)[
            Ui.TableCell[
                Div.Style("display:flex;align-items:center;gap:8px")[
                    Ui.Skeleton.Style("width:20px;height:20px;" + Round),
                    // On the cell's flex item, whose only child is the random line.
                    Div.Data("parity-skip", "width").Style("flex:1")[
                        Ui.SkeletonLine.Style(string.Create(CultureInfo.InvariantCulture, $"width:{50 + (order * 10)}%"))
                    ]
                ]
            ],
            Ui.TableCell[Ui.SkeletonLine],
            Ui.TableCell[Ui.SkeletonLine],
            Ui.TableCell[Ui.SkeletonLine]
        ];

    // The 512px flex column Flux's page lays its two larger examples out in.
    private static Component Rows(params Component[] items) =>
        Div.Style("display:flex;flex-direction:column;gap:24px;width:512px;margin:0 auto")[
            items
        ];

    private static Component Column(int width, params Component[] items) =>
        Div.Style(string.Create(CultureInfo.InvariantCulture, $"width:{width}px;margin:0 auto"))[
            items
        ];
}
