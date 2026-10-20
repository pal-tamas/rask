namespace Rask.Core.Tests.Live;

// TA: a canvas whose script set its width and height lost both on the next render that morphed around it. A patch
// never removes an attribute the server never wrote; what the server did write, and then leaves out, still goes.
// Runs the production rask-morph.ts in a Node subprocess; PageWrittenAttributesTests holds it in a browser.
public sealed class PageAddedAttributeTests
{
    [Fact]
    public void A_morph_keeps_an_attribute_the_page_added_and_removes_the_ones_the_server_wrote_and_dropped()
    {
        // No node on PATH: the browser test in Rask.Server.E2E.Tests holds the same rule.
        var result = NodeFixture.Run("PageAddedAttributeFixture");
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        Assert.True(root.GetProperty("sizeKept").GetBoolean(), "the width the page set was removed");
        Assert.True(root.GetProperty("titleRemoved").GetBoolean(), "the title the server dropped stayed");
        Assert.True(root.GetProperty("hookAttributeRemoved").GetBoolean(), "a dropped data-rask-on-click stayed");
        Assert.True(root.GetProperty("ownedKept").GetBoolean(), "an owned attribute was removed");
    }

    [Fact]
    public void An_attribute_the_page_added_is_the_servers_to_remove_once_a_render_has_written_it()
    {
        var result = NodeFixture.Run("PageAddedAttributeFixture");
        if (result is null)
        {
            return;
        }

        var root = result.Value;

        Assert.Equal("server", root.GetProperty("serverValue").GetString());
        Assert.True(root.GetProperty("writtenThenRemoved").GetBoolean(), "an attribute the server wrote then dropped stayed");
    }
}
