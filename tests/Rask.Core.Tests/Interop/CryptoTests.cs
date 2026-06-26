using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class CryptoTests
{
    [Fact]
    public async Task RandomUuid_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskCrypto.randomUuid", "0c4f8b1e-1111-4222-8333-444455556666");

        Assert.Equal("0c4f8b1e-1111-4222-8333-444455556666", await new Crypto(js).RandomUuidAsync());
    }

    [Fact]
    public async Task RandomBytes_PassesLength_AndReturnsBytes()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskCrypto.randomBytes", new byte[] { 1, 2, 3, 4 });

        var bytes = await new Crypto(js).RandomBytesAsync(4);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes);
        Assert.Equal([4], js.ArgsFor("__raskCrypto.randomBytes"));
    }

    [Fact]
    public async Task RandomBytes_NegativeLength_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await new Crypto(new FakeJsRuntime()).RandomBytesAsync(-1));
    }

    [Theory]
    [InlineData(HashAlgorithm.Sha1, "SHA-1")]
    [InlineData(HashAlgorithm.Sha256, "SHA-256")]
    [InlineData(HashAlgorithm.Sha384, "SHA-384")]
    [InlineData(HashAlgorithm.Sha512, "SHA-512")]
    public async Task DigestHex_PassesSpecNameAndText(HashAlgorithm algorithm, string spec)
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskCrypto.digestHex", "deadbeef");

        var hex = await new Crypto(js).DigestHexAsync(algorithm, "hello");

        Assert.Equal("deadbeef", hex);
        Assert.Equal([spec, "hello"], js.ArgsFor("__raskCrypto.digestHex"));
    }

    [Fact]
    public async Task DigestHex_NullText_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new Crypto(new FakeJsRuntime()).DigestHexAsync(HashAlgorithm.Sha256, null!));
    }
}
