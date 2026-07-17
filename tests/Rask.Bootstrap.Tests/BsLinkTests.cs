namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsLink — an anchor styled as a Bootstrap button. Attribute order follows
// the framework rule (class, then aria-*, then tag-specific href/target/rel).
public class BsLinkTests
{
    [Fact]
    public void Link_NoColor_IsBareButtonAnchor() =>
        Assert.Equal(
            "<a class=\"btn\" href=\"/x\">Go</a>",
            BsLink(Href: "/x")["Go"].ToHtml());

    [Fact]
    public void Link_Color_AddsThemeClass() =>
        Assert.Equal(
            "<a class=\"btn btn-primary\" href=\"/x\">Go</a>",
            BsLink(Href: "/x", Color: BsColor.Primary)["Go"].ToHtml());

    [Fact]
    public void Link_Outline_UsesOutlineClass() =>
        Assert.Equal(
            "<a class=\"btn btn-outline-light btn-sm\" href=\"/x\">Go</a>",
            BsLink(Href: "/x", Color: BsColor.Light, Outline: true, Size: BsSize.Sm)["Go"].ToHtml());

    [Fact]
    // target/rel are tag-specific, so they serialise after class in A's declared order (href, target, rel).
    public void Link_TargetRel_SerialiseAfterClass() =>
        Assert.Equal(
            "<a class=\"btn btn-primary\" href=\"https://example.com\" target=\"_blank\" rel=\"noopener\">GitHub</a>",
            BsLink(Href: "https://example.com", Target: "_blank", Rel: "noopener", Color: BsColor.Primary)["GitHub"]
                .ToHtml());

    [Fact]
    // Active adds .active plus aria-pressed="true"; aria-* precedes the tag-specific href.
    public void Link_Active_MarksPressed() =>
        Assert.Equal(
            "<a class=\"btn btn-primary active\" aria-pressed=\"true\" href=\"/x\">Go</a>",
            BsLink(Href: "/x", Color: BsColor.Primary, Active: true)["Go"].ToHtml());

    [Fact]
    // Size with no color still emits the size class after .btn.
    public void Link_SizeOnly_EmitsSizeClass() =>
        Assert.Equal(
            "<a class=\"btn btn-lg\" href=\"/x\">Go</a>",
            BsLink(Href: "/x", Size: BsSize.Lg)["Go"].ToHtml());
}
