using System.Text;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// The browser host's side of a page's callback that closes the editor whose handler raised it: the reload
// that follows is the page's, cancelled with the page and not with the editor it has just unmounted.
//
// The dispatch ends when the handler and every render around its continuations have, so what the page was
// sent is read from the frames the session pushed once it returns — not polled out of the send buffer, which
// a later render writes over. Its wait bounds a hang: a continuation lost with a render never ends the dispatch.
[Collection("WasmSession")]
public sealed class CallbackOwnerLifetimeTests : ResettingTestBase, IDisposable
{
    private readonly List<string> _frames = [];

    public CallbackOwnerLifetimeTests()
        : base(LiveDiffMode.DisabledFull) =>
        JSInterop.AppliedFrames = _frames;

    public void Dispose() => JSInterop.AppliedFrames = null;

    [Fact]
    public async Task A_pages_callback_that_closes_the_editor_that_raised_it_reloads_with_both_reads()
    {
        var (session, _) = NewSession<CallbackStubApp>(diffMode: DiffMode);
        var save = Regex.Match(
            Encoding.UTF8.GetString(await session.InitialRenderAsync()), "data-rask-on-click=..([^\"\\\\]+)");

        await session
            .DispatchAsync(Utf8($$"""{"id":"{{save.Groups[1].Value}}","type":"click"}"""))
            .WaitAsync(WaitFor.HangCeiling, TestContext.Current.CancellationToken);

        Assert.Contains("edited:6", _frames[^1], StringComparison.Ordinal);
        Assert.DoesNotContain(_frames, frame => frame.Contains("Application error", StringComparison.Ordinal));
    }
}
