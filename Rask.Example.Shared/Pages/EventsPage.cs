using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using static Rask.Core.Tags;

namespace Rask.Example.Shared;

[Route("events"), ParentRoute(typeof(ShowcaseLayout))]
public sealed class EventsPage : Component
{
    private int _clicks;
    private string _typed = string.Empty;
    private string _pick = "rask";
    private string _submitted = "(none yet)";

    public override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Events",
                "Event handlers are plain delegates on the factory call site — OnClick, OnInput, OnChange, OnSubmit. Each handler triggers a re-render after it runs."),

            H2(Class: "h4 mt-4 mb-3", Children: ["Click"]),
            Components.CodeSample(
                Source: """
                    Button(OnClick: () => _clicks++, Children: [$"Clicks: {_clicks}"])
                    """,
                Result: Button(
                    Class: "btn btn-primary",
                    OnClick: () => _clicks++,
                    Children: [I(Class: "bi bi-hand-index me-2"), $"Clicks: {_clicks}"])),

            H2(Class: "h4 mt-5 mb-3", Children: ["Input — onInput"]),
            Components.CodeSample(
                Source: """
                    Input(Type: "text",
                          Placeholder: "Type something",
                          OnInput: v => _typed = v)
                    P(Children: [$"You typed: {_typed}"])
                    """,
                Result: Fragment(
                    Input(
                        Type: "text",
                        Class: "form-control mb-2",
                        Placeholder: "Type something",
                        Value: _typed,
                        OnInput: v => _typed = v),
                    P(Class: "small mb-0", Children:
                    [
                        "You typed: ",
                        Code(Children: [string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""])
                    ]))),

            H2(Class: "h4 mt-5 mb-3", Children: ["Select — onChange"]),
            Components.CodeSample(
                Source: """
                    Select(OnChange: v => _pick = v, Children: [
                        Option(Value: "rask",   Children: ["Rask"]),
                        Option(Value: "blazor", Children: ["Blazor"]),
                        Option(Value: "htmx",   Children: ["htmx"])
                    ])
                    """,
                Result: Fragment(
                    Select(
                        Class: "form-select mb-2",
                        OnChange: v => _pick = v,
                        Children:
                        [
                            Option(Value: "rask",   Selected: _pick == "rask",   Children: ["Rask"]),
                            Option(Value: "blazor", Selected: _pick == "blazor", Children: ["Blazor"]),
                            Option(Value: "htmx",   Selected: _pick == "htmx",   Children: ["htmx"])
                        ]),
                    P(Class: "small mb-0", Children: ["Picked: ", Strong(Children: [_pick])]))),

            H2(Class: "h4 mt-5 mb-3", Children: ["Form — onSubmit"]),
            Components.CodeSample(
                Source: """
                    Form(OnSubmit: fd => _submitted = fd.Get("name"),
                         Children: [
                             Input(Type: "text", Name: "name", Placeholder: "Your name"),
                             Button(Type: "submit", Children: ["Send"])
                         ])
                    """,
                Notes: "OnSubmit receives a FormData object collected from all named form fields.",
                Result: Fragment(
                    Form(OnSubmit: OnSubmit, Class: "mb-2", Children:
                    [
                        Div(Class: "input-group", Children:
                        [
                            Input(
                                Type: "text",
                                Name: "name",
                                Class: "form-control",
                                Placeholder: "Your name"),
                            Button(
                                Type: "submit",
                                Class: "btn btn-primary",
                                Children: [I(Class: "bi bi-send me-1"), "Send"])
                        ])
                    ]),
                    P(Class: "small mb-0", Children: ["Last submitted: ", Strong(Children: [_submitted])])))
        );

    private void OnSubmit(FormData fd)
    {
        var name = fd.Get("name");
        _submitted = string.IsNullOrWhiteSpace(name) ? "(blank)" : name;
    }
}
