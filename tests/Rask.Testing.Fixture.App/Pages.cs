using Microsoft.AspNetCore.Authorization;
using Rask.Core;
using Rask.Core.Routing;

namespace Rask.Testing.Fixture.App;

/// <summary>Something only this app's Program.cs registers — a page shows it only if the app's own services are in play.</summary>
public sealed record Greeting(string Text);

public sealed partial class App : Component
{
    protected override Component? Render() => Router;
}

/// <summary>The app's own state, one per app: what a test sets before its visit, the page it visits reads.</summary>
public sealed class Guestbook
{
    public List<string> Names { get; } = [];
}

[Route("/")]
public sealed partial class HomePage(Greeting greeting, Guestbook guestbook) : Component
{
    protected override Component? Render() =>
        [H1[greeting.Text], P[$"{guestbook.Names.Count} signed the guestbook"]];
}

[Route("/admin")]
[Authorize(Roles = "Admin")]
public sealed partial class AdminPage : Component
{
    protected override Component? Render() => H1["Only admins"];
}

[Route("/save")]
public sealed partial class SavePage : Component
{
    protected override Component? Render() => Button.Type(ButtonType.Button).OnClick(() => Toast.Success("Saved"))["Save"];
}

[Route("/login")]
public sealed partial class LoginPage : Component
{
    protected override Component? Render() => H1["Sign in"];
}
