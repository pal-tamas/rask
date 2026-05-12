using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("components")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class ComponentsPage : Component
{
    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "User components",
                "Subclass Component, override Render. The Rask source generator emits a Namespace.Components.TypeName(...) factory for every concrete user component, with parameters derived from your public settable properties."),
            H2(Class: "h4 mt-4 mb-3", Children: ["A component and its generated factory"]),
            Demos.Components.CodeSample(
                """
                public sealed class Greeting : Component
                {
                    public required string Name { get; set; }
                    public string? Title { get; set; }

                    public override Component Render() =>
                        P(Children: [
                            Title is null ? "" : $"{Title} ",
                            "Hello, ", Strong(Children: [Name]), "!"
                        ]);
                }

                // call site (generated factory):
                Components.Greeting(Name: "Ada", Title: "Dr.")
                """,
                Notes:
                "Non-nullable property without an initializer → required factory parameter. Nullable property → optional with default null. Property with an initializer → excluded from the factory.",
                Result: Components.Greeting("Ada", "Dr.")),
            H2(Class: "h4 mt-5 mb-3", Children: ["DI via constructor"]),
            Demos.Components.CodeSample(
                """
                // Inject services like HttpClient/Navigator/RouteState through the
                // primary constructor — never as a public settable property:
                public sealed class WeatherCard(HttpClient http) : Component
                {
                    public required string City { get; set; }
                    // ... uses `http` to fetch
                }

                // call site is unchanged — ActivatorUtilities resolves `http`:
                Components.WeatherCard(City: "Helsinki")
                """,
                Notes:
                "ActivatorUtilities.CreateInstance constructs the component each time; constructor parameters resolve from DI, properties are then re-applied so cached private state survives across renders while props stay fresh."),
            H2(Class: "h4 mt-5 mb-3", Children: ["[SkipFactory] hides a property"]),
            Demos.Components.CodeSample(
                """
                public sealed class Counter : Component
                {
                    [SkipFactory] public int Initial { get; set; }
                    private int _count;
                    protected override void OnMount() => _count = Initial;
                    // ... renders _count
                }

                // Initial is excluded from the factory; assign it directly:
                var c = new Counter { Initial = 7 };
                """,
                Notes:
                "[SkipFactory] keeps a property settable in code while removing it from the generated factory signature."),
            H2(Class: "h4 mt-5 mb-3", Children: ["Diagnostics"]),
            Div(Class: "list-group mb-3", Children:
            [
                Div(Class: "list-group-item d-flex align-items-start", Children:
                [
                    Span(Class: "badge text-bg-secondary me-3", Children: ["RASK001"]),
                    Div(Children:
                    [
                        Strong(Children: ["Hidden suggestion."]),
                        " A property is treated as a required factory parameter — consider also marking it ",
                        Code(Children: ["required"]),
                        " for language-level enforcement."
                    ])
                ]),
                Div(Class: "list-group-item d-flex align-items-start", Children:
                [
                    Span(Class: "badge text-bg-warning me-3", Children: ["RASK002"]),
                    Div(Children:
                    [
                        Strong(Children: ["Warning."]),
                        " ", Code(Children: ["required"]),
                        " on a property combined with DI-injected constructor parameters: ",
                        Code(Children: ["ActivatorUtilities"]),
                        " cannot satisfy ", Code(Children: ["required"]), " members. Drop one or the other."
                    ])
                ])
            ])
        );
}

public sealed class Greeting : Component
{
    public required string Name { get; set; }
    public string? Title { get; set; }

    protected override Component Render() =>
        P(Class: "mb-0", Children:
        [
            Title is null ? "" : $"{Title} ",
            "Hello, ", Strong(Children: [Name]), "!"
        ]);
}
