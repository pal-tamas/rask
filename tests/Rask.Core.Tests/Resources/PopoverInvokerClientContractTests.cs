namespace Rask.Core.Tests.Resources;

/// <summary>
///     Source-level contract: a click handler must not cancel a popover invoker's default action.
/// </summary>
/// <remarks>
///     <para>
///         Both transports delegate <c>click</c> from <c>document</c> and <c>preventDefault()</c> it, so
///         that an <c>&lt;a href&gt;</c> or a bare <c>&lt;button&gt;</c> carrying a C# handler does not
///         also navigate or submit. Opening a popover is a button's default action too — so an element
///         with both <c>popovertarget</c> and <c>OnClick</c> rendered correct markup and did nothing at
///         all when pressed, on both hosts, with nothing reported anywhere.
///     </para>
///     <para>
///         Structural rather than behavioural, for the same reason as
///         <see cref="HotReloadClientContractTests" />: these entry points boot a transport against a
///         live document and cannot be loaded in Node. The behavioural proof is the browser E2E over
///         <c>UiSelect</c>'s drawn list, which is a popover opened by a button that also has a C#
///         handler. What is worth pinning here is that BOTH copies carry the carve-out — they are two
///         hand-kept copies of one listener, and the way that fails is one of them drifting.
///     </para>
/// </remarks>
public class PopoverInvokerClientContractTests
{
    private static readonly string _repoRoot = LocateRepoRoot();

    private static string ServerJs => Read("src", "Rask.Server", "Resources", "rask.ts");
    private static string WasmJs => Read("src", "Rask.Wasm", "Resources", "rask.wasm.ts");

    /// <summary>The bundle the browser actually loads, so this proves what shipped and not only the input.</summary>
    private static string BuiltWasmJs => Read("src", "Rask.Wasm", "Browser", "rask.wasm.js");

    [Theory]
    [InlineData("server")]
    [InlineData("wasm")]
    public void The_click_delegate_declines_to_cancel_a_popover_invoker(string transport)
    {
        var js = transport == "server" ? ServerJs : WasmJs;
        var listener = ClickDelegate(js);

        Assert.Contains("[popovertarget]", listener, StringComparison.Ordinal);

        // The point is the GUARD, not merely a mention: an unconditional preventDefault beside a
        // popovertarget lookup would satisfy a substring check and still swallow the activation.
        Assert.DoesNotContain("\n        e.preventDefault();", listener, StringComparison.Ordinal);
        Assert.DoesNotContain("\n    e.preventDefault();", listener, StringComparison.Ordinal);
        Assert.Contains("if (!invoker) { e.preventDefault(); }", listener, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shipped_wasm_bundle_carries_it_too()
    {
        // Read rather than trusted: the built bundle is committed, so a source fix that was never
        // rebuilt would leave the browser running the old listener with every source test green.
        Assert.Contains("[popovertarget]", BuiltWasmJs, StringComparison.Ordinal);
    }

    // The delegated click listener, from its `[data-rask-on-click]` lookup to the send() it ends with.
    private static string ClickDelegate(string js)
    {
        var start = js.IndexOf("[data-rask-on-click]", StringComparison.Ordinal);
        Assert.True(start >= 0, "the delegated click listener was not found");

        var end = js.IndexOf("addEventListener(\"change\"", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of the delegated click listener was not found");

        return js[start..end];
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([_repoRoot, .. parts]));

    private static string LocateRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }
}
