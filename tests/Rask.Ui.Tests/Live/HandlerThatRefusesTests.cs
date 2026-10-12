using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Messaging;
using Rask.Wire;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Live;

/// <summary>
///     A list's bin whose confirm dialog deletes, with <c>Ui.Toast</c> in the layout: when the store refuses the
///     delete, the reader is told why in a danger toast, and the page is as the handler left it.
/// </summary>
public partial class HandlerThatRefusesTests : global::Rask.Core.RaskMarkup
{
    private const string Referenced = "The trailer cannot be deleted: a waybill refers to it.";

    [Fact]
    public async Task A_confirm_whose_delete_is_refused_shows_the_stores_sentence_in_a_danger_toast_as_a_diff()
    {
        var trailers = new Trailers(refuses: true);
        var page = trailers.Draw();
        await page.Click("Remove ABC-123");

        await page.Click("Delete");

        var toast = page.Find("[data-ui-toast-dialog]");
        Assert.Contains(Referenced, toast.TextContent, StringComparison.Ordinal);
        Assert.Equal("danger", page.Find("[data-variant]").Attribute("data-variant"));
        Assert.Empty(DiffGuardAttribute.FullPages());
    }

    [Fact]
    public async Task The_row_is_still_listed_and_the_dialog_is_as_the_handler_left_it_which_here_is_open()
    {
        var trailers = new Trailers(refuses: true);
        var page = trailers.Draw();
        await page.Click("Remove ABC-123");

        await page.Click("Delete");

        Assert.Equal("ABC-123", page.TextOf("li > span"));
        Assert.Equal("true", page.Find("dialog").Attribute("data-rask-modal-open"));
    }

    [Fact]
    public async Task The_confirm_can_be_pressed_again_and_a_delete_that_goes_through_closes_the_dialog()
    {
        var trailers = new Trailers(refuses: true);
        var page = trailers.Draw();
        await page.Click("Remove ABC-123");
        await page.Click("Delete");
        trailers.Refuses = false;

        await page.Click("Delete");

        Assert.False(page.Exists("li"));
        Assert.Equal("false", page.Find("dialog").Attribute("data-rask-modal-open"));
    }

    private sealed class Refused(string sentence) : Exception(sentence), IFieldFailures
    {
        public IReadOnlyList<FieldFailure> Failures { get; } = [new FieldFailure(sentence, [])];
    }

    // One trailer, a bin beside it, and the dialog the bin opens. The delete closes the dialog after the
    // store took it, as an app writes it.
    private sealed class Trailers(bool refuses)
    {
        private bool _asking;
        private bool _deleted;

        internal bool Refuses { get; set; } = refuses;

        internal Page Draw() =>
            Page.Render(
                () => Div[
                    Main[
                        Ul[_deleted ? null : Li[Span["ABC-123"], Ui.Button.Aria("label", "Remove ABC-123").OnClick(() => _asking = true)["Bin"]]],
                        Ui.Modal.Open(_asking).OnClose(() => _asking = false)[
                            Ui.Heading["Delete the trailer?"],
                            Ui.Button.Danger.OnClick(Delete)["Delete"]
                        ]
                    ],
                    Ui.Toast
                ],
                new ServiceCollection().AddSingleton<IToaster, Toaster>().BuildServiceProvider());

        private async Task Delete()
        {
            await Task.Yield();
            if (Refuses)
            {
                throw new Refused(Referenced);
            }

            _deleted = true;
            _asking = false;
        }
    }
}
