using Rask.Site.Tests.Infrastructure;
using Rask.Web;

namespace Rask.Site.Tests.Demos;

/// <summary>The localStorage demo, with MDN's <c>localStorage</c> faked so no browser is needed.</summary>
public sealed partial class StorageDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task Reading_shows_the_stored_value_and_how_many_keys_local_storage_holds()
    {
        using var storage = LocalStorage.Fake();
        storage.Returns(s => s.GetItem("rask.browser.storage"), "persist-me");
        storage.Returns(s => s.Length, 3);
        var page = Page.Render(() => StorageDemo, TestServices.Default());
        var read = MarkupAssert.Attrs(page.Render(), "data-rask-on-click")[1];   // Set, Read, Remove

        await page.Invoke(read, "{}");

        var html = page.Render();
        Assert.Contains("<code id=\"storage-read-value\">persist-me</code>", html, StringComparison.Ordinal);
        Assert.Contains("Read (localStorage holds 3 key(s))", html, StringComparison.Ordinal);
    }
}
