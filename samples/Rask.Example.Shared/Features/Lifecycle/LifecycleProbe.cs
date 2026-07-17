namespace Rask.Example.Shared.Features;

public sealed class LifecycleProbe : Component
{
    private readonly List<string> _log = new();
    private int _renderCount;

    protected override void OnMount() => _log.Add("OnMount");

    protected override async Task OnMountAsync()
    {
        _log.Add("OnMountAsync (start)");
        await Task.Delay(450);
        _log.Add("OnMountAsync (after 450ms await)");
    }

    protected override void OnPropsChanged() => _log.Add($"OnPropsChanged (render #{_renderCount + 1})");

    protected override Task OnPropsChangedAsync()
    {
        _log.Add("OnPropsChangedAsync");
        return Task.CompletedTask;
    }

    protected override void OnRendered(bool firstRender) =>
        _log.Add($"OnRendered(firstRender: {firstRender})");

    protected override Component? Render() =>
        [
            BsStack(Gap: 3, Align: BsAlign.Center, Class: Margin.Bottom(3))[
                BsBadge(Color: BsColor.Primary, Class: "fs-6")[$"Render #{++_renderCount}"],
                // The handler just records the click; Rask re-renders the component that owns the
                // callback (this probe — the lambda closes over its state) right after it runs, so the
                // badge repaints with no StateHasChanged (RASK026). Works the same through BsButton,
                // which forwards the callback down to the native <button>.
                BsButton(Color: BsColor.Primary, Size: BsSize.Sm, OnClick: () => _log.Add("Trigger re-render (button click)"))[BsIcon(Name: BsIconName.ArrowClockwise, Class: "me-1"), "Trigger re-render"]
            ],
            H3(Class: "h6 text-secondary text-uppercase small")["Hook log"],
            Ol(Class: "list-group list-group-numbered list-group-flush")[
                _log.Select((l, i) => Li(Key: i, Class: "list-group-item ps-2 small")[Code(Class: "small")[l]])
                    .ToArray()]
        ];
}
