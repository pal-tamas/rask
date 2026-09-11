using Rask.Server.Prerender;

namespace Rask.Server.Tests.Prerender;

// A stored copy: its bytes, compressed forms, and the ETags a browser holds on to.
public class PageCacheEntryTests
{
    private static readonly DateTimeOffset Then = new(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Then.AddMinutes(5);

    [Fact]
    public void ALargeDocument_KeepsItsCompressedForms()
    {
        var document = "<html><body>" + string.Concat(Enumerable.Repeat("<p>row</p>", 400)) + "</body></html>";

        var entry = Create(document, Then, previous: null);

        Assert.NotNull(entry.Brotli);
        Assert.NotNull(entry.Gzip);
        Assert.True(entry.Gzip!.Length < entry.Identity.Length);
        Assert.Equal(entry.Identity.LongLength + entry.Brotli!.LongLength + entry.Gzip.LongLength, entry.Bytes);
    }

    [Fact]
    public void ACompressedFormThatIsNoSmaller_IsNotKept()
    {
        // A gzip header alone is larger than this document.
        var entry = Create("<p>a</p>", Then, previous: null);

        Assert.Null(entry.Gzip);
    }

    [Fact]
    public void TheETagsAreStrongAndDistinctPerEncoding()
    {
        var entry = Create("<html><body>" + new string('x', 2000) + "</body></html>", Then, previous: null);

        Assert.False(entry.IdentityTag.IsWeak);
        Assert.NotEqual(entry.IdentityTag.Tag, entry.BrotliTag!.Tag);
        Assert.NotEqual(entry.IdentityTag.Tag, entry.GzipTag!.Tag);
    }

    [Fact]
    public void ARenderOfTheSameBytes_ReusesTheCopyItReplaces()
    {
        // The ordinary refresh: nothing changed, so nothing is compressed again, the arrays are not churned,
        // and the ETag a browser holds still matches — its next visit is a 304.
        const string document = "<html><body><p>unchanged</p></body></html>";
        var first = Create(document, Then, previous: null);

        var second = Create(document, Later, previous: first);

        Assert.Same(first.Identity, second.Identity);
        Assert.Equal(first.IdentityTag.Tag, second.IdentityTag.Tag);
        Assert.Equal(Then, second.ContentChangedAt);
    }

    [Fact]
    public void ARenderOfDifferentBytes_IsANewCopy()
    {
        var first = Create("<html><body><p>one</p></body></html>", Then, previous: null);

        var second = Create("<html><body><p>two</p></body></html>", Later, previous: first);

        Assert.NotSame(first.Identity, second.Identity);
        Assert.NotEqual(first.IdentityTag.Tag, second.IdentityTag.Tag);
        Assert.Equal(Later, second.ContentChangedAt);
    }

    private static PageCacheEntry Create(string document, DateTimeOffset now, PageCacheEntry? previous) =>
        PageCacheEntry.Create(
            document, readsUser: false, renderedAt: 0, now, cssBundle: "css", jsBundle: "js", previous);
}
