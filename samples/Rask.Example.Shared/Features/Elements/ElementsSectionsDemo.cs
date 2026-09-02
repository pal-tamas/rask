namespace Rask.Example.Shared.Features;

// Sectioning + headings: the six headings, hgroup, and the semantic landmarks article/section/nav/
// aside/header/footer/main/address/search.
public sealed partial class ElementsSectionsDemo : Component
{
    protected override Component? Render() => Article.Class("border rounded p-3")[
        Header[
            Hgroup[
                H1.Class("text-xl font-semibold mb-1")["Article title"],
                P.Class("text-slate-500 dark:text-slate-400 mb-0")["A subtitle grouped with the heading"]
            ],
            Nav.Class("text-sm")[
                A.Href("#a").Class("me-2")["Intro"], A.Href("#b")["Details"]
            ]
        ],
        Search.Class("my-2")[
            Input.Value<string>(null).Type(InputType.Search).Class(Tw.Input).Placeholder("Search…")
        ],
        Div.Class("grid grid-cols-12 gap-4")[
            Main.Class("col-span-8")[
                Section.Id("a")[H2.Class("text-base font-semibold")["Section heading levels"],
                    P.Class("mb-1")["Headings ", Code["H1"], "–", Code["H6"], ":"],
                    H3.Class("text-base font-semibold mb-0")["H3"], H4.Class("text-base font-semibold mb-0")["H4"],
                    H5.Class("text-base font-semibold mb-0")["H5"], H6.Class("text-base font-semibold mb-0")["H6"]
                ]
            ],
            Aside.Class("col-span-4 text-slate-500 dark:text-slate-400 text-sm")[
                "An ", Code["aside"], " — complementary content."
            ]
        ],
        Footer.Class("border-t pt-2 mt-2 text-sm text-slate-500 dark:text-slate-400")[
            "Footer · ", Address.Class("inline italic")["contact@example.com"]
        ]
    ];
}
