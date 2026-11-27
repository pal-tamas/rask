using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;
using Rask.Wire;

namespace Rask.Auth.Tests;

/// <summary>
///     <see cref="Registration.Closed" />: nobody signs themselves up, an administrator hands accounts out, and a fresh
///     instance can still be claimed once with its first-run token.
/// </summary>
public sealed class ClosedRegistrationTests
{
    private const string Password = "Password1";
    private const string Owner = "owner@example.com";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_register_endpoint_is_not_mapped_while_sign_in_still_is()
    {
        using var app = new EndpointApp(auth: o => o.Registration = Registration.Closed);
        using var client = app.Client();

        var registered = await PostAsync(client, AuthApi.Register, new { email = Owner, password = Password, firstRunToken = EndpointApp.Token });
        var signedIn = await PostAsync(client, AuthApi.Login, new { email = Owner, password = Password });

        Assert.Equal(HttpStatusCode.NotFound, registered.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, signedIn.StatusCode);
    }

    [Fact]
    public async Task An_open_app_still_maps_the_register_endpoint()
    {
        using var app = new EndpointApp();
        using var client = app.Client();

        var registered = await PostAsync(client, AuthApi.Register, new { email = Owner, password = Password, firstRunToken = EndpointApp.Token });

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
    }

    [Fact]
    public async Task A_claimed_instance_refuses_every_registration_and_makes_no_account()
    {
        await using var harness = await ClaimedAsync();

        var outcome = await Accounts(harness).RegisterAsync("visitor@example.com", Password, AuthHarness.FirstRunTokenValue, client: null, Cancel);

        Assert.Equal(AuthError.NotAllowed, outcome.Result.Error);
        Assert.Null(outcome.Principal);
        Assert.Null(await harness.UserAsync("visitor@example.com"));
    }

    [Fact]
    public async Task An_unclaimed_instance_is_claimed_once_with_its_token_and_its_claimant_is_the_administrator()
    {
        await using var harness = await ClaimedAsync();

        Assert.Equal([RaskRoles.Admin], await harness.RolesOfAsync(Owner));
        Assert.Equal(1, await harness.UserCountAsync());
    }

    [Fact]
    public async Task Without_a_first_account_to_claim_nobody_registers_at_all()
    {
        await using var harness = new AuthHarness(o =>
        {
            o.Registration = Registration.Closed;
            o.FirstUserIsAdmin = false;
        });
        await harness.StartAsync();

        var outcome = await Accounts(harness).RegisterAsync(Owner, Password, AuthHarness.FirstRunTokenValue, client: null, Cancel);

        Assert.Equal(AuthError.NotAllowed, outcome.Result.Error);
        Assert.Equal(0, await harness.UserCountAsync());
    }

    [Fact]
    public async Task An_administrator_still_creates_accounts()
    {
        await using var harness = await ClaimedAsync();
        var owner = (await harness.UserAsync(Owner))!.Id;

        AuthResult created;
        using (var scope = harness.NewScope())
        using (Current.UseUser(owner))
        {
            created = await scope.ServiceProvider.GetRequiredService<IAccounts>().Create("kovacs@example.com", Password, Cancel);
        }

        Assert.True(created.Succeeded, $"{created.Error} {created.Message}");
        Assert.NotNull(await harness.UserAsync("kovacs@example.com"));
    }

    // A closed app whose owner claimed it with the first-run token.
    private static async Task<AuthHarness> ClaimedAsync()
    {
        var harness = new AuthHarness(o => o.Registration = Registration.Closed);
        await harness.StartAsync();
        var claimed = await Accounts(harness).RegisterAsync(Owner, Password, AuthHarness.FirstRunTokenValue, client: null, Cancel);
        Assert.True(claimed.Result.Succeeded, $"{claimed.Result.Error} {claimed.Result.Message}");

        return harness;
    }

    private static IAccountFlows Accounts(AuthHarness harness) => harness.Services.GetRequiredService<IAccountFlows>();

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string route, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AuthApi.DefaultPrefix + route)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(RaskAuthDefaults.RequestHeader, "1");

        return await client.SendAsync(request, Cancel);
    }
}
