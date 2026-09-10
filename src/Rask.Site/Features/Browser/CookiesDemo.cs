using Rask.Core.Browser;

namespace Rask.Site.Features;

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
        UiCard.Class("shadow-sm")[
                Div.Class($"{Tw.InputGroup} mb-2")[
                    Input
                        .Value(_input)
                        .Id("cookie-input")
                        .Class(Tw.Input)
                        .Placeholder("Cookie value")
                        .OnInput(v => _input = v),
                    UiButton.Label("Set").Tone(UiTone.Primary).Id("cookie-set").OnClick(Set),
                    UiButton.Label("Get").Tone(UiTone.Primary).Variant(UiVariant.Outline).Id("cookie-get").OnClick(Get),
                    UiButton.Label("Delete").Tone(UiTone.Error).Variant(UiVariant.Outline).Id("cookie-delete").OnClick(Delete)
                ],
                Div.Class("text-sm text-ui-muted")["Value: ", Code.Id("cookie-read-value")[_read ?? "(null)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("cookie-status")[_status ?? "(idle)"]]
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
