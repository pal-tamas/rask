using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Rask.Storage.Backends;

namespace Rask.Storage.Tests;

public sealed class AzureAccountTests
{
    private const string Key = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    [Fact]
    public void An_account_key_connection_string_signs_against_the_public_endpoint()
    {
        var account = AzureAccount.Parse($"DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey={Key};EndpointSuffix=core.windows.net");

        Assert.Equal("https://myaccount.blob.core.windows.net", account.BlobEndpoint);
        Assert.Equal("myaccount", account.AccountName);
        Assert.True(account.CanSign);
        Assert.Null(account.SharedAccessSignature);
    }

    [Fact]
    public void Names_are_case_insensitive_and_values_keep_their_equals_signs()
    {
        var account = AzureAccount.Parse($"accountname=myaccount;ACCOUNTKEY={Key}");

        Assert.Equal(Convert.FromBase64String(Key), account.AccountKey);
        Assert.Equal("https://myaccount.blob.core.windows.net", account.BlobEndpoint);
    }

    [Fact]
    public void A_sas_connection_string_cannot_sign()
    {
        var account = AzureAccount.Parse("BlobEndpoint=https://myaccount.blob.core.windows.net/;SharedAccessSignature=?sv=2024-11-04&sig=abc%3D");

        Assert.False(account.CanSign);
        Assert.Equal("sv=2024-11-04&sig=abc%3D", account.SharedAccessSignature);
        Assert.Equal("https://myaccount.blob.core.windows.net", account.BlobEndpoint);
        Assert.Equal("myaccount", account.AccountName);
    }

    [Fact]
    public void Development_storage_is_azurite()
    {
        var account = AzureAccount.Parse("UseDevelopmentStorage=true");

        Assert.Equal("http://127.0.0.1:10000/devstoreaccount1", account.BlobEndpoint);
        Assert.Equal("devstoreaccount1", account.AccountName);
        Assert.True(account.CanSign);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AccountName=myaccount")]
    [InlineData("AccountKey=c2VjcmV0")]
    [InlineData("AccountName=myaccount;AccountKey=not*base64")]
    [InlineData("BlobEndpoint=ftp://x;SharedAccessSignature=sig")]
    [InlineData("garbage")]
    public void A_bad_connection_string_names_the_setting_and_never_the_value(string connectionString)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AzureAccount.Parse(connectionString));

        Assert.Contains("Storage__Azure__ConnectionString", ex.Message);
        Assert.DoesNotContain("c2VjcmV0", ex.Message);
        Assert.DoesNotContain("not*base64", ex.Message);
    }

    [Fact]
    public void An_account_never_prints_its_key() =>
        Assert.DoesNotContain("Eby8", AzureAccount.Parse("UseDevelopmentStorage=true").ToString());
}

public sealed class AzureSigningTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Date = Now.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);

    [Fact]
    public void A_put_string_to_sign_has_the_length_the_blob_headers_and_the_emulators_doubled_account()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "http://127.0.0.1:10000/devstoreaccount1/files/ab/abcd")
        {
            Content = new ByteArrayContent([1, 2, 3, 4, 5]),
        };
        request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
        request.Headers.TryAddWithoutValidation("x-ms-blob-content-type", "image/png");
        request.Headers.TryAddWithoutValidation("x-ms-date", Date);
        request.Headers.TryAddWithoutValidation("x-ms-version", AzureSharedKey.Version);

        Assert.Equal(
            "PUT\n"         // verb
            + "\n"          // Content-Encoding
            + "\n"          // Content-Language
            + "5\n"         // Content-Length
            + "\n"          // Content-MD5
            + "\n"          // Content-Type
            + "\n"          // Date (x-ms-date is sent instead)
            + "\n"          // If-Modified-Since
            + "\n"          // If-Match
            + "\n"          // If-None-Match
            + "\n"          // If-Unmodified-Since
            + "\n"          // Range
            + "x-ms-blob-content-type:image/png\n"
            + "x-ms-blob-type:BlockBlob\n"
            + $"x-ms-date:{Date}\n"
            + $"x-ms-version:{AzureSharedKey.Version}\n"
            + "/devstoreaccount1/devstoreaccount1/files/ab/abcd",
            AzureSharedKey.StringToSign(request, "devstoreaccount1"));
    }

    [Fact]
    public void An_empty_body_signs_an_empty_length_and_query_parameters_are_sorted_decoded_lines()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "https://myaccount.blob.core.windows.net/files?restype=container&comp=list&prefix=app%2F&maxresults=5000");

        var stringToSign = AzureSharedKey.StringToSign(request, "myaccount");

        Assert.StartsWith("GET\n\n\n\n", stringToSign);
        Assert.EndsWith("/myaccount/files\ncomp:list\nmaxresults:5000\nprefix:app/\nrestype:container", stringToSign);
    }

    [Fact]
    public void The_authorization_header_is_the_hmac_of_the_string_to_sign_under_the_decoded_key()
    {
        var account = AzureAccount.Parse("UseDevelopmentStorage=true");
        var request = new HttpRequestMessage(HttpMethod.Delete, "http://127.0.0.1:10000/devstoreaccount1/files/ab/abcd");

        AzureSharedKey.Sign(request, account, Now);

        var expected = Convert.ToBase64String(HMACSHA256.HashData(account.AccountKey!,
            Encoding.UTF8.GetBytes(AzureSharedKey.StringToSign(request, "devstoreaccount1"))));
        Assert.Equal($"SharedKey devstoreaccount1:{expected}", request.Headers.Authorization!.ToString());
        Assert.Equal(Date, request.Headers.GetValues("x-ms-date").Single());
    }

    [Fact]
    public void A_service_sas_string_to_sign_has_sixteen_fields_in_order()
    {
        var stringToSign = AzureSas.StringToSign("myaccount", "files", "ab/abcd", "2026-08-08T12:05:00Z", "https",
            "image/png", "inline; filename=a.png");

        Assert.Equal(
            ["r", "", "2026-08-08T12:05:00Z", "/blob/myaccount/files/ab/abcd", "", "", "https", AzureSharedKey.Version,
             "b", "", "", "", "inline; filename=a.png", "", "", "image/png"],
            stringToSign.Split('\n'));
    }

    [Fact]
    public void A_service_sas_carries_its_fields_and_no_start_time()
    {
        var account = AzureAccount.Parse("UseDevelopmentStorage=true");

        var sas = AzureSas.BlobRead(account, "files", "ab/abcd", Now.AddMinutes(5), "image/png", "inline");
        var fields = sas.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

        Assert.Equal(AzureSharedKey.Version, fields["sv"]);
        Assert.Equal("https,http", fields["spr"]); // the emulator is http
        Assert.Equal("2026-08-08T12:05:00Z", fields["se"]);
        Assert.Equal("b", fields["sr"]);
        Assert.Equal("r", fields["sp"]);
        Assert.Equal("image/png", fields["rsct"]);
        Assert.False(fields.ContainsKey("st"));
        Assert.Equal(
            Convert.ToBase64String(HMACSHA256.HashData(account.AccountKey!, Encoding.UTF8.GetBytes(
                AzureSas.StringToSign("devstoreaccount1", "files", "ab/abcd", "2026-08-08T12:05:00Z", "https,http", "image/png", "inline")))),
            fields["sig"]);
    }
}

public sealed class AzureBlobBackendTests : IDisposable
{
    private readonly string _spool = Path.Combine(Path.GetTempPath(), $"rask-azure-spool-{Guid.NewGuid():N}.tmp");

    public void Dispose() => File.Delete(_spool);

    [Fact]
    public async Task A_put_creates_a_block_blob_with_the_safe_headers_and_shared_key()
    {
        var (backend, handler) = Create("UseDevelopmentStorage=true");
        handler.Respond(HttpStatusCode.Created);
        await File.WriteAllBytesAsync(_spool, "hello"u8.ToArray());

        await backend.PutFileAsync("ab/abcd", _spool, 5, new("image/png", "inline", "private, no-store"), default);

        var put = handler.Last;
        Assert.Equal("http://127.0.0.1:10000/devstoreaccount1/files/ab/abcd", put.Uri.ToString());
        Assert.Equal("BlockBlob", put.Header("x-ms-blob-type"));
        Assert.Equal("image/png", put.Header("x-ms-blob-content-type"));
        Assert.Equal("inline", put.Header("x-ms-blob-content-disposition"));
        Assert.Equal("private, no-store", put.Header("x-ms-blob-cache-control"));
        Assert.StartsWith("SharedKey devstoreaccount1:", put.Header("authorization"));
        Assert.Equal("hello", Encoding.UTF8.GetString(put.Body));
    }

    [Fact]
    public async Task A_sas_account_appends_its_token_and_sends_no_authorization()
    {
        var (backend, handler) = Create("BlobEndpoint=https://myaccount.blob.core.windows.net;SharedAccessSignature=sv=2024-11-04&sig=abc");
        handler.Respond(HttpStatusCode.OK, ListXml(null, "app/ab/one"));

        await backend.ListAsync("app/", default).ToListAsync();

        Assert.Contains("restype=container&comp=list", handler.Last.Uri.Query);
        Assert.EndsWith("&sv=2024-11-04&sig=abc", handler.Last.Uri.Query);
        Assert.Null(handler.Last.Header("authorization"));
        Assert.False(backend.TryPresign("ab/abcd", TimeSpan.FromMinutes(5), "image/png", "inline", out _));
    }

    [Fact]
    public void A_key_account_presigns_a_read_only_blob_sas()
    {
        var (backend, _) = Create("UseDevelopmentStorage=true");

        Assert.True(backend.TryPresign("ab/abcd", TimeSpan.FromMinutes(5), "image/png", "inline", out var url));
        Assert.StartsWith("http://127.0.0.1:10000/devstoreaccount1/files/ab/abcd?sv=", url);
        Assert.Contains("&sp=r&", url);
        Assert.Contains("&sig=", url);
    }

    [Fact]
    public async Task A_listing_follows_the_next_marker()
    {
        var (backend, handler) = Create("UseDevelopmentStorage=true");
        handler.Respond(HttpStatusCode.OK, ListXml("page-2", "app/ab/one")).Respond(HttpStatusCode.OK, ListXml(null, "app/cd/two"));

        var entries = await backend.ListAsync("app/", default).ToListAsync();

        Assert.Equal(["app/ab/one", "app/cd/two"], entries.Select(e => e.Key));
        Assert.Equal(7, entries[0].Size);
        Assert.Contains("marker=page-2", handler.Last.Uri.Query);
    }

    [Fact]
    public async Task A_ranged_read_uses_x_ms_range_and_a_missing_blob_reads_as_null()
    {
        var (backend, handler) = Create("UseDevelopmentStorage=true");
        handler.Respond(HttpStatusCode.PartialContent, "x").Respond(HttpStatusCode.NotFound);

        await using (await backend.OpenReadAsync("ab/abcd", 5, 10, default))
        {
            Assert.Equal("bytes=5-14", handler.Last.Header("x-ms-range"));
        }

        Assert.Null(await backend.OpenReadAsync("ab/none", 0, null, default));
    }

    [Fact]
    public async Task A_refusal_carries_the_error_code_header()
    {
        var (backend, handler) = Create("UseDevelopmentStorage=true");
        handler.Respond(HttpStatusCode.Forbidden, null, ("x-ms-error-code", "AuthenticationFailed"));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => backend.DeleteAsync("ab/abcd", default));

        Assert.Contains("AuthenticationFailed", ex.Message);
    }

    private static (AzureBlobBackend Backend, RecordingHandler Handler) Create(string connectionString)
    {
        var handler = new RecordingHandler();
        return (new AzureBlobBackend(new HttpClient(handler), AzureAccount.Parse(connectionString), "files",
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero))), handler);
    }

    private static string ListXml(string? nextMarker, params string[] names) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <EnumerationResults ContainerName="files">
          <Blobs>{string.Concat(names.Select(n => $"<Blob><Name>{n}</Name><Properties><Last-Modified>Sat, 08 Aug 2026 12:00:00 GMT</Last-Modified><Content-Length>7</Content-Length></Properties></Blob>"))}</Blobs>
          <NextMarker>{nextMarker}</NextMarker>
        </EnumerationResults>
        """;
}
