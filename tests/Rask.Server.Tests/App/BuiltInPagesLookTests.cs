using Rask.Core;

namespace Rask.Server.Tests.App;

/// <summary>
///     The built-in 404 and 403 look like the rest of an app on the kit: the host hands Core <c>Ui.NotFound</c> and
///     <c>Ui.Forbidden</c> with its document defaults, and nothing with the kit off.
/// </summary>
public sealed class BuiltInPagesLookTests
{
    [Fact]
    public void With_the_kit_on_the_built_in_pages_are_the_kits_callouts()
    {
        var defaults = RaskDocument.For(typeof(BuiltInPagesLookTests).Assembly, kit: true);

        var notFound = defaults.NotFound!().ToHtml();
        var forbidden = defaults.Forbidden!().ToHtml();

        Assert.Contains("data-ui-callout", notFound, StringComparison.Ordinal);
        Assert.Contains("The page you requested could not be found.", notFound, StringComparison.Ordinal);
        Assert.Contains("data-ui-callout", forbidden, StringComparison.Ordinal);
        Assert.Contains("You do not have permission to view this page.", forbidden, StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_kit_off_the_built_in_pages_are_rasks_own()
    {
        var defaults = RaskDocument.For(typeof(BuiltInPagesLookTests).Assembly, kit: false);

        var looks = (defaults.NotFound, defaults.Forbidden);

        Assert.Equal((null, null), looks);
    }
}
