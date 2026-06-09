using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Example.Shared.Demos;

#pragma warning disable RASK014 // test renders the demo component directly as a root

namespace Rask.Example.Shared.Tests.Demos;

// The Authorize-component showcase: the nested Authorize picks one of three states off the toggleable
// DemoUserProvider (admin / signed-in / anonymous) with no imperative branching.
public sealed class AuthorizeDemoTests
{
    [Fact]
    public void Anonymous_ShowsSignInPrompt()
    {
        var html = Render(new DemoUserProvider());

        Assert.Contains("Sign in to see member content", html);
        Assert.DoesNotContain("standard access", html);
        Assert.DoesNotContain("Admin-only", html);
    }

    [Fact]
    public void SignedInUser_ShowsStandardAccess()
    {
        var provider = new DemoUserProvider();
        provider.SignIn("alice", "user");

        var html = Render(provider);

        Assert.Contains("standard access", html);
        Assert.DoesNotContain("Admin-only", html);
    }

    [Fact]
    public void Admin_ShowsAdminContent()
    {
        var provider = new DemoUserProvider();
        provider.SignIn("rootadmin", "admin");

        var html = Render(provider);

        Assert.Contains("Admin-only", html);
        Assert.DoesNotContain("Sign in to see member content", html);
    }

    private static string Render(DemoUserProvider provider)
    {
        var sp = new ServiceCollection()
            .AddSingleton(provider)
            .AddSingleton<IUserProvider>(provider)
            .BuildServiceProvider();
        return new AuthorizeDemo(provider).RenderAsLiveRoot(sp);
    }
}
