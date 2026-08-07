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
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                BsStack(Gap: 2, WrapItems: true, Class: Margin.Bottom(2))[
                    BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Id: "perm-geo", OnClickAsync: QueryGeo)[
                        "Query geolocation"],
                    BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Id: "perm-clip", OnClickAsync: QueryClipboard)[
                        "Query clipboard-read"]
                ],
                Div(Class: "small text-secondary")["geolocation: ", Code(Id: "perm-geo-value")[_geo ?? "(unknown)"]],
                Div(Class: "small text-secondary")["clipboard-read: ", Code(Id: "perm-clip-value")[_clip ?? "(unknown)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "perm-status")[_status ?? "(idle)"]]
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
