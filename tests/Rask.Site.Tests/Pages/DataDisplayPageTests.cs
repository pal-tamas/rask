using Rask.Core.Routing;
using Rask.Site.Features.UiKit;

namespace Rask.Site.Tests.Pages;

/// <summary>
///     The old data display page and the two showcase pages it took over when the data grid and the tree left the kit:
///     each still answers, and sends the reader on to the UI tab.
/// </summary>
public sealed class DataDisplayPageTests
{
    [Fact]
    public void The_old_data_display_page_answers_the_tree_page_too_and_its_own_url_first()
    {
        var declared = Routes(typeof(UiKitDataDisplayPage));

        Assert.Equal(["ui/data-display", "ui/tree"], declared);
    }

    [Fact]
    public void The_old_data_grid_page_answers_on_a_page_of_its_own()
    {
        var declared = Routes(typeof(UiKitDataGridPage));

        Assert.Equal(["ui/data-grid"], declared);
    }

    private static string[] Routes(Type page) =>
    [
        .. page.GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(a => a.Template.Trim('/'))
    ];
}
