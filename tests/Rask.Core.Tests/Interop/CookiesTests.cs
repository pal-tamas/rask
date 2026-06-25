using System.Globalization;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class CookiesTests
{
    [Fact]
    public async Task Get_SendsCookieGet_AndReturnsValue()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.cookieGet", "dark");
        var cookies = new Cookies(js);

        var value = await cookies.GetAsync("theme");

        Assert.Equal("dark", value);
        Assert.Equal(["theme"], js.ArgsFor("__raskApi.cookieGet"));
    }

    [Fact]
    public async Task Set_NoOptions_SendsNullAttributes()
    {
        var js = new FakeJsRuntime();
        var cookies = new Cookies(js);

        await cookies.SetAsync("k", "v");

        // (name, value, maxAge, expires, path, domain, sameSite, secure)
        Assert.Equal(["k", "v", null, null, null, null, null, false], js.ArgsFor("__raskApi.cookieSet"));
    }

    [Fact]
    public async Task Set_WithOptions_SendsFormattedAttributes()
    {
        var js = new FakeJsRuntime();
        var cookies = new Cookies(js);
        var expires = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);

        await cookies.SetAsync("k", "v", new CookieOptions
        {
            MaxAgeSeconds = 3600,
            Expires = expires,
            Path = "/",
            Domain = "example.com",
            SameSite = SameSiteMode.Strict,
            Secure = true
        });

        var expectedExpires = expires.ToUniversalTime().ToString("R", CultureInfo.InvariantCulture);
        Assert.Equal(
            ["k", "v", 3600, expectedExpires, "/", "example.com", "strict", true],
            js.ArgsFor("__raskApi.cookieSet"));
    }

    [Fact]
    public async Task Delete_SendsCookieDelete_WithNameAndPath()
    {
        var js = new FakeJsRuntime();
        var cookies = new Cookies(js);

        await cookies.DeleteAsync("k", "/");

        Assert.Equal(["k", "/"], js.ArgsFor("__raskApi.cookieDelete"));
    }

    [Fact]
    public async Task GetAll_ReturnsMap()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.cookieAll", new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
        var cookies = new Cookies(js);

        var all = await cookies.GetAllAsync();

        Assert.Equal("1", all["a"]);
        Assert.Equal("2", all["b"]);
    }

    [Fact]
    public async Task Get_NullName_Throws()
    {
        var cookies = new Cookies(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await cookies.GetAsync(null!));
    }
}
