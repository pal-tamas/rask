using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.TestSupport;

namespace Rask.UiTests.Live;

/// <summary>
///     A document rendered with no host — what a WASM app's prerender writes — takes as many waves as its reads
///     take to come back, and says the same thing whatever that number is.
/// </summary>
/// <remarks>
///     A tooltip wires its trigger at render, on a button that outlives the wave. Wired a second time, an icon
///     button used to be described by the tooltip that already named it, so a page was one document after one
///     wave and another after two.
/// </remarks>
public sealed partial class TooltipLinkAcrossWavesTests : global::Rask.Core.RaskMarkup
{
    public const int Rows = 5;

    [Fact]
    public async Task An_icon_button_is_named_by_its_tooltip_alone_however_many_waves_follow_the_one_that_drew_it()
    {
        var once = await Prerendered(later: 0);

        var again = await Prerendered(later: 3);

        Assert.Equal(Rows, IdLinks.Count(again.Html, "aria-labelledby", "ui-tooltip-"));
        Assert.Equal(0, IdLinks.Count(again.Html, "aria-describedby", "ui-tooltip-"));
        Assert.Empty(IdLinks.Broken(again.Html));
        Assert.Equal(IdLinks.Shape(once.Html).Replace("later 0", "later 3", StringComparison.Ordinal), IdLinks.Shape(again.Html));
    }

    private static async Task<PrerenderResult> Prerendered(int later)
    {
        await using var services = new ServiceCollection()
            .AddScoped<RouteState>()
            .AddSingleton(new Reads(later))
            .BuildServiceProvider();

        return await RaskPrerender.RenderDocument(
            new StubComponent(() => TooltipWavesAbove), services, TimeSpan.FromSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>How many reads follow the rows', and the moment the rows' own is back.</summary>
    public sealed record Reads(int Later)
    {
        public TaskCompletionSource Back { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

// What stands above the rows, as a layout stands above a page: once their read is back it goes on reading, and
// each read it finishes renders IT again — so the rows under it are walked again and not rendered again.
internal sealed partial class TooltipWavesAbove(TooltipLinkAcrossWavesTests.Reads reads) : Component
{
    private int _done;

    protected override async Task OnMount()
    {
        while (_done < reads.Later)
        {
            await reads.Back.Task;
            await Task.Delay(1);
            _done++;
            StateHasChanged();
        }
    }

    protected override Component? Render() => Div[P[$"later {_done}"], TooltipWavesRows];
}

// Rows that arrive from an awaited read, each ending in a pencil its tooltip names.
internal sealed partial class TooltipWavesRows(TooltipLinkAcrossWavesTests.Reads reads) : Component
{
    private bool _read;

    protected override async Task OnMount()
    {
        await Task.Delay(1);
        _read = true;
        reads.Back.TrySetResult();
    }

    protected override Component? Render() =>
        !_read
            ? Div["reading"]
            : Div[Enumerable.Range(0, TooltipLinkAcrossWavesTests.Rows).Select(row => (Component)Div.Key(row)[
            Ui.Button.Ghost.Sm.Square(true).Icon(Ui.IconName.Pencil).Tooltip("Edit")])];
}
