using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Messaging;
using Page = Rask.Testing.Page;

#pragma warning disable RASK014 // a test-defined component has no generated chain entry
#pragma warning disable RASK019 // a small test app; its <head> is not what is under test

namespace Rask.UiTests.Live;

/// <summary>
///     An app as a host mounts it, with <c>Ui.Toast</c> in its layout and no error boundary of its own: a
///     handler that throws — a dialog's confirm, a pager's page, a select's pick — is a danger toast with a
///     reference, in the reader's language, and the page is as the handler left it.
/// </summary>
public partial class HandlerThatFailsTests : global::Rask.Core.RaskMarkup
{
    [Theory]
    [InlineData("en-US", "That did not work. Please try again. The problem has been recorded. If it keeps happening, quote this reference: ")]
    [InlineData("hu-HU", "Nem sikerült. Kérjük, próbálja újra. A hibát rögzítettük. Ha újra előfordul, erre az azonosítóra hivatkozzon: ")]
    public async Task A_handler_that_throws_shows_a_danger_toast_with_a_reference_in_the_readers_language(string language, string sentence)
    {
        var page = Draw();
        await page.Click("Remove ABC-123");

        await In(language, async () => await page.Click("Delete"));

        var toast = page.Find("[data-ui-toast-dialog]");
        Assert.Matches(System.Text.RegularExpressions.Regex.Escape(sentence) + "[A-Z0-9]{8}", toast.TextContent);
        Assert.Equal("danger", page.Find("[data-variant]").Attribute("data-variant"));
        Assert.DoesNotContain("hunter2", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_confirm_whose_delete_throws_leaves_the_row_listed_and_the_dialog_as_the_handler_left_it()
    {
        var page = Draw();
        await page.Click("Remove ABC-123");

        await page.Click("Delete");

        Assert.Equal("ABC-123", page.TextOf("li > span"));
        Assert.Equal("true", page.Find("dialog").Attribute("data-rask-modal-open"));
        Assert.Equal("attempts=1", page.TextOf("#attempts"));
        Assert.False(page.Exists(".rask-error-boundary"));
    }

    [Fact]
    public async Task The_confirm_can_be_pressed_again_and_a_delete_that_goes_through_closes_the_dialog()
    {
        var page = Draw();
        await page.Click("Remove ABC-123");
        await page.Click("Delete");
        page.Instance.Fails = false;

        await page.Click("Delete");

        Assert.False(page.Exists("li"));
        Assert.Equal("false", page.Find("dialog").Attribute("data-rask-modal-open"));
    }

    [Fact]
    public async Task A_pager_whose_page_handler_throws_is_a_toast_and_the_pager_is_still_there()
    {
        var page = Draw();

        await page.On("button:has-text(\"3\")").Click();

        Assert.Contains("That did not work.", page.Find("[data-ui-toast-dialog]").TextContent, StringComparison.Ordinal);
        Assert.True(page.Exists("[data-ui-pagination]"));
        Assert.False(page.Exists(".rask-error-boundary"));
    }

    [Fact]
    public async Task A_select_whose_change_handler_throws_is_a_toast_and_the_page_is_still_there()
    {
        var page = Draw();

        await page.On("select").Change("25");

        Assert.Contains("That did not work.", page.Find("[data-ui-toast-dialog]").TextContent, StringComparison.Ordinal);
        Assert.Equal("ABC-123", page.TextOf("li > span"));
        Assert.False(page.Exists(".rask-error-boundary"));
    }

    private static Rask.Testing.Page<TrailerDesk> Draw() =>
        Page.RenderDocument(new TrailerDesk(), new ServiceCollection().AddSingleton<IToaster, Toaster>().BuildServiceProvider());

    private static async Task In(string language, Func<Task> act)
    {
        var before = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            await act();
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    // One trailer, a bin and the dialog it opens, a pager and a page-size select: every handler but the bin's
    // own throws what a store throws when it is down.
    private sealed partial class TrailerDesk : Component
    {
        private bool _asking;
        private bool _deleted;
        private int _attempts;

        internal bool Fails { get; set; } = true;

        protected override Component? HeadAssets => Title["trailers"];

        protected override Component? Render() =>
        [
            Main[
                P.Id("attempts")[$"attempts={_attempts}"],
                Ul[_deleted ? null : Li[Span["ABC-123"], Ui.Button.Aria("label", "Remove ABC-123").OnClick(() => _asking = true)["Bin"]]],
                Ui.Select.Value(10).OnChange(Resize)[Ui.SelectOption.Value(10)["10"], Ui.SelectOption.Value(25)["25"]],
                Ui.Pagination.Paginator(new UiPaginator { Page = 2, PerPage = 5, Total = 24 }).OnPage(Turn),
                Ui.Modal.Open(_asking).OnClose(() => _asking = false)[
                    Ui.Heading["Delete the trailer?"],
                    Ui.Button.Danger.OnClick(Delete)["Delete"]
                ]
            ],
            Ui.Toast,
        ];

        private static void Resize(int size) => throw Down();

        private static void Turn(int page) => throw Down();

        private async Task Delete()
        {
            await Task.Yield();
            _attempts++;
            if (Fails)
            {
                throw Down();
            }

            _deleted = true;
            _asking = false;
        }

        private static InvalidOperationException Down() => new("Login failed for user 'sa' with password 'hunter2'.");
    }
}
