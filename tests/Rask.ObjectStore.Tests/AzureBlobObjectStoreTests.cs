using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace Rask.ObjectStore.Tests;

public class AzureBlobObjectStoreTests
{
    private const string Sas = "sv=2020-02-10&sr=c&sig=abc123";

    private static (AzureBlobObjectStore Store, RecordingHandler Handler) Create(string? sas = Sas)
    {
        var handler = new RecordingHandler();
        var store = new AzureBlobObjectStore(
            new HttpClient(handler),
            sas is null
                ? new InMemoryObjectStoreCredentials()
                : new InMemoryObjectStoreCredentials(new ObjectStoreCredential(SasToken: sas)),
            Options.Create(new ObjectStoreOptions
            {
                ServiceUrl = new Uri("https://acct.blob.core.windows.net"),
                Bucket = "data",
            }));

        return (store, handler);
    }

    [Fact]
    public async Task GetRange_AppendsTheSasAndAsksForAnInclusiveRange()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.PartialContent, "0123456789");

        await store.GetRangeAsync("db/app.sqlite", 0, 10);

        Assert.Equal(
            "https://acct.blob.core.windows.net/data/db/app.sqlite?sv=2020-02-10&sr=c&sig=abc123",
            handler.Last.Uri.ToString());
        Assert.Equal("bytes=0-9", handler.Last.Header("range"));
    }

    // A SAS is often copied straight out of the portal with its leading '?' attached; pasting that as-is
    // would otherwise produce "?...?sv=" and fail as a malformed signature rather than a bad paste.
    [Fact]
    public async Task LeadingQuestionMark_OnTheSas_IsTolerated()
    {
        var (store, handler) = Create("?" + Sas);
        handler.Respond(HttpStatusCode.PartialContent, "x");

        await store.GetRangeAsync("k", 0, 1);

        Assert.Equal($"https://acct.blob.core.windows.net/data/k?{Sas}", handler.Last.Uri.ToString());
    }

    // Azure refuses a write with no blob type, and the error names neither the header nor the fix.
    [Fact]
    public async Task Put_DeclaresTheBlobType()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.Created);

        await store.PutAsync("ops/1", "hello"u8.ToArray());

        Assert.Equal("BlockBlob", handler.Last.Header("x-ms-blob-type"));
        Assert.Equal("hello", Encoding.UTF8.GetString(handler.Last.Body));
    }

    [Fact]
    public async Task TryCreate_SendsIfNoneMatch_AndReportsTheWinner()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.Created);

        Assert.True(await store.TryCreateAsync("lock", "me"u8.ToArray()));
        Assert.Equal("*", handler.Last.Header("if-none-match"));
    }

    [Fact]
    public async Task TryCreate_ReportsLoser_WhenTheBlobExists()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.PreconditionFailed);

        Assert.False(await store.TryCreateAsync("lock", "me"u8.ToArray()));
    }

    [Fact]
    public async Task GetRange_ReturnsNull_WhenBlobMissing()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await store.GetRangeAsync("nope", 0, 10));
    }

    [Fact]
    public async Task Delete_IsANonEventWhenTheBlobIsMissing()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.NotFound);

        await store.DeleteAsync("nope");
    }

    [Fact]
    public async Task List_ParsesTheContainerListing()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.OK, ListXml(null, ("ops/1", 12), ("ops/2", 34)));

        var entries = await store.ListAsync("ops/");

        Assert.Collection(
            entries,
            e =>
            {
                Assert.Equal("ops/1", e.Key);
                Assert.Equal(12, e.Size);
                Assert.Equal("abc", e.ETag);
            },
            e => Assert.Equal("ops/2", e.Key));

        Assert.Contains("restype=container", handler.Last.Uri.Query);
        Assert.Contains("comp=list", handler.Last.Uri.Query);
    }

    [Fact]
    public async Task List_FollowsNextMarker()
    {
        var (store, handler) = Create();
        handler
            .Respond(HttpStatusCode.OK, ListXml("page-2", ("ops/1", 1)))
            .Respond(HttpStatusCode.OK, ListXml(null, ("ops/2", 2)));

        var entries = await store.ListAsync("ops/");

        Assert.Equal(["ops/1", "ops/2"], entries.Select(e => e.Key));
        Assert.Contains("marker=page-2", handler.Last.Uri.Query);
    }

    [Fact]
    public async Task MissingSas_FailsWithAnActionableMessage()
    {
        var (store, _) = Create(sas: null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetRangeAsync("k", 0, 1));

        Assert.Contains("InMemoryObjectStoreCredentials.Set", ex.Message);
    }

    private static string ListXml(string? nextMarker, params (string Name, int Size)[] blobs)
    {
        var items = string.Concat(blobs.Select(b =>
            $"""
             <Blob>
               <Name>{b.Name}</Name>
               <Properties>
                 <Last-Modified>2026-08-08T12:00:00.0000000Z</Last-Modified>
                 <Etag>&quot;abc&quot;</Etag>
                 <Content-Length>{b.Size}</Content-Length>
               </Properties>
             </Blob>
             """));

        return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <EnumerationResults>
                  <Blobs>{items}</Blobs>
                  <NextMarker>{nextMarker}</NextMarker>
                </EnumerationResults>
                """;
    }
}
