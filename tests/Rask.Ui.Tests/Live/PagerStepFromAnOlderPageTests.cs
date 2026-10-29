using Rask.Core;
using Rask.TestSupport;

#pragma warning disable RASK014 // the test owns the root it re-renders; there is no parent to build it

namespace Rask.UiTests.Live;

/// <summary>
///     Next pressed faster than the page answers: every press is read from the page as loaded, and each one the
///     page hears is a render of the pager — so the later presses arrive pages late, and each still turns a page.
/// </summary>
/// <remarks>
///     The step buttons closed over the page each render worked out to go to, so the Next of one render was never
///     the same handler as the Next of the next: the second quick press ran nothing.
/// </remarks>
public sealed partial class PagerStepFromAnOlderPageTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task Next_pressed_three_times_before_the_page_answered_turns_three_pages()
    {
        var pager = new UiPaginator { Page = 1, PerPage = 10, Total = 100 };
        using var session = new OlderPage(Pager(() => pager, page => pager = new UiPaginator { Page = page, PerPage = 10, Total = 100 }));
        var next = session.Ids("click")[^1];

        var heard = await PressAsync(session, next, 3);

        Assert.True(heard);
        Assert.Equal(4, pager.Page);
    }

    [Fact]
    public async Task Next_pressed_past_the_last_page_before_the_page_answered_stays_on_the_last()
    {
        var pager = new UiPaginator { Page = 1, PerPage = 10, Total = 20 };
        var turns = 0;
        using var session = new OlderPage(Pager(() => pager, page =>
        {
            turns++;
            pager = new UiPaginator { Page = page, PerPage = 10, Total = 20 };
        }));
        var next = session.Ids("click")[^1];

        await PressAsync(session, next, 3);

        Assert.Equal(2, pager.Page);
        Assert.Equal(1, turns);
    }

    private static StubComponent Pager(Func<UiPaginator> pager, Action<int> onPage) =>
        new(() => Ui.Pagination.Paginator(pager()).OnPage(onPage));

    private static async Task<bool> PressAsync(OlderPage session, string id, int times)
    {
        var heard = true;
        for (var i = 0; i < times; i++)
        {
            heard = await session.PressedAsync(id);
        }

        return heard;
    }
}
