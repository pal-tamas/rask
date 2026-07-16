namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsListGroup / BsListGroupItem.
public class BsListGroupTests
{
    [Fact]
    public void ListGroup_WrapsItemsInUl() =>
        Assert.Equal(
            "<ul class=\"list-group\"><li class=\"list-group-item\">One</li></ul>",
            BsListGroup()[BsListGroupItem()["One"]].ToHtml());

    [Fact]
    public void ListGroup_Numbered_UsesOrderedList() =>
        Assert.Equal(
            "<ol class=\"list-group list-group-numbered\"><li class=\"list-group-item\">One</li></ol>",
            BsListGroup(Numbered: true)[BsListGroupItem()["One"]].ToHtml());

    [Fact]
    public void ListGroup_Flush_DropsOuterBorders() =>
        Assert.Equal(
            "<ul class=\"list-group list-group-flush\"></ul>",
            BsListGroup(Flush: true).ToHtml());

    [Fact]
    public void ListGroupItem_Active_MarksCurrent() =>
        Assert.Equal(
            "<li class=\"list-group-item active\" aria-current=\"true\">Now</li>",
            BsListGroupItem(Active: true)["Now"].ToHtml());

    [Fact]
    public void ListGroupItem_Disabled_GreysItem() =>
        Assert.Equal(
            "<li class=\"list-group-item disabled\">Off</li>",
            BsListGroupItem(Disabled: true)["Off"].ToHtml());

    [Fact]
    public void ListGroupItem_Color_TintsItem() =>
        Assert.Equal(
            "<li class=\"list-group-item list-group-item-success\">Green</li>",
            BsListGroupItem(Color: BsColor.Success)["Green"].ToHtml());

    [Fact]
    // Href turns the item into an anchor with .list-group-item-action; href is tag-specific, so it
    // serialises after class.
    public void ListGroupItem_Href_RendersActionAnchor() =>
        Assert.Equal(
            "<a class=\"list-group-item list-group-item-action\" href=\"/x\">Go</a>",
            BsListGroupItem(Href: "/x")["Go"].ToHtml());

    [Fact]
    // A linked, current item keeps both the action anchor and aria-current; aria-current precedes the
    // tag-specific href.
    public void ListGroupItem_ActiveHref_KeepsAnchorAndAriaCurrent() =>
        Assert.Equal(
            "<a class=\"list-group-item list-group-item-action active\" aria-current=\"true\" href=\"/x\">Go</a>",
            BsListGroupItem(Active: true, Href: "/x")["Go"].ToHtml());
}
