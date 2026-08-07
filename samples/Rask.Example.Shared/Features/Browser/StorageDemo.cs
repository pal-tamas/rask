using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IBrowserStorage" /> — a <c>localStorage</c> round-trip, injected through the ctor and
///     identical on Server and WASM.
/// </summary>
public sealed partial class StorageDemo(IBrowserStorage storage) : Component
{
    private const string StorageKey = "rask.browser.storage";

    private string _input = string.Empty;
    private string? _read;
    private string? _status;

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                BsInputGroup(Size: BsSize.Sm, Class: "mb-2")[
                    BsInput<string>(
                        Id: "storage-input",
                        Value: _input,
                        Placeholder: "Value to persist",
                        OnChange: v => _input = v),
                    BsButton(Color: BsColor.Primary, Id: "storage-set", OnClickAsync: Set)["Set"],
                    BsButton(Color: BsColor.Primary, Outline: true, Id: "storage-read", OnClickAsync: Read)["Read"],
                    BsButton(Color: BsColor.Danger, Outline: true, Id: "storage-remove", OnClickAsync: Remove)["Remove"]
                ],
                Div(Class: "small text-secondary")["Last read: ", Code(Id: "storage-read-value")[_read ?? "(null)"]],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "storage-status")[_status ?? "(idle)"]]
            ]
        ];

    private async Task Set()
    {
        try
        {
            await storage.Local.SetAsync(StorageKey, _input);
            _status = $"Stored: {_input}";
        }
        catch (Exception ex) { _status = "Set failed: " + ex.Message; }
    }

    private async Task Read()
    {
        try
        {
            _read = await storage.Local.GetAsync(StorageKey);
            var count = await storage.Local.LengthAsync();
            _status = $"Read (localStorage holds {count} key(s))";
        }
        catch (Exception ex) { _status = "Read failed: " + ex.Message; }
    }

    private async Task Remove()
    {
        try
        {
            await storage.Local.RemoveAsync(StorageKey);
            _read = null;
            _status = "Removed";
        }
        catch (Exception ex) { _status = "Remove failed: " + ex.Message; }
    }
}
