using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Authorization;
using Rask.Core.Components;
using Rask.Core.Live;
using Rask.Core.Messaging;
using Rask.Core.Routing;

// RASK014: the router is a ROOT's only child, built here rather than by a chain because nothing renders this
// file's code as markup. One instance, captured, so every render hands back the same router — a new one each
// render would remount the page and lose its state.
#pragma warning disable RASK014

namespace Rask.Testing;

public partial class Page
{
    // Set on a page Visit opened, so As can sign someone in and visit the same address again.
    private string? _visited;
    private IServiceProvider? _app;

    /// <summary>
    ///     Opens the app at <paramref name="url" /> the way a person does: the app is booted from its own
    ///     <c>Program.cs</c> — its services, its database (a fresh one for this test), its settings — and the
    ///     page registered for that URL renders through the real router, route guards included.
    /// </summary>
    /// <remarks>
    ///     <code>
    ///     var page = Page.Visit("/products/new").As(admin);
    ///
    ///     await page.Type("Tea").Into("Name");
    ///     await page.Click("Save");
    ///
    ///     page.IsAt("/products");
    ///     </code>
    ///     A page behind <c>[Authorize]</c> sends a visitor to <c>/login</c>, as the app does; <see cref="As{TUser}" />
    ///     visits it signed in. The test's own reads — <c>await Product.Count()</c> — see the same database.
    ///     With no app referenced (a component library's tests), the router runs over <paramref name="services" />.
    /// </remarks>
    public static Page Visit(string url, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (services is null && TestRun.Current?.Services is { } run)
        {
            // The test's own app, which its earlier User.Create(…) may already have booted.
            return Open(url, run, visited: true);
        }

        if (services is null && TestApp.Boot(untilExit: true) is { } app)
        {
            // No test hook (another test framework): a scope of a fresh app, left open for the rest of the test so
            // the test's static calls after this line (Product.Count(), Jobs.Enqueue) reach the same database.
            var scope = app.Services.CreateScope().ServiceProvider;
            _ = Ambient.Enter(scope);
            return Open(url, scope, visited: true);
        }

        return Open(url, services, visited: false);
    }

    /// <summary>
    ///     This page again, visited by <paramref name="user" /> — signed in the way the app signs people in, so
    ///     <c>[Authorize]</c>, <c>Authorize.Roles(…)</c> and <c>Current.UserId</c> all see them.
    /// </summary>
    /// <typeparam name="TUser">The app's user type, or a <see cref="ClaimsPrincipal" />.</typeparam>
    /// <param name="user">Who is visiting — a row of the app's user table, created by the test.</param>
    /// <exception cref="InvalidOperationException">This page was not opened by <see cref="Visit" /> on an app.</exception>
    public Page As<TUser>(TUser user)
        where TUser : class
    {
        ArgumentNullException.ThrowIfNull(user);
        if (_visited is null || _app is null)
        {
            throw new InvalidOperationException(
                "As signs someone in to the app a page was visited on — open the page with Page.Visit(url) in a " +
                "test project that references the app.");
        }

        var principal = user as ClaimsPrincipal
                        ?? _app.GetService<IPrincipalFor>()?.For(user)
                        ?? throw new InvalidOperationException(
                            $"The app cannot sign in a {typeof(TUser).Name}: pass one of its users, or a ClaimsPrincipal.");

        (_app.GetRequiredService<IUserProvider>() as ISettableUserProvider
         ?? throw new InvalidOperationException("The app's user provider cannot be signed in to."))
            .Set(principal);

        return Open(_visited, _app, visited: true);
    }

    private static Page Open(string url, IServiceProvider? services, bool visited)
    {
        var route = TestRoute.At(Guarded(url, services));
        var routed = new RoutedServices(route, new Navigator(route, services?.GetService(typeof(IDownloadSink)) as IDownloadSink), services);
        // Routes = null resolves to the app's registered route table, which the chain entry does for an app.
        var router = new Router(route) { Routes = null };

        // The app's built-in toasts, drawn after the page as the host draws them — so a save's Toast.Success("Saved")
        // is on screen for page.Shows("Saved").
        var page = services?.GetService(typeof(RaskDocumentDefaults)) is RaskDocumentDefaults { Toasts: { } toasts } defaults
            ? Page.Render(Toasting(router, toasts, defaults.ToastDuration), routed)
            : Page.Render(() => router, routed);
        if (visited)
        {
            page._visited = url;
            page._app = services;
        }

        return page;
    }

    private static Func<Component?> Toasting(
        Router router, Func<IReadOnlyList<ToastMessage>, Action<int>, Component> toasts, TimeSpan duration)
    {
        var outlet = new ToastOutlet { Template = toasts, BuiltIn = true, AutoDismissAfter = duration };
        return () => [router, outlet];
    }

    // Where the app actually lands for url: the page's own address, or /login (or /forbidden) when a guard on the
    // page's route refuses the current user — the redirect a host makes before rendering anything.
    private static string Guarded(string url, IServiceProvider? services)
    {
        var path = url.Split('?', 2)[0];
        if (services is null || !RouteResolver.TryResolve(path, out var chain))
        {
            return url;
        }

        var user = services.GetService<IUserProvider>()?.Current ?? new ClaimsPrincipal(new ClaimsIdentity());
        var result = RouteAuthorizationGuard.Evaluate(services, chain, user).GetAwaiter().GetResult();
        return result.Outcome switch
        {
            RouteAuthorizationOutcome.Challenge => RouteAuthorizationGuard.ChallengePath + "?returnUrl=" + Uri.EscapeDataString(url),
            RouteAuthorizationOutcome.Forbid => RouteAuthorizationGuard.ForbidPath,
            _ => url,
        };
    }

    // The page's own route and navigator first, then whatever the test brought.
    private sealed class RoutedServices(RouteState route, Navigator navigator, IServiceProvider? app) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType switch
        {
            _ when serviceType == typeof(RouteState) => route,
            _ when serviceType == typeof(Navigator) => navigator,
            _ => app?.GetService(serviceType),
        };
    }
}
