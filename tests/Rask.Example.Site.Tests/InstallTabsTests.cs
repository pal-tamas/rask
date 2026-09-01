using Rask.Testing;

namespace Rask.Example.Site.Tests;

// The landing page's install block is the project's front door: for most people it is the first and only
// place they read the install command. These tests pin the command it actually RENDERS, not the constant
// it holds — a typo in the string, or a tab that quietly kept the old instruction, reaches the published
// site otherwise, and the site is built from this component on every push to main.
//
// The cross-file half of the same problem — the README, NUGET.md, docs/ and llms.txt drifting away from
// this page — is asserted in scripts/tests/install-script.test.sh, which greps the sources.
public partial class InstallTabsTests : global::Rask.Core.RaskMarkup
{
    private const string Installer = "curl -sSL https://rask.sh/rask.sh | sh";
    private const string WindowsInstaller = "irm https://rask.sh/rask.ps1 | iex";

    [Fact]
    public void Server_tab_is_the_default_and_leads_with_the_installer()
    {
        var page = RaskTest.Render(() => InstallTabs);

        Assert.Contains(Installer, page.Html, StringComparison.Ordinal);
        Assert.Contains("rask new MyApp", page.Html, StringComparison.Ordinal);
        Assert.Contains("ASP.NET live-server app", page.Html, StringComparison.Ordinal);
    }

    // Both terminals have to carry it. The tabs pick a TEMPLATE, not an install method, so a visitor who
    // lands on either one must see a command that works.
    [Fact]
    public async Task Wasm_tab_shows_the_installer_too()
    {
        var page = RaskTest.Render(() => InstallTabs);

        // Driven through the handler ids rather than a CSS selector: the two tabs differ only by an aria
        // attribute, and the second click handler is unambiguously the WASM tab.
        var tabs = page.HandlerIds("click");
        Assert.Equal(2, tabs.Count);
        await page.InvokeAsync(tabs[1]);

        Assert.Contains("browser-WASM SPA", page.Html, StringComparison.Ordinal);
        Assert.Contains(Installer, page.Html, StringComparison.Ordinal);
        Assert.Contains("rask new MyApp --template wasm", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_gets_its_own_one_liner()
    {
        var page = RaskTest.Render(() => InstallTabs);

        // rask.sh refuses to run under MINGW/MSYS and points here, so the page must actually offer it.
        Assert.Contains(WindowsInstaller, page.Html, StringComparison.Ordinal);
    }

    // The whole point of the change: `dotnet tool install -g Rask.Cli` fails on a machine with no .NET
    // SDK, which is exactly the machine a landing page is read on. It stays documented as the
    // SDK-already-present path in docs/installation.md — just not as the headline here.
    [Fact]
    public async Task Neither_terminal_still_leads_with_the_bare_dotnet_tool_install()
    {
        var page = RaskTest.Render(() => InstallTabs);
        Assert.DoesNotContain("dotnet tool install", page.Html, StringComparison.Ordinal);

        var tabs = page.HandlerIds("click");
        await page.InvokeAsync(tabs[1]);
        Assert.DoesNotContain("dotnet tool install", page.Html, StringComparison.Ordinal);
    }

    // Switching tabs is real Rask state, not a CSS toggle: the inactive terminal is not in the document at
    // all. Worth pinning, because "both tabs show the installer" would also pass if both were rendered and
    // one were hidden.
    [Fact]
    public async Task Only_the_selected_terminal_is_rendered()
    {
        var page = RaskTest.Render(() => InstallTabs);
        Assert.DoesNotContain("browser-WASM SPA", page.Html, StringComparison.Ordinal);

        var tabs = page.HandlerIds("click");
        await page.InvokeAsync(tabs[1]);
        Assert.DoesNotContain("ASP.NET live-server app", page.Html, StringComparison.Ordinal);
    }
}
