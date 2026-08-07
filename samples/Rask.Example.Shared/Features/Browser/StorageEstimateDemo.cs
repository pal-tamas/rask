using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="IStorageEstimator" /> — read the origin's storage quota and usage.</summary>
public sealed partial class StorageEstimateDemo(IStorageEstimator storage) : Component
{
    private string? _value;
    private string? _status;

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                BsButton(Color: BsColor.Primary, Outline: true, Size: BsSize.Sm, Class: "mb-2", Id: "storage-est-read", OnClickAsync: Read)[
                    "Estimate storage"],
                Div(Class: "small text-secondary")["Budget: ", Code(Id: "storage-est-value")[_value ?? "(not requested)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "storage-est-status")[_status ?? "(idle)"]]
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
