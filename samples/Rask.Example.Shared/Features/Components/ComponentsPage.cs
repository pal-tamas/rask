using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

[Route("components")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ComponentsPage : Component
{
    protected override RenderResult Head => Title()["User components — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "User components",
            "Subclass Component, override Render. The Rask source generator emits a Namespace.Generated.TypeName(...) factory for every concrete user component, with parameters derived from your public settable properties."),
        H2(Class: "h4 mt-4 mb-3")["A component and its generated factory"],
        CodeSample(
            ["ComponentsGreetingDemo.cs"],
            Notes:
            "Non-nullable property without an initializer → required factory parameter. Nullable property → optional with default null. Property with an initializer → excluded from the factory.",
            Result: ComponentsGreetingDemo()),
        H2(Class: "h4 mt-5 mb-3")["DI via constructor"],
        CodeSample(
            ["ComponentsDiDemo.cs"],
            Notes:
            "ActivatorUtilities.CreateInstance constructs the component each time; constructor parameters resolve from DI, properties are then re-applied so cached private state survives across renders while props stay fresh."),
        H2(Class: "h4 mt-5 mb-3")["[SkipFactory] hides a property"],
        CodeSample(
            ["ComponentsSkipFactoryDemo.cs"],
            Notes:
            "[SkipFactory] keeps a property settable in code while removing it from the generated factory signature. The counter below started at 7 — click it and the state persists across re-renders.",
            Result: ComponentsSkipFactoryDemo()),
        H2(Class: "h4 mt-5 mb-3")["Diagnostics"],
        Div(Class: "list-group mb-3")[
            Div(Class: "list-group-item d-flex align-items-start")[
                Span(Class: "badge text-bg-secondary me-3")["RASK001"],
                Div()[
                    Strong()["Hidden suggestion."],
                    " A property is treated as a required factory parameter — consider also marking it ",
                    Code()["required"],
                    " for language-level enforcement."
                ]
            ],
            Div(Class: "list-group-item d-flex align-items-start")[
                Span(Class: "badge text-bg-warning me-3")["RASK002"],
                Div()[
                    Strong()["Warning."],
                    " ", Code()["required"],
                    " on a property combined with DI-injected constructor parameters: ",
                    Code()["ActivatorUtilities"],
                    " cannot satisfy ", Code()["required"], " members. Drop one or the other."
                ]
            ]
        ]
    ];
}
