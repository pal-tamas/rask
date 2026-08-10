using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace Rask.ObjectStore.Tests;

public class S3ObjectStoreTests
{
    private static (S3ObjectStore Store, RecordingHandler Handler) Create(
        Action<ObjectStoreOptions>? configure = null, IObjectStoreCredentials? credentials = null)
    {
        var options = new ObjectStoreOptions
        {
            ServiceUrl = new Uri("https://s3.example.com"),
            Bucket = "my-bucket",
            Region = "us-east-1",
        };

        configure?.Invoke(options);

        var handler = new RecordingHandler();
        var store = new S3ObjectStore(
            new HttpClient(handler),
            credentials ?? new InMemoryObjectStoreCredentials(new ObjectStoreCredential("AKID", "SECRET")),
            Options.Create(options));

        return (store, handler);
    }

    [Fact]
    public async Task GetRange_RequestsAnInclusiveByteRange()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.PartialContent, "0123456789");

        await store.GetRangeAsync("db/app.sqlite", 4096, 10);

        // 10 bytes from 4096 is 4096-4105 inclusive, not 4096-4106.
        Assert.Equal("bytes=4096-4105", handler.Last.Header("range"));
    }

    [Fact]
    public async Task GetRange_BuildsPathStyleUrl_ByDefault()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.PartialContent, "x");

        await store.GetRangeAsync("db/app.sqlite", 0, 1);

        Assert.Equal("https://s3.example.com/my-bucket/db/app.sqlite", handler.Last.Uri.ToString());
    }

    [Fact]
    public async Task GetRange_BuildsVirtualHostUrl_WhenPathStyleIsOff()
    {
        var (store, handler) = Create(o => o.UsePathStyle = false);
        handler.Respond(HttpStatusCode.PartialContent, "x");

        await store.GetRangeAsync("db/app.sqlite", 0, 1);

        Assert.Equal("https://my-bucket.s3.example.com/db/app.sqlite", handler.Last.Uri.ToString());
    }

    [Fact]
    public async Task GetRange_ReturnsNull_WhenObjectMissing()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await store.GetRangeAsync("nope", 0, 10));
    }

    // The object exists, the range simply starts past its end. Answering null would say it isn't there,
    // which a caller walking an append-only log would read as "the log is gone" rather than "no new bytes".
    [Fact]
    public async Task GetRange_ReturnsEmpty_WhenRangeIsPastTheEnd()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.RequestedRangeNotSatisfiable);

        Assert.Empty((await store.GetRangeAsync("ops/1", 9999, 10))!);
    }

    [Fact]
    public async Task GetRange_ReturnsEmpty_WithoutCallingOut_ForZeroCount()
    {
        var (store, handler) = Create();

        Assert.Empty((await store.GetRangeAsync("ops/1", 0, 0))!);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OpenRead_ReturnsNull_WhenObjectMissing()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.NotFound);

        Assert.Null(await store.OpenReadAsync("nope"));
    }

    [Fact]
    public async Task OpenRead_StreamsTheBody()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.OK, "snapshot-bytes");

        await using var stream = await store.OpenReadAsync("snapshots/1");
        using var reader = new StreamReader(stream!);

        Assert.Equal("snapshot-bytes", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Put_SendsTheBytes()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.OK);

        await store.PutAsync("ops/7", "hello"u8.ToArray());

        Assert.Equal(HttpMethod.Put, handler.Last.Method);
        Assert.Equal("hello", Encoding.UTF8.GetString(handler.Last.Body));
    }

    [Fact]
    public async Task PutStream_SetsContentLength_SoTheBodyIsNotChunked()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.OK, date: DateTimeOffset.UtcNow).Respond(HttpStatusCode.OK);

        using var content = new MemoryStream("streamed"u8.ToArray());
        await store.PutAsync("snapshots/2", content, content.Length);

        Assert.Equal("8", handler.Last.Header("content-length"));
        Assert.Equal("streamed", Encoding.UTF8.GetString(handler.Last.Body));
    }

    [Fact]
    public async Task TryCreate_SendsIfNoneMatch_AndReportsTheWinner()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.OK);

        Assert.True(await store.TryCreateAsync("compaction/round-4.lock", "me"u8.ToArray()));
        Assert.Equal("*", handler.Last.Header("if-none-match"));
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task TryCreate_ReportsLoser_WhenTheObjectAlreadyExists(HttpStatusCode status)
    {
        var (store, handler) = Create();
        handler.Respond(status);

        Assert.False(await store.TryCreateAsync("compaction/round-4.lock", "me"u8.ToArray()));
    }

    [Fact]
    public async Task Delete_IsANonEventWhenTheObjectIsMissing()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.NotFound);

        await store.DeleteAsync("nope");
    }

    [Fact]
    public async Task List_ParsesEntries()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.OK, ListXml(false, ("ops/1", 12), ("ops/2", 34)));

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
    }

    // A caller that pages itself would silently see only the first 1,000 keys — which for an op-log means
    // quietly losing everything a client wrote after that.
    [Fact]
    public async Task List_FollowsContinuationTokens()
    {
        var (store, handler) = Create();
        handler
            .Respond(HttpStatusCode.OK, ListXml(true, ("ops/1", 1)))
            .Respond(HttpStatusCode.OK, ListXml(false, ("ops/2", 2)));

        var entries = await store.ListAsync("ops/");

        Assert.Equal(["ops/1", "ops/2"], entries.Select(e => e.Key));
        Assert.Contains("continuation-token=next-page", handler.Last.Uri.Query);
    }

    [Fact]
    public async Task List_SendsPrefixAndListTypeTwo()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.OK, ListXml(false));

        await store.ListAsync("clients/a/ops/");

        Assert.Contains("list-type=2", handler.Last.Uri.Query);
        Assert.Contains("prefix=clients%2Fa%2Fops%2F", handler.Last.Uri.Query);
    }

    // A client that remembers the last key it read resumes from there instead of re-reading the whole log.
    // S3 does this server-side, so the objects never leave the bucket.
    [Fact]
    public async Task List_ResumesFromStartAfter_ServerSide()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.OK, ListXml(false, ("ops/9", 1)));

        await store.ListAsync("ops/", startAfter: "ops/5");

        Assert.Contains("start-after=ops%2F5", handler.Last.Uri.Query);
    }

    // Sending both would be contradictory: a continuation token already resumes where the service stopped.
    [Fact]
    public async Task List_DropsStartAfter_OnceContinuing()
    {
        var (store, handler) = Create();
        handler
            .Respond(HttpStatusCode.OK, ListXml(true, ("ops/6", 1)))
            .Respond(HttpStatusCode.OK, ListXml(false, ("ops/7", 1)));

        await store.ListAsync("ops/", startAfter: "ops/5");

        Assert.Contains("continuation-token=next-page", handler.Last.Uri.Query);
        Assert.DoesNotContain("start-after", handler.Last.Uri.Query);
    }

    [Fact]
    public async Task EveryRequest_IsSigned()
    {
        var (store, handler) = Create();
        handler.Respond(HttpStatusCode.OK, ListXml(false));

        await store.ListAsync("ops/");

        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=AKID/", handler.Last.Header("authorization"));
        Assert.Equal("UNSIGNED-PAYLOAD", handler.Last.Header("x-amz-content-sha256"));
    }

    [Fact]
    public async Task MissingCredential_FailsWithAnActionableMessage()
    {
        var (store, _) = Create(credentials: new InMemoryObjectStoreCredentials());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => store.GetRangeAsync("k", 0, 1));

        Assert.Contains("InMemoryObjectStoreCredentials.Set", ex.Message);
    }

    // A device whose clock is wrong gets its signature rejected with a 403 that says nothing about time.
    // The service's own Date is the authority, so one corrected retry turns a dead end into a hiccup.
    [Fact]
    public async Task SkewedClock_IsCorrectedFromTheServiceDate_AndTheRequestRetried()
    {
        var (store, handler) = Create();
        var serverTime = DateTimeOffset.UtcNow.AddHours(-30);
        handler
            .Respond(HttpStatusCode.Forbidden, date: serverTime)
            .Respond(HttpStatusCode.PartialContent, "recovered");

        var bytes = await store.GetRangeAsync("ops/1", 0, 9);

        Assert.Equal("recovered", Encoding.UTF8.GetString(bytes!));
        Assert.Equal(2, handler.Requests.Count);

        // The retry signs against the service's clock, not the local one.
        Assert.Equal(
            serverTime.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'")[..8],
            handler.Last.Header("x-amz-date")![..8]);
    }

    [Fact]
    public async Task GenuineAuthFailure_IsNotRetriedForever()
    {
        var (store, handler) = Create();
        handler
            .Respond(HttpStatusCode.Forbidden, date: DateTimeOffset.UtcNow)
            .Respond(HttpStatusCode.Forbidden, date: DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<HttpRequestException>(() => store.GetRangeAsync("ops/1", 0, 9));

        Assert.Equal(2, handler.Requests.Count);
    }

    private static string ListXml(bool truncated, params (string Key, int Size)[] keys)
    {
        var contents = string.Concat(keys.Select(k =>
            $"""
             <Contents>
               <Key>{k.Key}</Key>
               <LastModified>2026-08-08T12:00:00.000Z</LastModified>
               <ETag>&quot;abc&quot;</ETag>
               <Size>{k.Size}</Size>
             </Contents>
             """));

        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
                  <IsTruncated>{(truncated ? "true" : "false")}</IsTruncated>
                  <NextContinuationToken>next-page</NextContinuationToken>
                  {contents}
                </ListBucketResult>
                """;
    }
}
