using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs.Tests;

public sealed class AuthorizationBehaviorTests
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private static ClaimsPrincipal SignedIn(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(static role => new Claim(ClaimTypes.Role, role)), "test"));

    // `inFlight: false` is a host that registered nothing — Rask.Cqrs on its own, or a browser app.
    private static ServiceProvider Build(ClaimsPrincipal? user, bool inFlight = true, bool? policies = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Recorder());
        if (inFlight)
        {
            services.AddSingleton<IDispatchPrincipal>(new FixedPrincipal(user));
        }

        if (policies is { } permits)
        {
            services.AddSingleton<IPolicyEvaluator>(new FixedPolicies(permits));
        }

        services.AddRaskCqrs();
        return services.BuildServiceProvider();
    }

    private static Task Send(ServiceProvider sp, ICommand command) =>
        sp.GetRequiredService<IDispatcher>().Send(command, TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_user_without_the_role_is_refused_an_admin_only_command()
    {
        await using var sp = Build(SignedIn("editor"));

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Send(sp, new DeleteEverything()));

        Assert.True(refused.IsAuthenticated);
        Assert.Equal("DeleteEverything requires the role admin, owner, which the signed-in user does not hold.", refused.Message);
        Assert.Empty(sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task An_admin_is_admitted_to_an_admin_only_command()
    {
        await using var sp = Build(SignedIn("admin"));

        await Send(sp, new DeleteEverything());

        Assert.Equal(["deleted"], sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task Any_one_of_the_named_roles_is_enough()
    {
        await using var sp = Build(SignedIn("owner"));

        await Send(sp, new DeleteEverything());

        Assert.Equal(["deleted"], sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task An_anonymous_visitor_is_refused_a_bare_authorize()
    {
        await using var sp = Build(Anonymous);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Send(sp, new SaveDraft()));

        Assert.False(refused.IsAuthenticated);
        Assert.Equal("SaveDraft requires a signed-in user, and nobody is signed in.", refused.Message);
        Assert.Empty(sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task A_signed_in_user_is_admitted_to_a_bare_authorize()
    {
        await using var sp = Build(SignedIn());

        await Send(sp, new SaveDraft());

        Assert.Equal(["saved"], sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task Work_with_no_principal_in_flight_runs_the_handler_as_the_system()
    {
        await using var sp = Build(user: null);

        await Send(sp, new DeleteEverything());

        Assert.Equal(["deleted"], sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task A_host_that_names_no_principal_runs_the_handler()
    {
        await using var sp = Build(user: null, inFlight: false);

        await Send(sp, new DeleteEverything());

        Assert.Equal(["deleted"], sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task Allow_anonymous_on_the_handler_is_never_checked()
    {
        await using var sp = Build(Anonymous);

        var terms = await sp.GetRequiredService<IDispatcher>().Query(new ReadTerms(), TestContext.Current.CancellationToken);

        Assert.Equal("terms", terms);
    }

    [Fact]
    public async Task A_query_is_refused_the_same_as_a_command()
    {
        await using var sp = Build(SignedIn("editor"));

        var refused = await Assert.ThrowsAsync<ForbiddenException>(
            () => sp.GetRequiredService<IDispatcher>().Query(new GetSalaries(), TestContext.Current.CancellationToken));

        Assert.True(refused.IsAuthenticated);
        Assert.Contains("GetSalaries requires the role admin", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_query_admits_the_role_it_names()
    {
        await using var sp = Build(SignedIn("admin"));

        var salaries = await sp.GetRequiredService<IDispatcher>().Query(new GetSalaries(), TestContext.Current.CancellationToken);

        Assert.Equal(42, salaries);
    }

    [Fact]
    public async Task A_policy_the_host_refuses_is_a_refusal()
    {
        await using var sp = Build(SignedIn(), policies: false);

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => Send(sp, new ShipOrder()));

        Assert.Equal("ShipOrder requires the policy 'CanShip', which the caller does not meet.", refused.Message);
        Assert.Empty(sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task A_policy_the_host_grants_runs_the_handler()
    {
        await using var sp = Build(SignedIn(), policies: true);

        await Send(sp, new ShipOrder());

        Assert.Equal(["shipped"], sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task A_policy_with_nothing_to_decide_it_throws_rather_than_allowing()
    {
        await using var sp = Build(SignedIn());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => Send(sp, new ShipOrder()));

        Assert.Contains("'CanShip'", thrown.Message, StringComparison.Ordinal);
        Assert.Empty(sp.GetRequiredService<Recorder>().Entries);
    }

    [Fact]
    public async Task A_caller_who_may_not_send_a_request_is_refused_before_it_is_validated()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Recorder());
        services.AddSingleton<IDispatchPrincipal>(new FixedPrincipal(Anonymous));
        services.AddSingleton<IRequestValidator<SaveDraft>>(new RejectsEveryDraft());
        services.AddRaskCqrs();
        await using var sp = services.BuildServiceProvider();

        var thrown = await Record.ExceptionAsync(() => Send(sp, new SaveDraft()));

        Assert.IsType<ForbiddenException>(thrown);
    }

    private sealed class RejectsEveryDraft : IRequestValidator<SaveDraft>
    {
        public ValueTask<IReadOnlyList<RequestValidationError>> Validate(SaveDraft request) =>
            ValueTask.FromResult<IReadOnlyList<RequestValidationError>>(
                [new RequestValidationError(string.Empty, "No drafts today.")]);
    }
}
