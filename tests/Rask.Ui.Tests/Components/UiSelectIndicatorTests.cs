using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>Flux's <c>indicator="checkbox"</c>: a listbox whose rows carry a checkbox in place of the checkmark.</summary>
public partial class UiSelectIndicatorTests : global::Rask.Core.RaskMarkup
{
    private ICollection<string> _picked = ["Design services"];

    private Component Industries(bool checkbox)
    {
        var select = Ui.Select.Values(_picked).OnChange(values => _picked = values).Listbox.Multiple().Placeholder("Choose industries...");

        return (checkbox ? select.Checkbox : select)[
            Ui.SelectOption["Photography"], Ui.SelectOption["Design services"], Ui.SelectOption["Web development"]
        ];
    }

    [Fact]
    public void Every_row_of_a_checkbox_listbox_holds_a_box_where_the_checkmark_was()
    {
        var page = Page.Render(() => Industries(checkbox: true));

        var rows = page.FindAll("[data-ui-option]");

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Contains(row.DescendantsAndSelf(), node => node.HasClass("rounded-[.3rem]") && node.HasClass("size-[1.125rem]")));
        Assert.All(rows, row => Assert.DoesNotContain(row.DescendantsAndSelf(), node => node.HasClass("group-data-selected/option:block") && node.HasClass("size-5")));
    }

    [Fact]
    public void The_box_is_filled_with_the_accent_in_a_picked_row_only()
    {
        var page = Page.Render(() => Industries(checkbox: true));

        var picked = page.FindAll("[data-ui-option][data-selected]");

        Assert.Equal("Design services", Assert.Single(picked).TextContent.Trim());
        Assert.Contains(picked[0].DescendantsAndSelf(), node => node.HasClass("group-data-selected/option:bg-fx-accent"));
    }

    [Fact]
    public async Task Picking_two_rows_ticks_both_boxes()
    {
        _picked = [];
        var page = Page.Render(() => Industries(checkbox: true));

        await page.On("[data-ui-option]:has-text(\"Photography\")").Click();
        await page.On("[data-ui-option]:has-text(\"Web development\")").Click();

        Assert.Equal(["Photography", "Web development"], _picked);
        Assert.Equal(2, page.FindAll("[data-ui-option][data-selected]").Count);
    }

    [Fact]
    public void A_listbox_left_to_its_default_keeps_the_checkmark()
    {
        var page = Page.Render(() => Industries(checkbox: false));

        var rows = page.FindAll("[data-ui-option]");

        Assert.All(rows, row => Assert.DoesNotContain(row.DescendantsAndSelf(), node => node.HasClass("rounded-[.3rem]")));
    }
}
