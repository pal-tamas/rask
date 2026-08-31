using Rask.Core.Live;

namespace Rask.Example.Shared.Features;

// Media & embedded content: img, picture/source, audio, video/track, canvas, iframe (srcdoc — no
// network), embed, object, and an image map (map/area). Self-contained assets so it works offline.
public sealed partial class ElementsMediaDemo : Component
{
    private static string Asset(string name) => LiveOptions.PathBase + "/img/" + name;

    protected override Component? Render() => Div.Class("flex flex-col gap-3")[
        Div.Class("flex gap-3 items-start flex-wrap items-center")[
            Figure.Class("m-0")[
                // <picture> picks a <source> by media query, else falls back to <img>.
                Picture[
                    Source.Srcset(Asset("rask-placeholder.svg")).Media("(min-width: 1px)"),
                    Img
                        .Src(Asset("rask-placeholder.svg"))
                        .Alt("Rask logo")
                        .Width(96)
                        .Height(96)
                        .Class("rounded border")
                ],
                Figcaption.Class(Ui.FigureCaption)["picture / source / img"]
            ],
            Figure.Class("m-0")[
                // <canvas> is a JS drawing surface; shown here as the (empty) element.
                Canvas.Width(96).Height(96).Class("border rounded"),
                Figcaption.Class(Ui.FigureCaption)["canvas"]
            ],
            Figure.Class("m-0")[
                Iframe
                    .Srcdoc("<p style='font:13px sans-serif;margin:8px'>An inline iframe document.</p>")
                    .Width(180)
                    .Height(96)
                    .Class("border rounded"),
                Figcaption.Class(Ui.FigureCaption)["iframe (srcdoc)"]
            ]
        ],
        Div.Class("flex gap-3 items-start flex-wrap items-center")[
            Figure.Class("m-0")[
                Embed.Src(Asset("rask-placeholder.svg")).Type("image/svg+xml").Width(96).Height(96),
                Figcaption.Class(Ui.FigureCaption)["embed"]
            ],
            Figure.Class("m-0")[
                HtmlObject.DataUrl(Asset("rask-placeholder.svg")).Type("image/svg+xml").Width(96).Height(96),
                Figcaption.Class(Ui.FigureCaption)["object"]
            ],
            Figure.Class("m-0")[
                // <img usemap> + <map>/<area>: a clickable region.
                Img
                    .Src(Asset("rask-placeholder.svg"))
                    .Alt("Map")
                    .Width(96)
                    .Height(96)
                    .UseMap("#regions")
                    .Class("border rounded"),
                Map.Name("regions")[Area.Shape("rect").Coords("0,0,48,96").Href("#").Alt("left half")],
                Figcaption.Class(Ui.FigureCaption)["img usemap / map / area"]
            ]
        ],
        Div.Class("grid grid-cols-12 gap-4")[
            Div.Class("md:col-span-6")[
                P.Class("text-sm mb-1 text-slate-500 dark:text-slate-400")["audio (controls)"],
                Audio.Controls(true).Preload("none").Class("w-full")
            ],
            Div.Class("md:col-span-6")[
                P.Class("text-sm mb-1 text-slate-500 dark:text-slate-400")["video (poster + track)"],
                Video
                    .Controls(true)
                    .Width(240)
                    .Poster(Asset("rask-placeholder.svg"))
                    .Preload("none")
                    .Class("border rounded")[
                    Track.Kind("captions").Src(Asset("captions.vtt")).Srclang("en").Label("English").Default(true)
                ]
            ]
        ]
    ];
}
