namespace Rask.Example.Shared.Features;

// Sectioning + headings: the six headings, hgroup, and the semantic landmarks article/section/nav/
// aside/header/footer/main/address/search.
public sealed partial class ElementsSectionsDemo : Component
{
    protected override Component? Render() => Article(Class: "border rounded p-3")[
        Header()[
            Hgroup()[
                H1(Class: "h4 mb-1")["Article title"],
                P(Class: "text-secondary mb-0")["A subtitle grouped with the heading"]
            ],
            Nav(Class: "small")[
                A(Href: "#a", Class: "me-2")["Intro"], A(Href: "#b")["Details"]
            ]
        ],
        Search(Class: "my-2")[
            Input<string>(InputType.Search, Class: "form-control form-control-sm", Placeholder: "Search…")
        ],
        BsRow()[
            Main(Class: "col-8")[
                Section(Id: "a")[H2(Class: "h6")["Section heading levels"],
                    P(Class: "mb-1")["Headings ", Code()["H1"], "–", Code()["H6"], ":"],
                    H3(Class: "h6 mb-0")["H3"], H4(Class: "h6 mb-0")["H4"],
                    H5(Class: "h6 mb-0")["H5"], H6(Class: "h6 mb-0")["H6"]
                ]
            ],
            Aside(Class: "col-4 text-secondary small")[
                "An ", Code()["aside"], " — complementary content."
            ]
        ],
        Footer(Class: "border-top pt-2 mt-2 small text-secondary")[
            "Footer · ", Address(Class: "d-inline fst-italic")["contact@example.com"]
        ]
    ];
}
