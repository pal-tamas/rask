using System.Text.Json;

namespace Rask.Wasm.Tests.JsInteropRuntime;

/// <summary>
///     <c>__raskHost.navigate</c>: the bridge front-end code navigates in-app through.
/// </summary>
/// <remarks>
///     What reaches .NET has to be the frame a click on an <c>a[data-rask-nav]</c> sends. Driven on Jint against the built
///     <c>rask.wasm.js</c> — see <c>HostNavigateFixture.ts</c>.
/// </remarks>
public sealed class HostNavigateTests
{
    [Fact]
    public void Navigating_from_front_end_code_sends_the_frame_a_nav_link_click_sends()
    {
        var result = Run("/orders/7?page=2#totals");

        var frame = Assert.Single(result.GetProperty("frames").EnumerateArray());

        Assert.Equal("navigate", frame.GetProperty("type").GetString());
        Assert.Equal("/orders/7", frame.GetProperty("path").GetString());
        Assert.Equal("?page=2", frame.GetProperty("query").GetString());
        Assert.False(frame.TryGetProperty("replace", out _));
    }

    [Fact]
    public void Replacing_is_carried_to_the_host()
    {
        var result = Run("/login", "replace");

        var frame = Assert.Single(result.GetProperty("frames").EnumerateArray());

        Assert.True(frame.GetProperty("replace").GetBoolean());
    }

    [Fact]
    public void A_url_of_another_origin_is_refused_and_says_so()
    {
        var result = Run("https://elsewhere.example/orders");

        var error = Assert.Single(result.GetProperty("errors").EnumerateArray()).GetString();

        Assert.Empty(result.GetProperty("frames").EnumerateArray());
        Assert.Contains("https://elsewhere.example/orders", error, StringComparison.Ordinal);
    }

    private static JsonElement Run(params string[] arguments)
    {
        var bundle = Path.Combine(RepoRoot(), "src", "Rask.Wasm", "Browser", "rask.wasm.js");
        Assert.True(File.Exists(bundle), $"Bundle missing: {bundle} — build src/Rask.Wasm first.");

        return ScriptFixture.Run("HostNavigateFixture", [bundle, .. arguments]);
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
