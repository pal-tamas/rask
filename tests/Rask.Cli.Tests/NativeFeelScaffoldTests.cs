using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
///     A scaffolded app looks like an app on a phone (owner, 2026-11-02): edge to edge with the kit keeping clear of
///     the notch, the browser's bar in the page's ground, text smoothed and, installed, no bounce of the whole app.
///     The kit ships no rule for a document an app owns, so the scaffold writes these into the app's own files.
/// </summary>
public sealed class NativeFeelScaffoldTests
{
    private const string Root = "/tmp/native-feel";
    private const string Viewport = "width=device-width, initial-scale=1, viewport-fit=cover";

    [Fact]
    public void A_server_app_writes_the_edge_to_edge_viewport_and_a_bar_colour_for_each_scheme()
    {
        var shell = Server()["Features/Shared/App.cs"];

        Assert.Contains($"Meta.Name(\"viewport\").Content(\"{Viewport}\")", shell, StringComparison.Ordinal);
        Assert.Contains("Meta.Name(\"theme-color\").Content(\"#ffffff\").Media(\"(prefers-color-scheme: light)\")", shell, StringComparison.Ordinal);
        Assert.Contains("Meta.Name(\"theme-color\").Content(\"#27272a\").Media(\"(prefers-color-scheme: dark)\")", shell, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wasm", "wwwroot/index.html")]
    [InlineData("wasm-hosted", "Client/wwwroot/index.html")]
    public void A_browser_apps_boot_page_writes_the_edge_to_edge_viewport_and_a_bar_colour_for_each_scheme(string template, string page)
    {
        var html = Generate(template)[page];

        Assert.Contains($"<meta content=\"{Viewport}\" name=\"viewport\"/>", html, StringComparison.Ordinal);
        Assert.Contains("<meta content=\"#ffffff\" media=\"(prefers-color-scheme: light)\" name=\"theme-color\"/>", html, StringComparison.Ordinal);
        Assert.Contains("<meta content=\"#27272a\" media=\"(prefers-color-scheme: dark)\" name=\"theme-color\"/>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("maximum-scale", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    [InlineData("wasm-hosted")]
    public void The_apps_stylesheet_smooths_its_text_and_holds_an_installed_app_still_at_its_ends(string template)
    {
        var sheet = Generate(template)["Styles/app.css"].Replace(" ", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);

        Assert.Contains("body{-webkit-font-smoothing:antialiased;-moz-osx-font-smoothing:grayscale;}", sheet, StringComparison.Ordinal);
        Assert.Contains("@media(display-mode:standalone){html{overscroll-behavior-y:none;}}", sheet, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Generate(string template) => template switch
    {
        "wasm" => Index(ProjectGenerator.GenerateWasm(Root, "App", pwa: false, docker: false, "1.2.3")),
        "wasm-hosted" => Index(ProjectGenerator.GenerateWasmHosted(Root, "App", new ServerBatteries(), "1.2.3")),
        _ => Server(),
    };

    private static Dictionary<string, string> Server() => Index(ProjectGenerator.GenerateServer(Root, "App", new ServerBatteries(), "1.2.3"));

    private static Dictionary<string, string> Index(ScaffoldResult result) =>
        result.Files.ToDictionary(
            f => f.Path.Replace('\\', '/')[(Root.Length + 1)..],
            f => f.Content,
            StringComparer.Ordinal);
}
