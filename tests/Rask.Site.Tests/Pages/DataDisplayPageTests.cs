using Rask.Core.Routing;
using Rask.Site.Features.UiKit;

namespace Rask.Site.Tests.Pages;

/// <summary>
///     The data display showcase, and the two showcase pages it took over when the data grid and the tree left the kit.
/// </summary>
public sealed class DataDisplayPageTests
{
    [Fact]
    public void It_answers_the_old_grid_and_tree_pages_and_its_own_url_first()
    {
        var declared = typeof(UiKitDataDisplayPage)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: false)
            .Cast<RouteAttribute>()
            .Select(a => a.Template.Trim('/'))
            .ToArray();

        Assert.Equal(["ui/data-display", "ui/data-grid", "ui/tree"], declared);
    }
}
