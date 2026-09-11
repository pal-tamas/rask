namespace Rask.DevTools.Tests;

/// <summary>
///     The browser host switches the devtools on only for a page served from this machine; a Debug bundle on a real host
///     keeps them off for its visitors.
/// </summary>
public sealed class DevToolsOriginTests
{
    [Theory]
    [InlineData("http://localhost:5050/")]
    [InlineData("https://localhost/")]
    [InlineData("http://127.0.0.1:8080/")]
    [InlineData("http://[::1]:5000/")]
    public void A_page_served_from_this_machine_is_local(string origin) =>
        Assert.True(DevToolsOrigin.IsLoopback(origin));

    [Theory]
    [InlineData("https://rask.sh/")]
    [InlineData("http://192.168.1.20:5000/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("https://localhost.example.com/")]
    [InlineData("file:///index.html")]
    [InlineData("localhost")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not(string? origin) =>
        Assert.False(DevToolsOrigin.IsLoopback(origin));
}
