using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask.Core.Components;

public sealed class DefaultNotFoundPage : Component
{
    protected internal override bool BypassRenderCache => true;

    protected override Component Render()
    {
        var route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        var path = route?.Path ?? "";

        return Components.Div(
            Style: "max-width:640px;margin:4rem auto;padding:0 1rem;font-family:system-ui,sans-serif;color:#1f2937;")[
            Components.H1(Style: "margin:0 0 0.75rem;font-size:2rem;")["Page not found"],
            Components.P(Style: "margin:0 0 1.25rem;color:#4b5563;line-height:1.5;")[
                "No route is registered for ",
                Components.Code(Style: "background:#f3f4f6;padding:0.1rem 0.35rem;border-radius:0.25rem;")[
                    path.Length == 0 ? "/" : path
                ],
                "."
            ],
            Components.A(Href: "/",
                Style:
                "display:inline-block;padding:0.5rem 0.9rem;background:#2563eb;color:#fff;text-decoration:none;border-radius:0.375rem;")["Back to home"]
        ];
    }
}
