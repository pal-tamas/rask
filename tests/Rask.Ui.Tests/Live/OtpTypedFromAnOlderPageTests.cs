using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Core.Live;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     A code typed faster than the page answers: every character is read from the page as loaded, and each one
///     the page hears is a render of the cells — so the last one arrives several pages late, and is still heard.
/// </summary>
/// <remarks>
///     An event from an older page reaches the handler that is the same as the one it was sent to. The bound
///     field's handler closed over what each render worked out, so no two renders made the same one: the last
///     character ran nothing, and the next action read the code one short.
/// </remarks>
public sealed partial class OtpTypedFromAnOlderPageTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task A_code_typed_before_the_page_answered_reaches_the_next_action_whole()
    {
        var model = new Holder();
        var resent = "";
        var page = new StubComponent(() => Div[
            Ui.Otp.Bind(() => model.Code).Length(6),
            Button.OnClick(() => resent = model.Code)["Resend"]
        ]);
        using var cache = new SessionRenderCache();
        var html = Render(cache, page);
        var typed = Id(html, "data-rask-on-input");
        var pressed = Id(html, "data-rask-on-click");

        foreach (var code in new[] { "4", "43", "432", "4321" })
        {
            await Hear(cache, page, typed, $"{{\"value\":\"{code}\",\"v\":0}}");
        }

        var heard = await Hear(cache, page, pressed, "{\"v\":0}");

        Assert.True(heard);
        Assert.Equal("4321", resent);
    }

    private static string Id(string html, string attribute) =>
        Regex.Match(html, attribute + "=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;

    // One event of the session, read from the page the browser loaded: its handler, then the render that answers it.
    private static async Task<bool> Hear(SessionRenderCache cache, Component page, string id, string payload)
    {
        using var frame = JsonDocument.Parse(payload);
        var heard = await page.TryInvokeHandlerAsync(id, frame.RootElement);
        Render(cache, page);

        return heard;
    }

    // Renders, and says the browser was sent the result.
    private static string Render(SessionRenderCache cache, Component page)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = page.RenderAsLiveRoot();
        }

        cache.TryComputeDiff([], out _, html);
        page.HandlersSent();

        return html;
    }

    private sealed class Holder
    {
        public string Code { get; set; } = "";
    }
}
