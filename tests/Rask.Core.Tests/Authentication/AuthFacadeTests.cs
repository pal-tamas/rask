using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Authentication;
using Rask.Wire;

namespace Rask.Core.Tests.Authentication;

public class AuthFacadeTests
{
    [Fact]
    public async Task Signing_in_through_the_facade_reaches_the_auth_of_the_work_in_progress()
    {
        var auth = new RecordingAuth();
        using var services = new ServiceCollection().AddSingleton<IAuth>(auth).BuildServiceProvider();
        using var work = Ambient.Enter(services);

        var result = await Auth.SignIn("ada@example.com", "secret", remember: true, returnUrl: "/orders");

        Assert.Same(RecordingAuth.Ok, result);
        Assert.Equal(["SignIn ada@example.com secret True /orders"], auth.Calls);
    }

    [Fact]
    public async Task Every_flow_on_the_facade_goes_to_the_same_named_flow_on_the_ambient_auth()
    {
        var auth = new RecordingAuth();
        using var services = new ServiceCollection().AddSingleton<IAuth>(auth).BuildServiceProvider();
        using var work = Ambient.Enter(services);

        await Auth.Register("ada@example.com", "secret");
        await Auth.SignOut("/bye");
        await Auth.SignOutOtherDevices();
        await Auth.SignOutEverywhere();
        await Auth.SendPasswordReset("ada@example.com");
        await Auth.ResetPassword("u1", "t1", "new");
        await Auth.ConfirmEmail("u1", "t2");

        Assert.Equal(
            [
                "Register ada@example.com secret",
                "SignOut /bye",
                "SignOutOtherDevices",
                "SignOutEverywhere ",
                "SendPasswordReset ada@example.com",
                "ResetPassword u1 t1 new",
                "ConfirmEmail u1 t2",
            ],
            auth.Calls);
    }

    [Fact]
    public async Task Signing_in_outside_any_work_in_progress_says_to_inject_IAuth_instead()
    {
        using var none = Ambient.Enter((IServiceProvider?)null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Auth.SignIn("ada@example.com", "secret"));

        Assert.Contains("outside any work in progress", error.Message, StringComparison.Ordinal);
        Assert.Contains("Inject IAuth", error.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingAuth : IAuth
    {
        public static readonly AuthResult Ok = AuthResult.Success;

        public List<string> Calls { get; } = [];

        public Task<AuthResult> Register(string email, string password, string? returnUrl = null, string? firstRunToken = null) =>
            Record($"Register {email} {password}");

        public Task SignOutOtherDevices() => Record("SignOutOtherDevices");

        public Task SignOutEverywhere(string? returnUrl = null) => Record($"SignOutEverywhere {returnUrl}");

        public Task<AuthResult> SignIn(string email, string password, bool remember = false, string? returnUrl = null) =>
            Record($"SignIn {email} {password} {remember} {returnUrl}");

        public Task SignOut(string? returnUrl = null) => Record($"SignOut {returnUrl}");

        public Task<AuthResult> SendPasswordReset(string email) => Record($"SendPasswordReset {email}");

        public Task<AuthResult> ResetPassword(string userId, string token, string password) =>
            Record($"ResetPassword {userId} {token} {password}");

        public Task<AuthResult> ConfirmEmail(string userId, string token) => Record($"ConfirmEmail {userId} {token}");

        private Task<AuthResult> Record(string call)
        {
            Calls.Add(call);
            return Task.FromResult(Ok);
        }
    }
}
