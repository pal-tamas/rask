using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace Rask.Example.Shared;

[Route("tags"), ParentRoute(typeof(ShowcaseLayout))]
public sealed class TagsPage : Component
{
    public override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Tag factories",
                "Every standard HTML element has a static factory method on Rask.Core.Tags. " +
                "Tag-specific attributes come first; the universal Id/Class/Style/Data trail at the end."),

            H2(Class: "h4 mt-5 mb-3", Children: [I(Class: "bi bi-fonts text-accent me-2"), "Text & semantic"]),
            Components.CodeSample(
                Source: """
                    Article(Children: [
                        H1(Children: ["Tags are just methods."]),
                        P(Children: [
                            "You can ", Strong(Children: ["emphasize"]), " or ",
                            Em(Children: ["italicize"]), " by composing them."
                        ]),
                        Blockquote(Children: ["A small DSL, an honest day's HTML."])
                    ])
                    """,
                Result: Article(Children:
                [
                    H1(Class: "h4", Children: ["Tags are just methods."]),
                    P(Children: ["You can ", Strong(Children: ["emphasize"]), " or ", Em(Children: ["italicize"]), " by composing them."]),
                    Blockquote(Class: "blockquote fs-6", Children: ["A small DSL, an honest day's HTML."])
                ])),

            H2(Class: "h4 mt-5 mb-3", Children: [I(Class: "bi bi-input-cursor-text text-accent me-2"), "Forms"]),
            Components.CodeSample(
                Source: """
                    Form(Children: [
                        Label(For: "n", Children: ["Name"]),
                        Input(Type: "text", Id: "n", Placeholder: "Jane Doe"),
                        Button(Type: "submit", Children: ["Submit"])
                    ])
                    """,
                Result: Form(Children:
                [
                    Div(Class: "mb-2", Children:
                    [
                        Label(For: "n", Class: "form-label small mb-1", Children: ["Name"]),
                        Input(Type: "text", Id: "n", Class: "form-control form-control-sm", Placeholder: "Jane Doe")
                    ]),
                    Button(Type: "submit", Class: "btn btn-primary btn-sm", Children: ["Submit"])
                ])),

            H2(Class: "h4 mt-5 mb-3", Children: [I(Class: "bi bi-table text-accent me-2"), "Tables"]),
            Components.CodeSample(
                Source: """
                    Table(Children: [
                        Thead(Children: [Tr(Children: [
                            Th(Children: ["#"]),
                            Th(Children: ["Tag"])
                        ])]),
                        Tbody(Children: [
                            Tr(Children: [Td(Children: ["1"]), Td(Children: ["Div"])]),
                            Tr(Children: [Td(Children: ["2"]), Td(Children: ["Span"])])
                        ])
                    ])
                    """,
                Result: Table(Class: "table table-sm mb-0", Children:
                [
                    Thead(Children: [Tr(Children: [Th(Children: ["#"]), Th(Children: ["Tag"])])]),
                    Tbody(Children:
                    [
                        Tr(Children: [Td(Children: ["1"]), Td(Children: [Code(Children: ["Div"])])]),
                        Tr(Children: [Td(Children: ["2"]), Td(Children: [Code(Children: ["Span"])])])
                    ])
                ])),

            H2(Class: "h4 mt-5 mb-3", Children: [I(Class: "bi bi-image text-accent me-2"), "Media"]),
            Components.CodeSample(
                Source: """
                    Img(Src: "https://placehold.co/120x60/0066B3/ffffff?text=Rask",
                        Alt: "Rask")
                    """,
                Result: Img(
                    Src: "https://placehold.co/120x60/0066B3/ffffff?text=Rask",
                    Alt: "Rask",
                    Class: "rounded shadow-sm")),

            H2(Class: "h4 mt-5 mb-3", Children: [I(Class: "bi bi-dash-circle text-accent me-2"), "Void elements"]),
            Components.CodeSample(
                Source: """
                    Fragment(
                        P(Children: ["Above the rule"]),
                        Hr(),
                        P(Children: ["Below the rule"])
                    )
                    """,
                Notes: "Void elements (Br, Hr, Img, Meta, Link, Input, …) have SelfClosing => true and never accept children.",
                Result: Fragment(
                    P(Class: "mb-2", Children: ["Above the rule"]),
                    Hr(),
                    P(Class: "mb-0", Children: ["Below the rule"])
                ))
        );
}
