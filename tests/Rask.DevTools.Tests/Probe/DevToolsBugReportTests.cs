using Rask.DevTools.Probe;

namespace Rask.DevTools.Tests.Probe;

/// <summary>
///     Which errors may be reported as framework bugs, and what a report carries: Rask's frames by name, the app's collapsed,
///     and nothing of the app's data.
/// </summary>
public sealed class DevToolsBugReportTests
{
    private const string FrameworkStack = """
           at Rask.Core.Live.FrameDiffer.DiffSiblings(ReadOnlySpan`1 old, ReadOnlySpan`1 next) in /_/src/Rask.Core/Live/FrameDiffer.cs:line 312
           at System.Collections.Generic.List`1.ForEach(Action`1 action)
           at Shop.Features.Cart.CartPage.Render() in /Users/me/Shop/Features/Cart/CartPage.cs:line 40
           at Shop.Features.Cart.CartRow.Render() in /Users/me/Shop/Features/Cart/CartRow.cs:line 12
        --- End of stack trace from previous location ---
           at Rask.Core.Component.RenderForLive()
        """;

    private const string AppStack = """
           at Shop.Features.Cart.CartPage.Checkout() in /Users/me/Shop/Features/Cart/CartPage.cs:line 88
           at Rask.Core.Component.TryInvokeHandlerCoreAsync(String id, JsonElement payload)
        """;

    [Fact]
    public void A_stack_whose_innermost_code_is_raskS_is_a_likely_framework_bug_with_the_app_collapsed()
    {
        var verdict = DevToolsBugReport.FromDotNet(FrameworkStack, ["Shop"]);

        Assert.True(verdict.LikelyFrameworkBug);
        Assert.Equal(
            ["Rask.Core.Live.FrameDiffer.DiffSiblings", "[app code]", "Rask.Core.Component.RenderForLive"],
            verdict.Frames);
    }

    [Fact]
    public void A_stack_whose_innermost_code_is_the_appS_is_not_even_when_rask_called_it()
    {
        Assert.False(DevToolsBugReport.FromDotNet(AppStack, ["Shop"]).LikelyFrameworkBug);
    }

    [Fact]
    public void An_app_that_lives_under_a_rask_namespace_is_still_the_app()
    {
        const string stack = "   at Rask.Showcase.DeployCard.Deploy() in /x/App.cs:line 3\n   at Rask.Core.Component.Invoke()";

        Assert.False(DevToolsBugReport.FromDotNet(stack, ["Rask.Showcase"]).LikelyFrameworkBug);
        Assert.True(DevToolsBugReport.FromDotNet(stack, []).LikelyFrameworkBug);
        Assert.False(DevToolsBugReport.FromDotNet(null, []).LikelyFrameworkBug);
    }

    [Fact]
    public void A_script_stack_is_raskS_when_its_innermost_frame_is_in_a_rask_script_and_names_only_the_file()
    {
        const string chrome = """
            TypeError: Cannot read properties of null
                at applyDiff (http://localhost:5000/rask/rask.js:1:2345)
                at http://localhost:5000/app/chart.js:10:5
            """;
        const string firefox = "applyDiff@http://localhost:5000/_framework/rask.wasm.js:3:9\n@http://localhost:5000/app.js:1:1";
        const string app = "Error: nope\n    at draw (http://localhost:5000/js/chart.js:4:2)\n    at applyDiff (http://localhost:5000/rask/rask.js:1:2)";

        var fromChrome = DevToolsBugReport.FromScript(chrome);
        Assert.True(fromChrome.LikelyFrameworkBug);
        Assert.Equal(["applyDiff (rask.js:1:2345)", "[app code]"], fromChrome.Frames);
        Assert.True(DevToolsBugReport.FromScript(firefox).LikelyFrameworkBug);
        Assert.False(DevToolsBugReport.FromScript(app).LikelyFrameworkBug);
    }

    [Fact]
    public void A_draft_carries_the_type_the_components_the_host_and_raskS_frames_and_nothing_of_the_app()
    {
        var verdict = DevToolsBugReport.FromDotNet(FrameworkStack, ["Shop"]);
        var error = new DevToolsError(
            1, DateTimeOffset.Now, DevToolsErrorKind.Render, false, "NullReferenceException",
            "customer 4711 has no card on file", FrameworkStack, ["App", "CartPage", "CartRow"], 7, false, false, 1,
            verdict.LikelyFrameworkBug, verdict.Frames);

        var (title, body) = DevToolsBugReport.Draft(
            error, new DevToolsBugReport.Environment("Server", "0.22.0", ".NET 10.0.1", "macOS 15.4", "Chrome 131"));

        Assert.Equal("NullReferenceException in Rask.Core.Live.FrameDiffer.DiffSiblings", title);
        Assert.Contains("`NullReferenceException` in rendering", body);
        Assert.Contains("`App › CartPage › CartRow`", body);
        Assert.Contains("Server · Rask 0.22.0 · .NET 10.0.1 · macOS 15.4 · Chrome 131", body);
        Assert.Contains("at Rask.Core.Live.FrameDiffer.DiffSiblings\n[app code]\nat Rask.Core.Component.RenderForLive", body);
        // The app's own: never the message, a file path, a line, or an app frame.
        Assert.DoesNotContain("4711", body);
        Assert.DoesNotContain("/Users/me", body);
        Assert.DoesNotContain("line ", body);
        Assert.DoesNotContain("Shop.", body);
    }

    [Fact]
    public void The_issue_url_is_the_projects_new_issue_page_filled_in_and_kept_under_its_limit()
    {
        var url = DevToolsBugReport.IssueUrl("A title & more", "Line one\nat Rask.X");
        Assert.StartsWith("https://github.com/pal-tamas/rask/issues/new?labels=bug&title=A%20title%20%26%20more&body=", url);
        Assert.Contains("Line%20one%0Aat%20Rask.X", url);

        var frames = string.Join('\n', Enumerable.Range(0, 400).Select(i => "at Rask.Core.Very.Long.Namespace.Type" + i + ".Method"));
        var body = "**What happened:** x\n\n**Stack**:\n```\n" + frames + "\n```\n\n**What were you doing?**";
        var shortened = DevToolsBugReport.IssueUrl("t", body);

        Assert.True(shortened.Length <= DevToolsBugReport.UrlLimit, $"{shortened.Length} characters");
        // The innermost frames are what is kept, and the question after the stack stays.
        var decoded = Uri.UnescapeDataString(shortened);
        Assert.Contains("Type0.Method", decoded);
        Assert.DoesNotContain("Type399.Method", decoded);
        Assert.Contains("What were you doing", decoded);
    }
}
