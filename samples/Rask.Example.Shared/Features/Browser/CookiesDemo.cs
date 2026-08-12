using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="ICookies" /> — read/write non-<c>HttpOnly</c> cookies via <c>document.cookie</c>,
///     identical on Server and WASM.
/// </summary>
public sealed partial class CookiesDemo(ICookies cookies) : Component
{
    private const string Name = "rask_browser_cookie";

    private string _input = "vanilla";
    private string? _read;
    private string? _status;

    protected override Component? Render() =>
        BsCard.Class(Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody[
                Div.Class("input-group input-group-sm mb-2")[
                    Input
                        .Value(_input)
                        .Id("cookie-input")
                        .Class("form-control")
                        .Placeholder("Cookie value")
                        .OnInput(v => _input = v),
                    BsButton.Color(BsColor.Primary).Id("cookie-set").OnClickAsync(Set)["Set"],
                    BsButton.Color(BsColor.Primary).Outline(true).Id("cookie-get").OnClickAsync(Get)["Get"],
                    BsButton.Color(BsColor.Danger).Outline(true).Id("cookie-delete").OnClickAsync(Delete)["Delete"]
                ],
                Div.Class("small text-secondary")["Value: ", Code.Id("cookie-read-value")[_read ?? "(null)"]],
                Div.Class("small text-secondary")["Status: ", Code.Id("cookie-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Set()
    {
        try
        {
            await cookies.SetAsync(Name, _input, new CookieOptions
            {
                MaxAgeSeconds = 3600,
                Path = "/",
                SameSite = SameSiteMode.Lax
            });
            _status = $"Set: {_input}";
        }
        catch (Exception ex) { _status = "Set failed: " + ex.Message; }
    }

    private async Task Get()
    {
        try
        {
            _read = await cookies.GetAsync(Name);
            _status = _read is null ? "Not present" : "Read";
        }
        catch (Exception ex) { _status = "Get failed: " + ex.Message; }
    }

    private async Task Delete()
    {
        try
        {
            await cookies.DeleteAsync(Name, "/");
            _read = null;
            _status = "Deleted";
        }
        catch (Exception ex) { _status = "Delete failed: " + ex.Message; }
    }
}
