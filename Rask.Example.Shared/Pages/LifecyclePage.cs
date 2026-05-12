using Rask.Core.Routing;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Layout;

namespace Rask.Example.Shared.Pages;

[Route("lifecycle")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class LifecyclePage : Component
{
    protected override Component Render() =>
        Fragment(
            PageHeader.Render(
                "Lifecycle hooks",
                "Every Component can override five virtual lifecycle methods. Async hooks install a synchronization context that triggers a re-render after each in-method await, plus one terminal render on completion."),
            H2(Class: "h4 mt-4 mb-3", Children: ["Live probe"]),
            P(Class: "text-secondary",
                Children:
                [
                    "The component below records every hook invocation into a list and re-renders so you can watch the order."
                ]),
            Div(Class: "card shadow-sm border-0 mb-4", Children:
            [
                Div(Class: "card-body", Children: [LifecycleProbe()])
            ]),
            H2(Class: "h4 mt-4 mb-3", Children: ["Source"]),
            CodeSample(
                """
                public sealed class LifecycleProbe : Component
                {
                    private readonly List<string> _log = new();
                    private int _renderCount;

                    protected override void OnMount() =>
                        _log.Add("OnMount");

                    protected override async Task OnMountAsync()
                    {
                        _log.Add("OnMountAsync (start)");
                        await Task.Delay(450);
                        _log.Add("OnMountAsync (after 450ms await)");
                    }

                    protected override void OnPropsChanged() =>
                        _log.Add($"OnPropsChanged (render #{_renderCount + 1})");

                    protected override Task OnPropsChangedAsync()
                    {
                        _log.Add("OnPropsChangedAsync");
                        return Task.CompletedTask;
                    }

                    protected override void OnRendered(bool firstRender) =>
                        _log.Add($"OnRendered(firstRender: {firstRender})");

                    public override Component Render()
                    {
                        _renderCount++;
                        return /* ... */;
                    }
                }
                """,
                Notes:
                "OnMount* fires once; OnPropsChanged* fires on every render; OnRendered* fires after the render commits. StateHasChanged() asks the live render handle for a re-render."),
            Div(Class: "alert alert-danger d-flex align-items-start mt-3", Children:
            [
                I(Class: "bi bi-exclamation-triangle-fill me-3 fs-4"),
                Div(Children:
                [
                    Strong(Children: ["Failure model:"]),
                    " if an async hook faults, the framework logs the exception to ",
                    Code(Children: ["Console.Error"]),
                    " and does NOT trigger a re-render — so a component stuck on a loading placeholder is usually a hook that threw."
                ])
            ])
        );
}
