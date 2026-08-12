using System.Net.Http.Headers;

namespace Rask.ObjectStore.Tests;

// SigV4 is the one thing in this package that is either exactly right or completely broken: a signature
// that differs by one byte is rejected the same way as no signature at all, and the service never says
// which part was wrong. So these tests pin it two independent ways.
//
// The three expected Authorization headers below were produced by a separate implementation of the
// algorithm written in Python from the AWS specification (Create a signed AWS API request), not by running
// this code and recording what it emitted. Agreement between two independent implementations is evidence;
// a golden file captured from the implementation under test would only prove it hasn't changed.
//
// The remaining tests assert the specific rules the specification calls out by name — the exact unreserved
// set, %20 rather than +, no double-encoding, sorting after encoding, which headers must be signed — so a
// shared misreading of the spec is caught as well as a coding mistake.
public class SigV4SignerTests
{
    private const string AccessKey = "AKIAIOSFODNN7EXAMPLE";
    private const string Secret = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
    private static readonly DateTimeOffset SigningTime = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly ObjectStoreCredential Credential = new(AccessKey, Secret);

    private static string Sign(HttpRequestMessage request)
    {
        SigV4Signer.Sign(request, Credential, "us-east-1", "s3", SigningTime);
        return request.Headers.Authorization!.ToString();
    }

    [Fact]
    public void RangedGet_MatchesIndependentImplementation()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/my-bucket/db/app v2.sqlite");
        request.Headers.TryAddWithoutValidation("Range", "bytes=0-9");

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260808/us-east-1/s3/aws4_request, " +
            "SignedHeaders=host;range;x-amz-content-sha256;x-amz-date, " +
            "Signature=fbd35e1217a0041e926b279c154137260459f0ee9d730cb2e74eeb9de5202079",
            Sign(request));
    }

    [Fact]
    public void List_MatchesIndependentImplementation()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, "https://s3.example.com/my-bucket/?list-type=2&prefix=ops%2F");

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260808/us-east-1/s3/aws4_request, " +
            "SignedHeaders=host;x-amz-content-sha256;x-amz-date, " +
            "Signature=476ba5a0aad4be774613ff528bfc2c3ad249028e318185694235f9fbe1ff1cc2",
            Sign(request));
    }

    [Fact]
    public void ConditionalCreate_MatchesIndependentImplementation()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "https://s3.example.com/my-bucket/lock");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260808/us-east-1/s3/aws4_request, " +
            "SignedHeaders=host;if-none-match;x-amz-content-sha256;x-amz-date, " +
            "Signature=9b80da239e8e39b102eb676c37963dfdd5e47250d781387ec4c6889daf7c05e3",
            Sign(request));
    }

    // "For Amazon S3, include the literal string UNSIGNED-PAYLOAD ... and set the same value as the
    // x-amz-content-sha256 header value when sending the request."
    [Fact]
    public void Sets_UnsignedPayload_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "https://s3.example.com/b/k");

        Sign(request);

        Assert.Equal("UNSIGNED-PAYLOAD", Assert.Single(request.Headers.GetValues("x-amz-content-sha256")));
    }

    [Fact]
    public void Sets_AmzDate_InBasicIso8601()
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "https://s3.example.com/b/k");

        Sign(request);

        Assert.Equal("20260808T120000Z", Assert.Single(request.Headers.GetValues("x-amz-date")));
    }

    // A session token is an x-amz-* header, so it must be signed rather than merely sent — a service that
    // saw an unsigned token would be trusting a value anything in the middle could have replaced.
    [Fact]
    public void SessionToken_IsSentAndSigned()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/k");

        SigV4Signer.Sign(
            request, new ObjectStoreCredential(AccessKey, Secret, "session-token-value"),
            "us-east-1", "s3", SigningTime);

        Assert.Equal("session-token-value", Assert.Single(request.Headers.GetValues("x-amz-security-token")));
        Assert.Contains("x-amz-security-token", request.Headers.Authorization!.Parameter);
    }

    // "The space character is a reserved character and must be encoded as %20 (and not as +)" — and it must
    // be encoded once. A path taken already-escaped and escaped again signs %2520, so the signature covers
    // a key that isn't the one being requested, and the service rejects it with no hint why.
    [Fact]
    public void Space_EncodesOnce_As20()
    {
        var fromUnescaped = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/a b.txt");
        var fromEscaped = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/a%20b.txt");

        Assert.Equal(Sign(fromUnescaped), Sign(fromEscaped));
    }

    // "Encode the forward slash character, '/', everywhere except in the object key name." A key reads as
    // a path in the bucket, so its separators must survive canonicalisation unescaped.
    [Fact]
    public void Slashes_InKey_StayUnencoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/x/y/z.txt");

        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260808/us-east-1/s3/aws4_request, " +
            "SignedHeaders=host;x-amz-content-sha256;x-amz-date, " +
            "Signature=a492587f6a42ef0c5ed320d4e4246fb2f859cc6687ab771f1990ab13bc585b9a",
            Sign(request));
    }

    // A documented limitation rather than a wish: System.Uri normalises %2F back to '/' while parsing, so a
    // key whose *name* contains an encoded slash is indistinguishable from one with a real path separator
    // by the time any signing code can see it. S3 permits such keys; this client cannot address them, and
    // the failure would otherwise look like a signature mismatch rather than an unrepresentable key.
    // Pinned so the day the platform changes, this says so instead of a signature quietly starting to differ.
    [Fact]
    public void EncodedSlash_IsIndistinguishableFromARealSeparator()
    {
        var nested = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/x/y/z.txt");
        var escaped = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/x%2Fy%2Fz.txt");

        Assert.Equal(Sign(nested), Sign(escaped));
    }

    // "You must also sort the parameters in the canonical query string alphabetically by key name. The
    // sorting occurs after encoding." Declaration order must therefore not affect the signature.
    [Fact]
    public void QueryParameters_SortAfterEncoding_SoOrderDoesNotMatter()
    {
        var one = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/?prefix=a&list-type=2");
        var other = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/?list-type=2&prefix=a");

        Assert.Equal(Sign(one), Sign(other));
    }

    [Fact]
    public void Unreserved_Characters_AreNeverEscaped()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/aZ0-._~");

        Sign(request);

        // If any of the unreserved set were escaped the signature would be computed over a different path;
        // asserting the pair against the independent implementation is what proves the set is exactly right.
        Assert.Equal(
            "AWS4-HMAC-SHA256 Credential=AKIAIOSFODNN7EXAMPLE/20260808/us-east-1/s3/aws4_request, " +
            "SignedHeaders=host;x-amz-content-sha256;x-amz-date, " +
            "Signature=" + ExpectedForUnreserved,
            request.Headers.Authorization!.ToString());
    }

    // "There is no comma between the algorithm and Credential. However, the other elements must be
    // separated by commas."
    [Fact]
    public void AuthorizationHeader_HasNoCommaAfterTheAlgorithm()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/k");

        Sign(request);

        var header = request.Headers.Authorization!;
        Assert.Equal("AWS4-HMAC-SHA256", header.Scheme);
        Assert.StartsWith("Credential=", header.Parameter);
    }

    [Fact]
    public void DifferentSecret_ProducesDifferentSignature()
    {
        var a = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/k");
        var b = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/k");

        SigV4Signer.Sign(a, new ObjectStoreCredential(AccessKey, Secret), "us-east-1", "s3", SigningTime);
        SigV4Signer.Sign(b, new ObjectStoreCredential(AccessKey, "a-different-secret"), "us-east-1", "s3", SigningTime);

        Assert.NotEqual(a.Headers.Authorization!.ToString(), b.Headers.Authorization!.ToString());
    }

    // The credential scope binds a signature to one day, one region and one service, so a signature cannot
    // be replayed against a different bucket's region.
    [Fact]
    public void Region_IsPartOfTheCredentialScope()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/k");

        SigV4Signer.Sign(request, Credential, "eu-west-2", "s3", SigningTime);

        Assert.Contains("/20260808/eu-west-2/s3/aws4_request", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public void NonDefaultPort_IsPartOfTheSignedHost()
    {
        var standard = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com/b/k");
        var custom = new HttpRequestMessage(HttpMethod.Get, "https://s3.example.com:9000/b/k");

        Assert.NotEqual(Sign(standard), Sign(custom));
    }

    private const string ExpectedForUnreserved =
        "b4cc88f31dafd52dacb65fcbafebb55d2bc61d305e8f258a9981632a3611079b";
}
