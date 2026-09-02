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
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class($"{Tw.InputGroup} mb-2")[
                    Input
                        .Value(_input)
                        .Id("cookie-input")
                        .Class(Tw.Input)
                        .Placeholder("Cookie value")
                        .OnInput(v => _input = v),
                    Button.Type("button").Class(Tw.BtnPrimary).Id("cookie-set").OnClickAsync(Set)["Set"],
                    Button.Type("button").Class(Tw.BtnOutlinePrimary).Id("cookie-get").OnClickAsync(Get)["Get"],
                    Button.Type("button").Class(Tw.BtnOutlineDanger).Id("cookie-delete").OnClickAsync(Delete)["Delete"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Value: ", Code.Id("cookie-read-value")[_read ?? "(null)"]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("cookie-status")[_status ?? "(idle)"]]
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
