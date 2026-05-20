using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("events")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class EventsPage : Component
{
    private int _clicks;
    private string _pick = "rask";
    private string _submitted = "(none yet)";
    private string _typed = string.Empty;

    protected override Component? Head => Title()["Events — Rask"];

    protected override Component Render() =>
        Fragment()[
            PageHeader.Render(
                "Events",
                "Event handlers are plain delegates on the factory call site — OnClick, OnInput, OnChange, OnSubmit. Each handler triggers a re-render after it runs."),
            H2(Class: "h4 mt-4 mb-3")["Click"],
            CodeSample(
                """
                Button(OnClick: () => _clicks++)[$"Clicks: {_clicks}"]
                """,
                Result: Button(
                    Class: "btn btn-primary",
                    OnClick: () => _clicks++)[I(Class: "bi bi-hand-index me-2"), $"Clicks: {_clicks}"]),
            H2(Class: "h4 mt-5 mb-3")["Input — onInput"],
            CodeSample(
                """
                Input(Type: "text",
                      Placeholder: "Type something",
                      OnInput: v => _typed = v)
                P()[$"You typed: {_typed}"]
                """,
                Result: Fragment()[
                    Input(
                        "text",
                        Class: "form-control mb-2",
                        Placeholder: "Type something",
                        Value: _typed,
                        OnInput: v => _typed = v),
                    P(Class: "small mb-0")[
                        "You typed: ",
                        Code()[string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""]
                    ]]),
            H2(Class: "h4 mt-5 mb-3")["Select — onChange"],
            CodeSample(
                """
                Select(OnChange: v => _pick = v)[
                    Option(Value: "rask")["Rask"],
                    Option(Value: "blazor")["Blazor"],
                    Option(Value: "htmx")["htmx"]
                ]
                """,
                Result: Fragment()[
                    Select(
                        Class: "form-select mb-2",
                        OnChange: v => _pick = v)[
                        Option("rask", _pick == "rask")["Rask"],
                        Option("blazor", _pick == "blazor")["Blazor"],
                        Option("htmx", _pick == "htmx")["htmx"]
                    ],
                    P(Class: "small mb-0")["Picked: ", Strong()[_pick]]]),
            H2(Class: "h4 mt-5 mb-3")["Form — onSubmit"],
            CodeSample(
                """
                Form(OnSubmit: fd => _submitted = fd.Get("name"))[
                         Input(Type: "text", Name: "name", Placeholder: "Your name"),
                         Button(Type: "submit")["Send"]
                     ]
                """,
                Notes: "OnSubmit receives a FormData object collected from all named form fields.",
                Result: Fragment()[
                    Form(OnSubmit: OnSubmit, Class: "mb-2")[
                        Div(Class: "input-group")[
                            Input(
                                "text",
                                "name",
                                Class: "form-control",
                                Placeholder: "Your name"),
                            Button(
                                "submit",
                                Class: "btn btn-primary")[I(Class: "bi bi-send me-1"), "Send"]
                        ]
                    ],
                    P(Class: "small mb-0")["Last submitted: ", Strong()[_submitted]]])
        ];

    private void OnSubmit(FormData fd)
    {
        var name = fd.Get("name");
        _submitted = string.IsNullOrWhiteSpace(name) ? "(blank)" : name;
    }
}
