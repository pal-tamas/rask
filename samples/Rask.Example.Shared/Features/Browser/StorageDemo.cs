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
        Div.Class($"{Tw.Card} shadow-sm border-0")[
            Div.Class(Tw.CardBody)[
                Div.Class("mb-2 flex gap-2")[
                    Input.Value(_input)
                        .Class(Tw.Input)
                        .Id("storage-input")
                        .Placeholder("Value to persist")
                        .OnInput(v => _input = v),
                    Button.Type("button").Class(Tw.BtnPrimary).Id("storage-set").OnClickAsync(Set)["Set"],
                    Button.Type("button").Class(Tw.BtnOutlinePrimary).Id("storage-read").OnClickAsync(Read)["Read"],
                    Button.Type("button").Class(Tw.BtnOutlineDanger).Id("storage-remove").OnClickAsync(Remove)["Remove"]
                ],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Last read: ", Code.Id("storage-read-value")[_read ?? "(null)"]],
                Div.Class("text-sm text-slate-500 dark:text-slate-400")["Status: ", Code.Id("storage-status")[_status ?? "(idle)"]]
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
