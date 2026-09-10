using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="IStorageEstimator" /> — read the origin's storage quota and usage.</summary>
public sealed partial class StorageEstimateDemo(IStorageEstimator storage) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                UiButton.Label("Estimate storage").Tone(UiTone.Primary).Variant(UiVariant.Outline).Class("mb-2")
                    .Id("storage-est-read")
                    .OnClick(Read),
                Div.Class("text-sm text-ui-muted")["Budget: ", Code.Id("storage-est-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("storage-est-status")[_status ?? "(idle)"]]
            ];

    private async Task Read()
    {
        try
        {
            if (!await storage.IsSupportedAsync())
            {
                _value = "not supported in this browser";
                _status = "Storage estimate unavailable";
                return;
            }

            var e = await storage.EstimateAsync();
            _value = e is null
                ? "unavailable"
                : $"{Mb(e.Usage)} / {Mb(e.Quota)} MB used ({e.UsageRatio:P1})";
            _status = "Estimate read";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }

    private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("N1");
}
