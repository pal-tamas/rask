using System.Linq;
using System.Reflection;

namespace Rask.Bootstrap.Tests;

// Enforces the library convention: Bs components WRAP the core components and never subclass Element
// to mint a new element type. See BsBlock for the full convention.
public class BsArchitectureTests
{
    private static readonly Assembly Lib = typeof(BsButton).Assembly;

    [Fact]
    public void NoComponent_SubclassesElementDirectly()
    {
        var offenders = Lib.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(global::Rask.Core.Component).IsAssignableFrom(t))
            .Where(t => typeof(global::Rask.Core.Element).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These Rask.Bootstrap components subclass Element instead of wrapping a core component: "
            + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData(BsColor.Primary, "primary")]
    [InlineData(BsColor.Secondary, "secondary")]
    [InlineData(BsColor.Success, "success")]
    [InlineData(BsColor.Danger, "danger")]
    [InlineData(BsColor.Warning, "warning")]
    [InlineData(BsColor.Info, "info")]
    [InlineData(BsColor.Light, "light")]
    [InlineData(BsColor.Dark, "dark")]
    public void Button_EmitsEveryColor(BsColor color, string infix) =>
        Assert.Equal(
            $"<button class=\"btn btn-{infix}\" type=\"button\">x</button>",
            BsButton(Color: color)["x"].ToHtml());

    [Theory]
    [InlineData(BsColor.Primary, "alert-primary")]
    [InlineData(BsColor.Danger, "alert-danger")]
    public void Alert_EmitsColorClass(BsColor color, string cls) =>
        Assert.Contains(cls, BsAlert(Color: color)["x"].ToHtml());
}
