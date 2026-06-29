using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class InstallPromptTests
{
    [Fact]
    public async Task CanInstall_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskInstall.canInstall", true);

        Assert.True(await new InstallPrompt(js).CanInstallAsync());
    }

    [Fact]
    public async Task IsInstalled_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskInstall.isInstalled", true);

        Assert.True(await new InstallPrompt(js).IsInstalledAsync());
    }

    [Theory]
    [InlineData("accepted", InstallOutcome.Accepted)]
    [InlineData("dismissed", InstallOutcome.Dismissed)]
    [InlineData("unavailable", InstallOutcome.Unavailable)]
    [InlineData("anything-else", InstallOutcome.Unavailable)]
    public async Task Prompt_MapsOutcome(string raw, InstallOutcome expected)
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskInstall.prompt", raw);

        Assert.Equal(expected, await new InstallPrompt(js).PromptAsync());
    }
}
