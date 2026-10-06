using System.Text.Json;

namespace Rask.Wasm.Tests.JsInteropRuntime;

/// <summary>
///     <c>__raskHost.navigate</c>: the bridge the generated <c>@rask/routes</c> module navigates through.
/// </summary>
/// <remarks>
///     Front-end island code writes <c>Routes.OrdersPage({ Page: 2 }).Go()</c>; what reaches .NET has to
///     be the frame a click on an <c>a[data-rask-nav]</c> sends. Driven under Node against the built
///     <c>rask.wasm.js</c> — see <c>HostNavigateFixture.ts</c>.
/// </remarks>
public sealed class HostNavigateTests
{
    [Fact]
    public void Navigating_from_front_end_code_sends_the_frame_a_nav_link_click_sends()
    {
        if (Run("/orders/7?page=2#totals") is not { } result)
        {
            return;
        }

        var frame = Assert.Single(result.GetProperty("frames").EnumerateArray());

        Assert.Equal("navigate", frame.GetProperty("type").GetString());
        Assert.Equal("/orders/7", frame.GetProperty("path").GetString());
        Assert.Equal("?page=2", frame.GetProperty("query").GetString());
        Assert.False(frame.TryGetProperty("replace", out _));
    }

    [Fact]
    public void Replacing_is_carried_to_the_host()
    {
        if (Run("/login", "replace") is not { } result)
        {
            return;
        }

        var frame = Assert.Single(result.GetProperty("frames").EnumerateArray());

        Assert.True(frame.GetProperty("replace").GetBoolean());
    }

    [Fact]
    public void A_url_of_another_origin_is_refused_and_says_so()
    {
        if (Run("https://elsewhere.example/orders") is not { } result)
        {
            return;
        }

        var error = Assert.Single(result.GetProperty("errors").EnumerateArray()).GetString();

        Assert.Empty(result.GetProperty("frames").EnumerateArray());
        Assert.Contains("https://elsewhere.example/orders", error, StringComparison.Ordinal);
    }

    // Null when node is not on PATH: the JS-driven check cannot run, and node is not required to build Rask.
    private static JsonElement? Run(params string[] arguments)
    {
        var bundle = Path.Combine(RepoRoot(), "src", "Rask.Wasm", "Browser", "rask.wasm.js");
        Assert.True(File.Exists(bundle), $"Bundle missing: {bundle} — build src/Rask.Wasm first.");

        return NodeFixture.Run("HostNavigateFixture", [bundle, .. arguments]);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"Could not locate Rask.slnx above {AppContext.BaseDirectory}");
    }
}
