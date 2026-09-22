using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class InstallPromptTests
{
    [Fact]
    public async Task Whether_it_can_install_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskInstall.canInstall", true);

        Assert.True(await new InstallPrompt(js).CanInstallAsync());
    }

    [Fact]
    public async Task Whether_it_is_installed_is_asked_of_the_helper()
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
    public async Task Prompting_maps_the_outcome(string raw, InstallOutcome expected)
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskInstall.prompt", raw);

        Assert.Equal(expected, await new InstallPrompt(js).PromptAsync());
    }
}
