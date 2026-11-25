using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
/// A job enqueued by a user an app numbers — <c>User : Aggregate&lt;int&gt;, IUser</c> — records that user as the Guid
/// that carries the number, and runs as the same number: the handler's <c>Current.RequiredUserId</c> is 12 again.
/// </summary>
[Collection(CurrentRowsCollection.Name)]
public sealed class JobNumberedUserTests : IDisposable
{
    private static readonly Guid Twelve = new("00000000-0000-0000-0000-00000000000c");

    public void Dispose() => CurrentRows.Reset();

    [Fact]
    public async Task A_job_enqueued_by_user_12_records_the_carried_guid_and_runs_as_user_12()
    {
        CurrentRows.Use(new NumberedUsers());
        await using var h = new JobsHarness(services: services =>
        {
            services.AddScoped<SignedIn>();
            services.AddScoped<IScopeUser>(sp => sp.GetRequiredService<SignedIn>());
            services.AddScoped<IPrincipalSource>(sp => sp.GetRequiredService<SignedIn>());
        });
        var page = new ServiceCollection()
            .AddSingleton<IPrincipalSource>(new SignedIn { Current = Principal("12", "admin") })
            .BuildServiceProvider();

        using (Db.UseScope(page))
        {
            await h.Queue.Enqueue(new NumberedUserJob("ran"), TestContext.Current.CancellationToken);
        }

        var row = await h.SingleJobAsync();
        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal(Twelve, row.UserId);
        Assert.Equal("ran:12:12:admin", Assert.Single(h.Recorder.Values));
    }

    private static ClaimsPrincipal Principal(string user, params string[] roles) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, user), .. roles.Select(role => new Claim(ClaimTypes.Role, role))],
        "Test",
        ClaimTypes.Name,
        ClaimTypes.Role));

    // The app's user registration as the generated code makes it for an int key; no row is ever read here.
    private sealed class NumberedUsers() : CurrentRow(CurrentRowKind.User, typeof(object))
    {
        internal override bool IsNumbered => true;

        protected override object? Load(Guid key) => null;

        protected override Task<object?> LoadAsync(Guid key, CancellationToken cancellationToken) => Task.FromResult<object?>(null);

        protected override bool Owns(object row, Guid key) => false;
    }

    private sealed class SignedIn : IScopeUser, IPrincipalSource
    {
        public ClaimsPrincipal? Current { get; set; }

        public void SignIn(ClaimsPrincipal user) => Current = user;
    }
}

public sealed record NumberedUserJob(string Tag) : IJob;

public sealed class NumberedUserJobHandler(Recorder recorder) : ICommandHandler<NumberedUserJob>
{
    public Task Handle(NumberedUserJob command)
    {
        var principal = Current.Principal;
        var claimed = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "none";
        var roles = string.Join(',', (principal?.FindAll(ClaimTypes.Role) ?? []).Select(role => role.Value));
        recorder.Add($"{command.Tag}:{CurrentRows.RequiredUserId<int>()}:{claimed}:{roles}");
        return Task.CompletedTask;
    }
}
