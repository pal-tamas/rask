using System.Text;
using System.Text.RegularExpressions;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// The browser host's side of `await thing.Save(); Go.To("/list");`: the list mounts inside the handler's turn
// and loads under its own lifetime, not under the saving page's, which the navigation ends.
[Collection("WasmSession")]
public sealed class ArrivalAfterHandlerTests() : ResettingTestBase(LiveDiffMode.DisabledFull)
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_list_reached_by_Go_from_a_handler_shows_both_of_its_reads(int button)
    {
        var (session, _) = NewSession<ArrivalStubApp>(diffMode: DiffMode);
        var handlers = Regex.Matches(
            Encoding.UTF8.GetString(await session.InitialRenderAsync()), "data-rask-on-click=..([^\"\\\\]+)",
            RegexOptions.None, TimeSpan.FromSeconds(1));

        await session.DispatchAsync(Utf8($$"""{"id":"{{handlers[button].Groups[1].Value}}","type":"click"}"""));

        await ShowsTheLoadedList(session);
    }

    [Fact]
    public async Task A_list_reached_by_a_link_shows_both_of_its_reads()
    {
        var (session, _) = NewSession<ArrivalStubApp>(diffMode: DiffMode);
        await session.InitialRenderAsync();

        await session.DispatchAsync(Utf8("""{"type":"navigate","path":"/list","query":""}"""));

        await ShowsTheLoadedList(session);
    }

    private static Task ShowsTheLoadedList(WasmLiveSession session) =>
        WaitFor.True(
            () => Encoding.UTF8.GetString(session.LastSentFrame).Contains("rows:3 total:3", StringComparison.Ordinal),
            "the list, loaded, in the frame the page was last sent");
}
