using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IPermissions" /> — query a feature's permission state (granted/denied/prompt) before
///     triggering it. Pairs with <see cref="IGeolocation" /> / <see cref="IClipboard" />.
/// </summary>
public sealed partial class PermissionsDemo(IPermissions permissions) : Component
{
    private string? _geo;
    private string? _clip;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Button.Type("button").Class(Tw.BtnOutlinePrimary)
                        .Id("perm-geo")
                        .OnClickAsync(QueryGeo)[
                        "Query geolocation"],
                    Button.Type("button").Class(Tw.BtnOutlinePrimary)
                        .Id("perm-clip")
                        .OnClickAsync(QueryClipboard)[
                        "Query clipboard-read"]
                ],
                Div.Class("text-sm text-ui-muted")["geolocation: ", Code.Id("perm-geo-value")[_geo ?? "(unknown)"]],
                Div.Class("text-sm text-ui-muted")["clipboard-read: ", Code.Id("perm-clip-value")[_clip ?? "(unknown)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("perm-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task QueryGeo()
    {
        try
        {
            _geo = (await permissions.QueryAsync(PermissionName.Geolocation)).ToString();
            _status = "Queried geolocation";
        }
        catch (Exception ex) { _status = "Query failed: " + ex.Message; }
    }

    private async Task QueryClipboard()
    {
        try
        {
            _clip = (await permissions.QueryAsync(PermissionName.ClipboardRead)).ToString();
            _status = "Queried clipboard-read";
        }
        catch (Exception ex) { _status = "Query failed: " + ex.Message; }
    }
}
