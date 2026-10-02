using System.Globalization;
using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>MDN's <c>StorageManager.estimate()</c> from Rask.Web — read the origin's storage quota and usage.</summary>
public sealed partial class StorageEstimateDemo : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Ui.Button.Primary.Outline.Class("mb-2")
                    .Id("storage-est-read")
                    .OnClick(Read)["Estimate storage"],
                Div.Class("text-sm text-ui-muted")["Budget: ", Code.Id("storage-est-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("storage-est-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            if (!await Navigator.Storage.IsSupported)
            {
                _value = "not supported in this browser";
                _status = "Storage estimate unavailable";
                return;
            }

            // Both figures are coarse on purpose (anti-fingerprinting): a budget, not an exact count.
            var e = await Navigator.Storage.Estimate();
            var usage = e.Usage ?? 0;
            var quota = e.Quota ?? 0;
            var ratio = quota > 0 ? (double)usage / quota : 0;
            _value = $"{Mb(usage)} / {Mb(quota)} MB used ({ratio:P1})";
            _status = "Estimate read";
        }
        catch (JSException ex) { _status = "Read failed: " + ex.Message; }
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("N1", CultureInfo.InvariantCulture);
}
