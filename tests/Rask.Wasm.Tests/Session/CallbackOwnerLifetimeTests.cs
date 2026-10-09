using System.Text;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// The browser host's side of a page's callback that closes the editor whose handler raised it: the reload
// that follows is the page's, cancelled with the page and not with the editor it has just unmounted.
[Collection("WasmSession")]
public sealed class CallbackOwnerLifetimeTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Fact]
    public async Task A_pages_callback_that_closes_the_editor_that_raised_it_reloads_with_both_reads()
    {
        var (session, _) = NewSession<CallbackStubApp>(diffMode: DiffMode);
        var save = Regex.Match(
            Encoding.UTF8.GetString(await session.InitialRenderAsync()), "data-rask-on-click=..([^\"\\\\]+)",
            RegexOptions.None, TimeSpan.FromSeconds(1));

        await session.DispatchAsync(Utf8($$"""{"id":"{{save.Groups[1].Value}}","type":"click"}"""));

        await WaitFor.True(
            () => Encoding.UTF8.GetString(session.LastSentFrame).Contains("edited:6", StringComparison.Ordinal),
            "the reloaded list in the frame the page was last sent");
    }
}
