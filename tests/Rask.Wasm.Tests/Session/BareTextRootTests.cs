using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Wasm.Tests.Infrastructure;

namespace Rask.Wasm.Tests.Session;

// #1238 on this host: a crumb whose whole render is `Text[heading.Text]` rendered nothing, so no frame ever
// carried its words. The first frame has to hold what the page named as it mounted, and each later change has to
// arrive as an op on the text node inside the crumb item's own element.
[Collection("WasmSession")]
public class BareTextRootTests() : ResettingTestBase(LiveDiffMode.Forced)
{
    // document > html > body > nav > div.item > div.step > the text node itself.
    private static readonly int[] CrumbPath = [1, 1, 0, 0, 0, 0];

    [Fact]
    public async Task The_first_frame_carries_the_words_a_page_set_while_it_mounted()
    {
        var session = NewCrumbSession();

        var html = Html(await session.InitialRenderAsync());

        Assert.Contains("<div class=\"step\">Orders</div>", html);
    }

    [Fact]
    public async Task A_bare_text_root_is_updated_removed_and_inserted_again_by_diff_ops()
    {
        var session = NewCrumbSession();
        var initial = Html(await session.InitialRenderAsync());

        var renamed = await Click(session, initial, "rename");
        var cleared = await Click(session, initial, "clear");
        var back = await Click(session, initial, "rename");

        Assert.Equal(((int)EditOpKind.UpdateText, "Invoices"), OpAt(renamed, CrumbPath));
        Assert.Equal((int)EditOpKind.RemoveSubtree, OpAt(cleared, CrumbPath).Kind);
        Assert.Equal(((int)EditOpKind.InsertSubtree, "Invoices"), OpAt(back, CrumbPath));
    }

    private WasmLiveSession NewCrumbSession() =>
        NewSession<BareTextCrumbApp>(configure: s => s.AddSingleton<CrumbHeading>(), diffMode: DiffMode).session;

    private static string Html(byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame.AsMemory());
        return doc.RootElement.GetProperty("html").GetString()!;
    }

    private static async Task<string> Click(WasmLiveSession session, string html, string buttonId)
    {
        var handler = Regex.Match(html, $"id=\"{buttonId}\" data-rask-on-click=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEqual(string.Empty, handler);
        return Encoding.UTF8.GetString(
            await session.DispatchAsync(Encoding.UTF8.GetBytes($$"""{"id":"{{handler}}","type":"click"}""")));
    }

    private static (int Kind, string? Value) OpAt(string frame, int[] path)
    {
        using var doc = JsonDocument.Parse(frame);
        foreach (var op in doc.RootElement.GetProperty("ops").EnumerateArray())
        {
            if (op[1].EnumerateArray().Select(slot => slot.GetInt32()).SequenceEqual(path))
            {
                return (op[0].GetInt32(), op.GetArrayLength() > 2 && op[2].ValueKind == JsonValueKind.String ? op[2].GetString() : null);
            }
        }

        Assert.Fail($"no op addresses [{string.Join(',', path)}] in {frame}");
        return default;
    }
}
