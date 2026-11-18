using Microsoft.Playwright;

namespace Rask.Testing.Playwright.Tests;

/// <summary>
///     The question a shared sign-in is asked before <see cref="RaskSite.SignedIn" /> hands it out again, against the
///     fixture app's own sessions — over Playwright's request API, which needs no browser.
/// </summary>
public sealed class SignInProbeTests : IAsyncLifetime
{
    private readonly RaskSite<Program> _site = new();
    private IPlaywright? _playwright;

    private IAPIRequest Requests => _playwright!.APIRequest;

    public async ValueTask InitializeAsync()
    {
        await _site.Start();
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _playwright?.Dispose();
        await _site.DisposeAsync();
    }

    [Fact]
    public async Task A_sign_in_that_holds_answers_as_it_did_when_it_was_new()
    {
        var state = await SignedIn("ann");

        var first = await SignInProbe.Answer(Requests, _site.Address, state, _site.SignInPage);
        var later = await SignInProbe.Answer(Requests, _site.Address, state, _site.SignInPage);

        Assert.Equal("/api/auth/me 200", first);
        Assert.Equal(first, later);
    }

    [Fact]
    public async Task A_copy_of_a_sign_in_signed_out_elsewhere_answers_as_nobody()
    {
        var state = await SignedIn("bob");
        var first = await SignInProbe.Answer(Requests, _site.Address, state, _site.SignInPage);

        await SignOut(state);
        var later = await SignInProbe.Answer(Requests, _site.Address, state, _site.SignInPage);

        Assert.Equal("/api/auth/me 200", first);
        Assert.Equal("/api/auth/me 204", later);
    }

    [Fact]
    public async Task A_sign_in_signed_out_elsewhere_leaves_another_sign_in_of_the_same_person_holding()
    {
        var shared = await SignedIn("carol");
        var own = await SignedIn("carol");

        await SignOut(own);
        var answer = await SignInProbe.Answer(Requests, _site.Address, shared, _site.SignInPage);

        Assert.Equal("/api/auth/me 200", answer);
    }

    private async Task<string> SignedIn(string name)
    {
        var context = await Requests.NewContextAsync(new APIRequestNewContextOptions { BaseURL = _site.Address.ToString() });
        await using var _ = context;
        var answer = await context.GetAsync("/fixture/sign-in/" + name);
        Assert.Equal(200, answer.Status);
        return await context.StorageStateAsync();
    }

    private async Task SignOut(string state)
    {
        var context = await Requests.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL = _site.Address.ToString(),
            StorageState = state,
        });
        await using var _ = context;
        var answer = await context.PostAsync("/api/auth/logout");
        Assert.Equal(204, answer.Status);
    }
}
