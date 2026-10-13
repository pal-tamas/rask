using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Globalization;
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
    // Unmount can't re-resolve RouteState from the render scope.
    private RouteState? _route;

    protected override Task OnMount()
    {
        // Re-render when the route changes so the displayed missing-path stays accurate
        // for in-session navigations into other unknown routes.
        _route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        if (_route is null)
        {
            return Task.CompletedTask;
        }

        _route.Changed += StateHasChanged;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        if (_route is null)
        {
            return Task.CompletedTask;
        }

        _route.Changed -= StateHasChanged;
        return Task.CompletedTask;
    }

    // Stands where the path goes while the sentence is read from the catalog. No address holds it.
    private const string PathPlace = "\u0001";

    // The tab says what the page says. The last title in the walk wins, so this one stands over the app's.
    protected override Component? HeadAssets => Title[RaskStrings.Get(RaskString.NotFoundTitle, "Page not found")];

    protected override Component? Render()
    {
        var route = LiveRenderContext.Current?.Services?.GetService<RouteState>();
        var path = route?.Path ?? "";

        return Div
            .Style("max-width:640px;margin:4rem auto;padding:0 1rem;font-family:system-ui,sans-serif;color:#1f2937;")[
            H1.Style("margin:0 0 0.75rem;font-size:2rem;")[
                RaskStrings.Get(RaskString.NotFoundTitle, "Page not found")],
            P.Style("margin:0 0 1.25rem;color:#4b5563;line-height:1.5;")[Sentence(path.Length == 0 ? "/" : path)],
            A
                .Href("/")
                .Style("display:inline-block;padding:0.5rem 0.9rem;background:#2563eb;color:#fff;text-decoration:none;border-radius:0.375rem;")
                [RaskStrings.Get(RaskString.NotFoundBackHome, "Back to home")]
        ];
    }

    // The sentence around the path, which is what the visitor typed: written as text, never as markup, and
    // never as part of the text the sentence is formatted from. A translation from before the sentence
    // carried the path has no place for it, and gets it where it always stood: after the text.
    private static Component[] Sentence(string path)
    {
        var shown = Code.Style("background:#f3f4f6;padding:0.1rem 0.35rem;border-radius:0.25rem;")[path];
        var sentence = RaskStrings.Get(RaskString.NotFoundBody, "No route is registered for {0}.", PathPlace);
        var at = sentence.IndexOf(PathPlace, StringComparison.Ordinal);

        return at < 0 ? [sentence, shown, "."] : [sentence[..at], shown, sentence[(at + PathPlace.Length)..]];
    }
}
