using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>document.cookie</c> from Rask.Web — read/write non-<c>HttpOnly</c> cookies, identical on Server and
///     WASM.
/// </summary>
public sealed partial class CookiesDemo : Component
{
    private const string Name = "rask_browser_cookie";

    private string _input = "vanilla";
    private string? _read;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex items-stretch gap-2 mb-2")[
                    Ui.Input.Value(_input).Label("Cookie value")
                        .Id("cookie-input")
                        .Placeholder("Cookie value")
                        .OnInput(v => _input = v),
                    Ui.Button.Primary.Id("cookie-set").OnClick(Set)["Set"],
                    Ui.Button.Id("cookie-get").OnClick(Get)["Get"],
                    Ui.Button.Red.Id("cookie-delete").OnClick(Delete)["Delete"]
                ],
                Div.Class("text-sm text-ui-muted")["Value: ", Code.Id("cookie-read-value")[_read ?? "(null)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("cookie-status")[_status ?? "(idle)"]]
            ];

    // document.cookie = "name=value; …": one cookie per write, its value URI-encoded as MDN recommends.
    private async Task Set()
    {
        try
        {
            await Document.SetCookie($"{Name}={Uri.EscapeDataString(_input)}; max-age=3600; path=/; samesite=lax");
            _status = $"Set: {_input}";
        }
        catch (JSException ex) { _status = "Set failed: " + ex.Message; }
    }

    // document.cookie reads every cookie as "a=1; b=2", so find ours by name.
    private async Task Get()
    {
        try
        {
            _read = Find(await Document.Cookie, Name);
            _status = _read is null ? "Not present" : "Read";
        }
        catch (JSException ex) { _status = "Get failed: " + ex.Message; }
    }

    // A cookie is deleted by writing it again, already expired, on the same path.
    private async Task Delete()
    {
        try
        {
            await Document.SetCookie($"{Name}=; max-age=0; path=/");
            _read = null;
            _status = "Deleted";
        }
        catch (JSException ex) { _status = "Delete failed: " + ex.Message; }
    }

    private static string? Find(string cookies, string name)
    {
        foreach (var pair in cookies.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var at = pair.IndexOf('=', StringComparison.Ordinal);
            if (at > 0 && pair.AsSpan(0, at).SequenceEqual(name))
            {
                return Uri.UnescapeDataString(pair[(at + 1)..]);
            }
        }

        return null;
    }
}
