namespace Rask.Example.Shared;

public sealed class LifecycleProbe : Component
{
    private readonly List<string> _log = new();
    private int _renderCount;

    protected override void OnInitialized() => _log.Add("OnInitialized");

    protected override async Task OnInitializedAsync()
    {
        _log.Add("OnInitializedAsync (start)");
        await Task.Delay(450);
        _log.Add("OnInitializedAsync (after 450ms await)");
    }

    protected override void OnParametersSet() => _log.Add($"OnParametersSet (render #{_renderCount + 1})");

    protected override Task OnParametersSetAsync()
    {
        _log.Add("OnParametersSetAsync");
        return Task.CompletedTask;
    }

    protected override void OnAfterRender(bool firstRender) =>
        _log.Add($"OnAfterRender(firstRender: {firstRender})");

    public override Component Render()
    {
        _renderCount++;
        return Fragment(
            Div(Class: "d-flex align-items-center gap-3 mb-3", Children:
            [
                Span(Class: "badge text-bg-primary fs-6", Children: [$"Render #{_renderCount}"]),
                Button(
                    Class: "btn btn-primary btn-sm",
                    OnClick: () => StateHasChanged(),
                    Children: [I(Class: "bi bi-arrow-clockwise me-1"), "Trigger re-render"])
            ]),
            H3(Class: "h6 text-secondary text-uppercase small", Children: ["Hook log"]),
            Ol(Class: "list-group list-group-numbered list-group-flush", Children:
                _log.Select(l => (Child)Li(Class: "list-group-item ps-2 small",
                    Children: [Code(Class: "small", Children: [l])])).ToArray())
        );
    }
}
