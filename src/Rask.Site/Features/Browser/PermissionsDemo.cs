
namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>Permissions</c> from Rask.Web — query a feature's permission state (granted/denied/prompt) before
///     triggering it. Pairs with <c>Navigator.Geolocation</c> / <c>Navigator.Clipboard</c>.
/// </summary>
public sealed partial class PermissionsDemo : Component
{
    private string? _geo;
    private string? _clip;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Primary.Outline
                        .Id("perm-geo")
                        .OnClick(QueryGeo)["Query geolocation"],
                    Ui.Button.Primary.Outline
                        .Id("perm-clip")
                        .OnClick(QueryClipboard)["Query clipboard-read"]
                ],
                Div.Class("text-sm text-ui-muted")["geolocation: ", Code.Id("perm-geo-value")[_geo ?? "(unknown)"]],
                Div.Class("text-sm text-ui-muted")["clipboard-read: ", Code.Id("perm-clip-value")[_clip ?? "(unknown)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("perm-status")[_status ?? "(idle)"]]
            ];

    private async Task QueryGeo()
    {
        try
        {
            _geo = await Query("geolocation");
            _status = "Queried geolocation";
        }
        catch (Exception ex) { _status = "Query failed: " + ex.Message; }
    }

    private async Task QueryClipboard()
    {
        try
        {
            _clip = await Query("clipboard-read");
            _status = "Queried clipboard-read";
        }
        catch (Exception ex) { _status = "Query failed: " + ex.Message; }
    }

    // navigator.permissions.query({ name }): the PermissionStatus is kept, so read its state and let it go.
    private static async Task<string> Query(string name)
    {
        await using var status = await Navigator.Permissions.Query(new() { Name = name });
        return (await status.State).ToString();
    }
}
