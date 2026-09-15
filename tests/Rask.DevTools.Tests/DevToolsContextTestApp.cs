using Rask.Core;
using Rask.Core.Components;

namespace Rask.DevTools.Tests;

/// <summary>A page that provides context in one component and reads it in another, with a click to render it again.</summary>
public sealed partial class DevToolsContextTestApp : Component
{
    private int _clicks;

    protected override Component? Render() =>
    [
        Button.OnClick(() => _clicks++)[$"clicks={_clicks}"],
        DevToolsContextShell[DevToolsContextReader]
    ];
}

/// <summary>What <see cref="DevToolsContextShell" /> provides.</summary>
public sealed record DevToolsTestTheme(string Name);

/// <summary>Provides a theme and a secret around whatever it is given.</summary>
public sealed partial class DevToolsContextShell : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Context.Provide(new DevToolsTestTheme("dark"))[
            Context.Provide("s3cr3t", Name: "api-token")[
                Div[Children ?? []]
            ]
        ];
}

/// <summary>Reads the theme twice, the secret once, and something nobody provides.</summary>
public sealed partial class DevToolsContextReader : Component
{
    /// <inheritdoc />
    protected override Component? Render()
    {
        var theme = Context.Required<DevToolsTestTheme>();
        _ = Context.Get<DevToolsTestTheme>();
        _ = Context.Has<int>("page-size");
        return Span[$"{theme.Name}:{Context.Get<string>("api-token")?.Length}"];
    }
}
