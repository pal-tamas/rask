using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class WebAuthnTests
{
    private static PublicKeyCredentialCreationOptions CreationOptions() => new()
    {
        Challenge = "Y2hhbGxlbmdl",
        Rp = new RelyingParty("Rask"),
        User = new PublicKeyCredentialUser("dXNlcg", "ada@example.com", "Ada")
    };

    private static PublicKeyCredentialRequestOptions RequestOptions() => new()
    {
        Challenge = "Y2hhbGxlbmdl"
    };

    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskWebAuthn.isSupported", true);

        Assert.True(await new WebAuthn(js).IsSupportedAsync());
    }

    [Fact]
    public async Task IsPlatformAuthenticatorAvailable_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskWebAuthn.platformAuthenticatorAvailable", true);

        Assert.True(await new WebAuthn(js).IsPlatformAuthenticatorAvailableAsync());
    }

    [Fact]
    public async Task Create_PassesOptions_AndReturnsAttestation()
    {
        var js = new FakeJsRuntime();
        var attestation = new AttestationResult("id1", "raw1", "cdj", "att", ["internal"]);
        js.SetResponse("__raskWebAuthn.create", attestation);
        var options = CreationOptions();

        var result = await new WebAuthn(js).CreateAsync(options);

        Assert.Same(options, js.ArgsFor("__raskWebAuthn.create")![0]);
        Assert.Same(attestation, result);
    }

    [Fact]
    public async Task Create_ReturnsNull_WhenCancelled()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new WebAuthn(js).CreateAsync(CreationOptions()));
    }

    [Fact]
    public async Task Get_PassesOptions_AndReturnsAssertion()
    {
        var js = new FakeJsRuntime();
        var assertion = new AssertionResult("id1", "raw1", "cdj", "authData", "sig", null);
        js.SetResponse("__raskWebAuthn.get", assertion);
        var options = RequestOptions();

        var result = await new WebAuthn(js).GetAsync(options);

        Assert.Same(options, js.ArgsFor("__raskWebAuthn.get")![0]);
        Assert.Same(assertion, result);
    }

    [Fact]
    public async Task Get_ReturnsNull_WhenCancelled()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new WebAuthn(js).GetAsync(RequestOptions()));
    }

    [Fact]
    public async Task Create_NullOptions_Throws()
    {
        var svc = new WebAuthn(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.CreateAsync(null!));
    }

    [Fact]
    public async Task Get_NullOptions_Throws()
    {
        var svc = new WebAuthn(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.GetAsync(null!));
    }
}
