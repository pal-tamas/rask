namespace Rask.Example.Shared;

// Each live demo is its own user component so the bound input's auto-registered
// handler owner resolves to *this* demo (the structural CurrentParent at handler
// registration). Without this wrapper the owner falls back to CodeSample, which
// re-renders only itself and never re-evaluates the page's state.

public sealed class BindingManualDemo : Component
{
    private string _typed = "";

    protected override Component Render() =>
        Fragment(
            Input(
                "text",
                Class: "form-control mb-2",
                Placeholder: "Type something",
                Value: _typed,
                OnInput: v => _typed = v),
            P(Class: "small mb-0", Children:
            [
                "Echo: ",
                Code(Children: [string.IsNullOrEmpty(_typed) ? "\"\"" : $"\"{_typed}\""])
            ]));
}

public sealed class BindingTypedDemo : Component
{
    private readonly Holder _model = new();

    protected override Component Render() =>
        Fragment(
            Input(
                Bind: () => _model.Name,
                Class: "form-control mb-2",
                Placeholder: "Your name"),
            P(Class: "small mb-0", Children:
            [
                "Hello, ",
                Strong(Children: [string.IsNullOrEmpty(_model.Name) ? "stranger" : _model.Name]),
                "!"
            ]));

    private sealed class Holder
    {
        public string Name { get; set; } = "";
    }
}

public sealed class BindingMultiDemo : Component
{
    private readonly Holder _model = new();

    protected override Component Render() =>
        Fragment(
            Div(Class: "mb-3 form-check", Children:
            [
                Input(
                    Bind: () => _model.Subscribe,
                    Id: "bind-subscribe",
                    Class: "form-check-input"),
                Label(For: "bind-subscribe", Class: "form-check-label ms-1",
                    Children: ["Subscribe to the newsletter"])
            ]),
            Div(Class: "mb-3", Children:
            [
                Label(For: "bind-age", Class: "form-label small",
                    Children: ["Age"]),
                Input(
                    Bind: () => _model.Age,
                    Id: "bind-age",
                    Class: "form-control",
                    Min: "0",
                    Max: "120")
            ]),
            Div(Class: "mb-3", Children:
            [
                Label(For: "bind-start", Class: "form-label small",
                    Children: ["Start date"]),
                Input(
                    Bind: () => _model.StartDate,
                    Id: "bind-start",
                    Class: "form-control")
            ]),
            Div(Class: "mb-3", Children:
            [
                Label(For: "bind-favorite", Class: "form-label small",
                    Children: ["Favourite colour"]),
                Select(
                    Bind: () => _model.Favorite,
                    Id: "bind-favorite",
                    Class: "form-select",
                    Children:
                    [
                        Option("Red", Children: ["Red"]),
                        Option("Green", Children: ["Green"]),
                        Option("Blue", Children: ["Blue"])
                    ])
            ]),
            Pre(Class: "small mb-0 p-3 bg-light border rounded", Children:
            [
                Code(Children:
                [
                    $"Subscribe = {(_model.Subscribe ? "true" : "false")}\n" +
                    $"Age       = {_model.Age}\n" +
                    $"StartDate = {_model.StartDate:yyyy-MM-dd}\n" +
                    $"Favorite  = {_model.Favorite}"
                ])
            ]));

    private sealed class Holder
    {
        public bool Subscribe { get; set; }
        public int Age { get; set; } = 30;
        public DateOnly StartDate { get; set; } = new(2026, 1, 1);
        public Color Favorite { get; set; } = Color.Blue;
    }

    public enum Color { Red, Green, Blue }
}
