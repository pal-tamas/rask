using static Rask.Core.Tags;

namespace Rask.Core.Components;

public sealed class DefaultErrorPage : Component
{
    private readonly Exception _error;

    public DefaultErrorPage(Exception error) => _error = error;

    protected internal override bool BypassRenderCache => true;

    protected override Component Render()
    {
        var showStack = IsDevelopmentEnvironment();
        var typeName = _error.GetType().FullName ?? _error.GetType().Name;

        var children = new List<Child>
        {
            H1(Style: "margin:0 0 0.75rem;font-size:1.5rem;color:#b42323;", Children: ["Something went wrong"]),
            P(Style: "margin:0 0 0.5rem;color:#4b5563;font-family:ui-monospace,Menlo,Consolas,monospace;font-size:0.9rem;",
                Children: [typeName]),
            Pre(
                Style:
                "margin:0;padding:0.75rem;background:#fbe9e9;border-radius:0.375rem;white-space:pre-wrap;font-size:0.9rem;color:#7f1d1d;",
                Children: [_error.Message])
        };

        if (showStack && !string.IsNullOrEmpty(_error.StackTrace))
        {
            children.Add(Pre(
                Style:
                "margin:1rem 0 0;padding:0.75rem;background:#f3f4f6;border-radius:0.375rem;white-space:pre-wrap;font-size:0.78rem;color:#4b5563;overflow:auto;max-height:300px;",
                Children: [_error.StackTrace]));
        }

        return Div(
            Class: "rask-error-boundary",
            Style:
            "max-width:720px;margin:4rem auto;padding:1.5rem;font-family:system-ui,sans-serif;color:#1f2937;border:1px solid #f5c2c0;background:#fff5f5;border-radius:0.5rem;",
            Children: children);
    }

    private static bool IsDevelopmentEnvironment()
    {
        // Read the standard ASP.NET environment variables so we can gate stack traces without
        // taking a Microsoft.Extensions.Hosting dependency on the framework core.
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
