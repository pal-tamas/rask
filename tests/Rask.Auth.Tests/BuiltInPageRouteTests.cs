using Rask.Auth.Pages;
using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Auth.Tests;

/// <summary>
///     The built-in pages are reachable, and an app's own page at the same route wins.
/// </summary>
/// <remarks>
///     <para>
///         In the same xUnit collection as everything else here because <see cref="RouteRegistry" /> is
///         process-global: a test that mutates it cannot run beside one that reads it.
///     </para>
///     <para>
///         The override case registers the competing page directly rather than through a second
///         assembly's generated registry. What that pins is the part which actually decides the outcome —
///         that the <b>earlier</b> registration wins a duplicate template. The rest is ordering, and the
///         ordering is structural: an app's generated registry is initialised when its own module is
///         first touched, which happens when <c>Program.cs</c> starts running, and that is necessarily
///         before the <c>AddRaskAuth</c> call inside it first touches this package.
///     </para>
/// </remarks>
[Collection(AuthDbCollection.Name)]
public sealed class BuiltInPageRouteTests
{
    [Fact]
    public void The_built_in_pages_answer_their_routes()
    {
        WithRegistry(() =>
        {
            Assert.True(RouteResolver.TryResolve("/login", out var login));
            Assert.Contains(typeof(LoginPage), login);

            Assert.True(RouteResolver.TryResolve("/register", out var register));
            Assert.Contains(typeof(RegisterPage), register);

            Assert.True(RouteResolver.TryResolve("/logout", out var logout));
            Assert.Contains(typeof(LogoutPage), logout);
        });
    }

    [Fact]
    public void An_app_page_at_the_same_route_wins()
    {
        WithRegistry(() =>
        {
            RouteRegistry.Add([new RouteRegistration(typeof(AppLoginPage), "login", null)]);

            Assert.True(RouteResolver.TryResolve("/login", out var chain));

            Assert.Contains(typeof(AppLoginPage), chain);
            Assert.DoesNotContain(typeof(LoginPage), chain);
        });
    }

    /// <summary>Runs <paramref name="body" /> with this package's routes registered, then resets.</summary>
    private static void WithRegistry(Action body)
    {
        // Touching a type forces this assembly's generated __RaskRoutesRegistry module initializer,
        // which is what registers the [Route] pages — the same thing that happens in a real host.
        _ = typeof(LoginPage).Name;

        try
        {
            body();
        }
        finally
        {
            // Global state: leaving the competing registration behind would break every later test.
            RouteRegistry.Reset();
            _ = typeof(LoginPage).Name;
        }
    }

    /// <summary>Stands in for a page an app declared at <c>/login</c> itself.</summary>
    private sealed class AppLoginPage : Component
    {
        protected override Component? Render() => null;
    }
}
