using Rask.Core;
using Rask.Core.Authentication;
using Rask.Core.Routing;

namespace Rask.Wasm.Fixture.Wasm;

public sealed partial class App : Component
{
    protected override Component? HeadAssets => [
        Title["Rask.Wasm fixture"],
        Meta.Charset("utf-8"),
    ];

    protected override Component? Render() => Main[Router];
}

/// <summary>
///     A page anybody may open: who it is drawn for, and what the last call to the host's API answered. Public on
///     purpose — a tab whose sign-in ended goes to the sign-in page from any page, not only from one that needs it.
/// </summary>
[Route("/desk")]
public sealed partial class DeskPage(IUserProvider users, HttpClient http, IAuthSignIn auth) : Component
{
    private string _note = "none";

    protected override Component? Render() =>
    [
        P.Id("who")[$"who={users.Current.Identity?.Name ?? "nobody"}"],
        P.Id("note")[$"note={_note}"],
        Button.Id("act").OnClick(Act)["act"],
        Button.Id("sign-out").OnClick(() => auth.SignOut("/desk"))["sign out"]
    ];

    private async Task Act()
    {
        try
        {
            _note = await http.GetStringAsync(new Uri("api/note", UriKind.Relative));
        }
        catch (HttpRequestException ex)
        {
            _note = $"refused {(int?)ex.StatusCode}";
        }
    }
}

[Route("/login")]
public sealed partial class SignInPage : Component
{
    protected override Component? Render() => P.Id("sign-in-page")["sign in here"];
}
