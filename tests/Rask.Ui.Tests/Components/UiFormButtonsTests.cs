using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <see cref="UiFormButtons" />: a form's Delete, Cancel back to the list and Save, as Flux draws a form's footer.
/// </summary>
public partial class UiFormButtonsTests : RaskMarkup
{
    private static readonly RouteUrl Suppliers = new("/suppliers");

    [Fact]
    public void A_form_footer_is_cancel_back_to_the_list_then_save_at_the_end()
    {
        var footer = Ui.FormButtons.Back(Suppliers);

        var html = footer.ToHtml().AsText();

        Assert.StartsWith("<div class=\"[:where(&)]:flex [:where(&)]:gap-2\" data-ui-form-buttons=\"\"><div class=\"flex-1\" data-ui-spacer=\"\"></div>", html, StringComparison.Ordinal);
        Assert.Matches("<a [^>]*href=\"/suppliers\"[^>]*>Cancel</a>", html);
        Assert.Matches("<button [^>]*data-rask-key=\"save\" type=\"submit\">.*>Save</span></button></div>$", html);
        Assert.DoesNotContain("data-rask-confirm", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Remove_draws_delete_at_the_start_which_asks_its_question_under_its_heading_first()
    {
        var footer = Ui.FormButtons.Back(Suppliers).Remove(() => Task.CompletedTask, "Delete supplier", "Delete Aminoff Freight?");

        var html = footer.ToHtml().AsText();

        var delete = html[html.IndexOf("<button", StringComparison.Ordinal)..html.IndexOf("data-ui-spacer", StringComparison.Ordinal)];
        Assert.Contains("data-rask-confirm=\"Delete Aminoff Freight?\"", delete, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-heading=\"Delete supplier\"", delete, StringComparison.Ordinal);
        Assert.Contains(">Delete</span></button>", delete, StringComparison.Ordinal);
        Assert.Contains("bg-red-500", delete, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hungarian_reader_reads_the_catalogs_words_and_question()
    {
        var footer = () => Ui.FormButtons.Back(Suppliers).Remove(() => { }).ToHtml().AsText();

        var html = KitCulture.In("hu-HU", footer);

        Assert.Contains(">Törlés</span></button>", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm=\"Biztos törölni szeretné?\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-confirm-heading=\"Törlés\"", html, StringComparison.Ordinal);
        Assert.Contains(">Mégse</a>", html, StringComparison.Ordinal);
        Assert.Contains(">Mentés</span></button>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_record_names_its_own_buttons_and_an_unnamed_one_reads_the_catalog()
    {
        var created = Ui.FormButtons.Back(Suppliers).Save("Létrehozás").Cancel("Vissza");

        var html = created.ToHtml().AsText();
        var hungarian = KitCulture.In("hu-HU", () => Ui.FormButtons.Back(Suppliers).Save("Létrehozás").ToHtml().AsText());

        Assert.Contains(">Vissza</a>", html, StringComparison.Ordinal);
        Assert.Contains(">Létrehozás</span></button>", html, StringComparison.Ordinal);
        Assert.Contains(">Mégse</a>", hungarian, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_that_cannot_be_deleted_has_no_delete_button()
    {
        Func<Task>? remove = null;

        var html = Ui.FormButtons.Back(Suppliers).Remove(remove, "Delete supplier").ToHtml();

        Assert.DoesNotContain("Delete", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-confirm", html, StringComparison.Ordinal);
    }

    [Fact]
    public void What_the_page_adds_stands_before_cancel()
    {
        var footer = Ui.FormButtons.Back(Suppliers)[Ui.Button.Key("copy")["Save as new"]];

        var html = footer.ToHtml();

        Assert.True(html.IndexOf("Save as new", StringComparison.Ordinal) < html.IndexOf(">Cancel<", StringComparison.Ordinal));
        Assert.True(html.IndexOf("data-ui-spacer", StringComparison.Ordinal) < html.IndexOf("Save as new", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Yes_runs_the_pages_delete_and_a_render_without_one_takes_the_button_away()
    {
        var deleted = 0;
        var removable = true;
        var page = Page.Render(() =>
        [
            Ui.FormButtons.Back(Suppliers).Remove(removable ? () => { deleted++; removable = false; } : null),
        ]);

        await page.On("[data-ui-form-buttons] button[data-rask-confirm]").Click();

        Assert.Equal(1, deleted);
        Assert.False(page.Exists("[data-rask-confirm]"));
        Assert.True(page.Exists("button[type=\"submit\"]"));
    }
}
