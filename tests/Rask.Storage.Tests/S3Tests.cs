using System.Net;
using System.Text;
using Rask.Storage.Backends;

namespace Rask.Storage.Tests;

// SigV4 is either exactly right or completely broken: a signature one byte off is rejected the same way as none,
// and the service never says which part was wrong. So it is pinned three independent ways — AWS's own published
// worked examples, vectors produced by a separate Python implementation of the specification (salvaged with the
// withdrawn Rask.ObjectStore), and the rules the specification calls out by name.
public sealed class SigV4Tests
{
    private static readonly S3Credential Credential =
        new("AKIAIOSFODNN7EXAMPLE", "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", null);

    private static readonly DateTimeOffset SigningTime = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset AwsExampleTime = new(2013, 5, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Aws_presigned_url_example()
    {
        // "Authenticating Requests: Using Query Parameters (AWS Signature Version 4)".
        var url = SigV4.Presign("https", "examplebucket.s3.amazonaws.com", ["test.txt"], [], Credential, "us-east-1",
            AwsExampleTime, TimeSpan.FromSeconds(86400));

        Assert.Equal(
            "https://examplebucket.s3.amazonaws.com/test.txt?X-Amz-Algorithm=AWS4-HMAC-SHA256"
            + "&X-Amz-Credential=AKIAIOSFODNN7EXAMPLE%2F20130524%2Fus-east-1%2Fs3%2Faws4_request"
            + "&X-Amz-Date=20130524T000000Z&X-Amz-Expires=86400&X-Amz-SignedHeaders=host"
            + "&X-Amz-Signature=aeeed9bbccd4d02ee5c0109b86d86835f995330da4c265957d157751f604d404",
            url);
    }

    [Fact]
    public void Aws_get_object_header_example()
    {
        // "Signature Calculations for the Authorization Header", Example: GET Object — which signs the empty
        // payload hash rather than UNSIGNED-PAYLOAD.
        var request = new HttpRequestMessage(HttpMethod.Get, "https://examplebucket.s3.amazonaws.com/test.txt");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-9");

        SigV4.SignHeaders(request, "/test.txt", "", Credential, "us-east-1", AwsExampleTime,
            payloadHash: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20130524/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, "
            + "Signature=f0e8bdb87c964420e857bd35b5d6ed310bd44f0170aba48dd91039c6036bdb41",
            request.Headers.Authorization!.ToString());
    }

    [Fact]
    public void Ranged_get_matches_the_independent_implementation() =>
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260808/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, "
            + "Signature=fbd35e1217a0041e926b279c154137260459f0ee9d730cb2e74eeb9de5202079",
            Sign(["my-bucket", "db", "app v2.sqlite"], [], ("Range", "bytes=0-9")));

    [Fact]
    public void List_matches_the_independent_implementation() =>
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260808/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;x-amz-content-sha256;x-amz-date, "
            + "Signature=476ba5a0aad4be774613ff528bfc2c3ad249028e318185694235f9fbe1ff1cc2",
            Sign(["my-bucket", ""], [("list-type", "2"), ("prefix", "ops/")]));

    [Fact]
    public void Slashes_in_a_key_stay_separators() =>
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260808/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;x-amz-content-sha256;x-amz-date, "
            + "Signature=a492587f6a42ef0c5ed320d4e4246fb2f859cc6687ab771f1990ab13bc585b9a",
            Sign(["b", "x", "y", "z.txt"], []));

    [Fact]
    public void Unreserved_characters_are_never_escaped() =>
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260808/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;x-amz-content-sha256;x-amz-date, "
            + "Signature=b4cc88f31dafd52dacb65fcbafebb55d2bc61d305e8f258a9981632a3611079b",
            Sign(["b", "aZ0-._~"], []));

    [Fact]
    public void Query_order_does_not_change_the_signature() =>
        Assert.Equal(
            Sign(["b", ""], [("prefix", "a"), ("list-type", "2")]),
            Sign(["b", ""], [("list-type", "2"), ("prefix", "a")]));

    [Fact]
    public void A_disposition_with_reserved_characters_signs_the_query_it_sends()
    {
        // The regression the withdrawn client had: rebuilding the query from an unescaped URI split this value
        // at its '&', so the service verified a different query than the one it received.
        const string disposition = "attachment; filename=\"a&b=c.pdf\"; filename*=UTF-8''%C3%A1rv%C3%ADz%26t%C5%B1r%C5%91.pdf";
        var url = SigV4.Presign("https", "s3.example.com", ["b", "ab", "k"],
            [new("response-content-disposition", disposition), new("response-content-type", "application/pdf")],
            Credential, "us-east-1", SigningTime, TimeSpan.FromMinutes(5));

        var query = url[(url.IndexOf('?') + 1)..url.IndexOf("&X-Amz-Signature=", StringComparison.Ordinal)];
        var pairs = query.Split('&').Select(p => p.Split('=')).ToList();

        Assert.All(pairs, p => Assert.Equal(2, p.Length));
        var decoded = pairs.Select(p => new KeyValuePair<string, string>(Uri.UnescapeDataString(p[0]), Uri.UnescapeDataString(p[1]))).ToList();
        Assert.Equal(disposition, decoded.Single(p => p.Key == "response-content-disposition").Value);
        Assert.Equal(query, SigV4.CanonicalQuery(decoded));
    }

    [Fact]
    public void A_session_token_is_signed_in_both_forms()
    {
        var temporary = new S3Credential("AKID", "SECRET", "session-token");

        var request = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/k");
        SigV4.SignHeaders(request, "/b/k", "", temporary, "us-east-1", SigningTime);
        Assert.Contains("x-amz-security-token", request.Headers.Authorization!.Parameter);

        var url = SigV4.Presign("https", "s3.example.com", ["b", "k"], [], temporary, "us-east-1", SigningTime, TimeSpan.FromMinutes(1));
        Assert.Contains("X-Amz-Security-Token=session-token", url);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7 * 24 * 60 * 60 + 1)]
    public void A_presigned_url_lasts_one_second_to_seven_days(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SigV4.Presign("https", "s3.example.com", ["b", "k"], [], Credential, "us-east-1", SigningTime, TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void The_region_and_port_are_part_of_the_signature()
    {
        Assert.NotEqual(Sign(["b", "k"], []), Sign(["b", "k"], [], region: "eu-west-2"));
        Assert.NotEqual(Sign(["b", "k"], []), Sign(["b", "k"], [], host: "s3.example.com:9000"));
    }

    [Fact]
    public void A_credential_never_prints_its_secret() =>
        Assert.DoesNotContain("EXAMPLEKEY", Credential.ToString());

    private static string Sign(string[] segments, (string Name, string Value)[] query, (string, string)? header = null,
        string region = "us-east-1", string host = "s3.example.com")
    {
        var path = SigV4.CanonicalPath(segments);
        var canonicalQuery = SigV4.CanonicalQuery(query.Select(p => new KeyValuePair<string, string>(p.Name, p.Value)));
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://{host}{path}{(canonicalQuery.Length > 0 ? "?" + canonicalQuery : "")}");
        if (header is var (name, value))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        SigV4.SignHeaders(request, path, canonicalQuery, Credential, region, SigningTime);
        return request.Headers.Authorization!.ToString();
    }

    private static string Sign(string[] segments, (string Name, string Value)[] query, (string Name, string Value) header) =>
        Sign(segments, query, (header.Name, header.Value), "us-east-1", "s3.example.com");
}

public sealed class S3BlobBackendTests : IDisposable
{
    private readonly string _spool = Path.Combine(Path.GetTempPath(), $"rask-s3-spool-{Guid.NewGuid():N}.tmp");

    public void Dispose() => File.Delete(_spool);

    [Fact]
    public async Task A_put_sends_the_exact_length_and_signs_the_headers_the_object_is_stored_with()
    {
        var (backend, handler) = Create();
        handler.Respond(HttpStatusCode.OK);
        await File.WriteAllBytesAsync(_spool, "hello"u8.ToArray());

        await backend.PutFileAsync("ab/abcd", _spool, 5,
            new BlobHeaders("image/png", "inline; filename=a.png", "private, no-store"), default);

        var put = handler.Last;
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://s3.example.com/my-bucket/ab/abcd", put.Uri.ToString());
        Assert.Equal("5", put.Header("content-length"));
        Assert.Equal("hello", Encoding.UTF8.GetString(put.Body));
        Assert.Equal("image/png", put.Header("content-type"));
        Assert.Equal("inline; filename=a.png", put.Header("content-disposition"));
        Assert.Contains("SignedHeaders=cache-control;content-disposition;content-type;host;x-amz-content-sha256;x-amz-date,",
            put.Header("authorization"));
        Assert.Equal("UNSIGNED-PAYLOAD", put.Header("x-amz-content-sha256"));
    }

    [Fact]
    public async Task Virtual_host_addressing_puts_the_bucket_in_the_host()
    {
        var (backend, handler) = Create(o => o.UsePathStyle = false);
        handler.Respond(HttpStatusCode.OK, "x");

        await using var _ = await backend.OpenReadAsync("ab/abcd", 0, null, default);

        Assert.Equal("https://my-bucket.s3.example.com/ab/abcd", handler.Last.Uri.ToString());
    }

    [Fact]
    public async Task A_ranged_read_asks_for_an_inclusive_range()
    {
        var (backend, handler) = Create();
        handler.Respond(HttpStatusCode.PartialContent, "0123456789");

        await using var stream = await backend.OpenReadAsync("ab/abcd", 4096, 10, default);

        Assert.Equal("bytes=4096-4105", handler.Last.Header("range"));
        Assert.Equal("0123456789", await new StreamReader(stream!).ReadToEndAsync());
    }

    [Fact]
    public async Task Missing_past_the_end_and_ignored_ranges_are_told_apart()
    {
        var (backend, handler) = Create();
        handler.Respond(HttpStatusCode.NotFound).Respond(HttpStatusCode.RequestedRangeNotSatisfiable).Respond(HttpStatusCode.OK, "whole");

        Assert.Null(await backend.OpenReadAsync("ab/none", 0, null, default));
        Assert.Equal(0, (await backend.OpenReadAsync("ab/abcd", 99, null, default))!.Length);
        await Assert.ThrowsAsync<IOException>(() => backend.OpenReadAsync("ab/abcd", 10, null, default));
    }

    [Fact]
    public async Task Deleting_something_absent_is_not_an_error()
    {
        var (backend, handler) = Create();
        handler.Respond(HttpStatusCode.NotFound).Respond(HttpStatusCode.NoContent);

        await backend.DeleteAsync("ab/none", default);
        await backend.DeleteAsync("ab/abcd", default);

        Assert.Equal(HttpMethod.Delete, handler.Last.Method);
    }

    [Fact]
    public async Task A_listing_follows_continuation_tokens()
    {
        var (backend, handler) = Create();
        handler
            .Respond(HttpStatusCode.OK, ListXml(true, ("app/ab/one", 12)))
            .Respond(HttpStatusCode.OK, ListXml(false, ("app/cd/two", 34)));

        var entries = await backend.ListAsync("app/", default).ToListAsync();

        Assert.Equal(["app/ab/one", "app/cd/two"], entries.Select(e => e.Key));
        Assert.Equal(12, entries[0].Size);
        Assert.Contains("prefix=app%2F", handler.Requests[0].Uri.Query);
        Assert.Contains("continuation-token=next-page", handler.Last.Uri.Query);
    }

    [Fact]
    public async Task A_refusal_carries_the_code_and_none_of_the_body()
    {
        var (backend, handler) = Create();
        handler.Respond(HttpStatusCode.Forbidden,
            "<Error><Code>SignatureDoesNotMatch</Code><AWSAccessKeyId>AKID</AWSAccessKeyId><StringToSign>SECRET-ish</StringToSign></Error>");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => backend.DeleteAsync("ab/abcd", default));

        Assert.Contains("SignatureDoesNotMatch", ex.Message);
        Assert.Contains("Storage__", ex.Message);
        Assert.DoesNotContain("AKID", ex.Message);
        Assert.DoesNotContain("SECRET", ex.Message);
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task A_skewed_clock_is_named_as_the_cause()
    {
        var (backend, handler) = Create();
        handler.Respond(HttpStatusCode.Forbidden, "<Error><Code>RequestTimeTooSkewed</Code></Error>");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => backend.DeleteAsync("ab/abcd", default));

        Assert.Contains("clock", ex.Message);
    }

    [Fact]
    public void A_presigned_url_carries_the_safe_headers()
    {
        var (backend, _) = Create();

        Assert.True(backend.TryPresign("ab/abcd", TimeSpan.FromMinutes(5), "image/png", "inline", out var url));
        Assert.StartsWith("https://s3.example.com/my-bucket/ab/abcd?", url);
        Assert.Contains("response-content-type=image%2Fpng", url);
        Assert.Contains("X-Amz-Expires=300", url);
    }

    private static (S3BlobBackend Backend, RecordingHandler Handler) Create(Action<S3StorageOptions>? configure = null)
    {
        var options = new S3StorageOptions
        {
            ServiceUrl = new Uri("https://s3.example.com"),
            Bucket = "my-bucket",
            AccessKeyId = "AKID",
            SecretAccessKey = "SECRET",
        };
        configure?.Invoke(options);

        var handler = new RecordingHandler();
        return (new S3BlobBackend(new HttpClient(handler), options, new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero))), handler);
    }

    private static string ListXml(bool truncated, params (string Key, int Size)[] keys) =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
          <IsTruncated>{(truncated ? "true" : "false")}</IsTruncated>
          <NextContinuationToken>next-page</NextContinuationToken>
          {string.Concat(keys.Select(k => $"<Contents><Key>{k.Key}</Key><LastModified>2026-08-08T12:00:00.000Z</LastModified><Size>{k.Size}</Size></Contents>"))}
        </ListBucketResult>
        """;
}
