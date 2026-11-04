using System.Text.Json;
using Rask.Core.Diagnostics;

namespace Rask.Core.Tests.Components;

// What a control is sent and what it says about it: one change frame to the page's one change handler, and the
// diagnostics reported meanwhile. The sink is one for the process, so a class using it runs in a non-parallel
// collection (ConsoleRedirect).
internal static class Refusals
{
    /// <summary>Renders the page live and sends its one change handler <paramref name="frame" />.</summary>
    public static Task Change(Component view, string frame) => Send(view, "change", frame);

    /// <summary>Renders the page live and sends its one handler of <paramref name="domEvent" /> <paramref name="frame" />.</summary>
    public static async Task Send(Component view, string domEvent, string frame)
    {
        var html = view.RenderAsLiveRoot();
        var id = MarkupAssert.Attr(html, "data-rask-on-" + domEvent);
        using var doc = JsonDocument.Parse(frame);
        Assert.True(await view.TryInvokeHandlerAsync(id!, doc.RootElement));
    }

    /// <summary>What <paramref name="act" /> reported to the diagnostics sink.</summary>
    public static async Task<List<RaskDiagnosticEvent>> Reported(Func<Task> act)
    {
        var captured = new List<RaskDiagnosticEvent>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = captured.Add;
        try
        {
            await act();
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        return captured;
    }
}
