using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Core.Components;

/// <summary>
///     The built-in 404 page, rendered when no route matches. Replace it by registering your own not-found
///     page rather than editing this.
/// </summary>
public sealed class DefaultNotFoundPage : Component
{
    // Cached at mount because LiveRenderContext.Current is null during disposal, so
    // OnUnmount can't re-resolve RouteState from the render scope.
    private RouteState? _route;

    protected override void OnMount()
    {
        // Re-render when the route changes so the displayed missing-path stays accurate
        // for in-session navigations into other unknown routes.
        _route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        if (_route is null)
        {
            return;
        }

        _route.Changed += StateHasChanged;
    }

    protected override void OnUnmount()
    {
        if (_route is null)
        {
            return;
        }

        _route.Changed -= StateHasChanged;
    }

    protected override Component? Render()
    {
        var route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        var path = route?.Path ?? "";

        return Div
            .Style("max-width:640px;margin:4rem auto;padding:0 1rem;font-family:system-ui,sans-serif;color:#1f2937;")[
            H1.Style("margin:0 0 0.75rem;font-size:2rem;")["Page not found"],
            P.Style("margin:0 0 1.25rem;color:#4b5563;line-height:1.5;")[
                "No route is registered for ",
                Code.Style("background:#f3f4f6;padding:0.1rem 0.35rem;border-radius:0.25rem;")[
                    path.Length == 0 ? "/" : path
                ],
                "."
            ],
            A
                .Href("/")
                .Style("display:inline-block;padding:0.5rem 0.9rem;background:#2563eb;color:#fff;text-decoration:none;border-radius:0.375rem;")
                ["Back to home"]
        ];
    }
}
