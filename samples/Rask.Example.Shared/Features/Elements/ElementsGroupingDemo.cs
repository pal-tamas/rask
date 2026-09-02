namespace Rask.Example.Shared.Features;

// Grouping content + lists: p, hr, pre, blockquote, div, ol/ul/li, dl/dt/dd, figure/figcaption.
public sealed partial class ElementsGroupingDemo : Component
{
    protected override Component? Render() => Div.Class("flex flex-col gap-3")[
        P["A paragraph of flow content, grouped in a ", Code["Div"], "."],
        Pre.Class("bg-slate-100 border rounded p-2 mb-0")["  preformatted\n  text  keeps   spacing"],
        Blockquote.Class($"{Tw.Blockquote} text-base border-l ps-3").Cite("https://example.com")[
            "A small DSL, an honest day's HTML."],
        Hr,
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("col-span-12")[
                P.Class("font-semibold mb-1")["Ordered (start=2, reversed)"],
                Ol.Class("mb-0").Start(2).Reversed(true)[
                    Li.Value(2)["Second"], Li["First-ish"], Li["Zeroth-ish"]
                ]
            ],
            Div.Class("col-span-12")[
                P.Class("font-semibold mb-1")["Unordered"],
                Ul.Class("mb-0")[Li["Alpha"], Li["Beta"], Li["Gamma"]]
            ],
            Div.Class("col-span-12")[
                P.Class("font-semibold mb-1")["Description"],
                Dl.Class("mb-0")[
                    Dt["Rask"], Dd.Class("mb-1")["A C# UI framework."],
                    Dt["Tag"], Dd.Class("mb-0")["A generated factory method."]
                ]
            ]
        ],
        Figure.Class("mb-0")[
            Pre.Class("bg-slate-900 text-slate-100 rounded p-2")["Div()[Span()[\"hi\"]]"],
            Figcaption.Class(Tw.FigureCaption)["Figure: a tiny component tree."]
        ]
    ];
}
