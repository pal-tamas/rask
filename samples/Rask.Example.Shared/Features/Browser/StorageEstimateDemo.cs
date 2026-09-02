using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IStorageEstimator" /> — read the origin's storage quota and usage.</summary>
public sealed partial class StorageEstimateDemo(IStorageEstimator storage) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Button.Class($"{Tw.BtnOutlinePrimary} mb-2").Type("button")
                    .Id("storage-est-read")
                    .OnClickAsync(Read)[
                    "Estimate storage"],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Budget: ", Code.Id("storage-est-value")[_value ?? "(not requested)"]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("storage-est-status")[_status ?? "(idle)"]]
            ]
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
