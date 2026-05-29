using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("tags")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class TagsPage : Component
{
    protected override RenderResult Head => Title()["Tag factories — Rask"];

    protected override RenderResult Render() =>
        [
            PageHeader.Render(
                "Tag factories",
                "Every standard HTML element has a generator-emitted factory in Rask.Core.Components.Generated. " +
                "Tag-specific attributes come first; the universal Id/Class/Style/Data trail at the end."),
            H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-fonts text-accent me-2"), "Text & semantic"],
            CodeSample(
                """
                Article()[
                    H1()["Tags are just methods."],
                    P()[
                        "You can ", Strong()["emphasize"], " or ",
                        Em()["italicize"], " by composing them."
                    ],
                    Blockquote()["A small DSL, an honest day's HTML."]
                ]
                """,
                Result: Article()[
                    H1(Class: "h4")["Tags are just methods."],
                    P()[
                        "You can ", Strong()["emphasize"], " or ", Em()["italicize"],
                        " by composing them."
                    ],
                    Blockquote(Class: "blockquote fs-6")["A small DSL, an honest day's HTML."]
                ]),
            H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-input-cursor-text text-accent me-2"), "Forms"],
            CodeSample(
                """
                Form()[
                    Label(For: "n")["Name"],
                    Input(Type: "text", Id: "n", Placeholder: "Jane Doe"),
                    Button(Type: "submit")["Submit"]
                ]
                """,
                Result: Form()[
                    Div(Class: "mb-2")[
                        Label("n", Class: "form-label small mb-1")["Name"],
                        Input("text", Id: "n", Class: "form-control form-control-sm", Placeholder: "Jane Doe")
                    ],
                    Button("submit", Class: "btn btn-primary btn-sm")["Submit"]
                ]),
            H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-table text-accent me-2"), "Tables"],
            CodeSample(
                """
                Table()[
                    Thead()[Tr()[
                        Th()["#"],
                        Th()["Tag"]
                    ]],
                    Tbody()[
                        Tr()[Td()["1"], Td()["Div"]],
                        Tr()[Td()["2"], Td()["Span"]]
                    ]
                ]
                """,
                Result: Table(Class: "table table-sm mb-0")[
                    Thead()[Tr()[Th()["#"], Th()["Tag"]]],
                    Tbody()[
                        Tr()[Td()["1"], Td()[Code()["Div"]]],
                        Tr()[Td()["2"], Td()[Code()["Span"]]]
                    ]
                ]),
            H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-image text-accent me-2"), "Media"],
            CodeSample(
                """
                Img(Src: LiveOptions.PathBase + "/img/rask-placeholder.svg",
                    Alt: "Rask")
                """,
                Result: Img(
                    LiveOptions.PathBase + "/img/rask-placeholder.svg",
                    "Rask",
                    Class: "rounded shadow-sm")),
            H2(Class: "h4 mt-5 mb-3")[I(Class: "bi bi-dash-circle text-accent me-2"), "Void elements"],
            CodeSample(
                """
                Fragment(
                    P()["Above the rule"],
                    Hr(),
                    P()["Below the rule"]
                )
                """,
                Notes:
                "Void elements (Br, Hr, Img, Meta, Link, Input, …) have SelfClosing => true and never accept children.",
                Result: Fragment()[
                    P(Class: "mb-2")["Above the rule"],
                    Hr(),
                    P(Class: "mb-0")["Below the rule"]
                ])
        ];
}
