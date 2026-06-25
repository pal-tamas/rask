using System.Globalization;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     Exercises the typed browser-API foundation — <see cref="IBrowserStorage" />,
///     <see cref="IClipboard" />, <see cref="IGeolocation" />, <see cref="INavigatorInfo" /> — each
///     injected through the ctor (the framework's DI seam) and identical on Server and WASM. Every call
///     is wrapped in try/catch because these APIs are browser-gated (secure context, permissions).
/// </summary>
public sealed class BrowserApiDemo(
    IBrowserStorage storage,
    IClipboard clipboard,
    IGeolocation geolocation,
    INavigatorInfo navigator) : Component
{
    private const string StorageKey = "rask.browser.demo";

    private string _storageInput = string.Empty;
    private string? _storageRead;
    private string _clipboardInput = "Copied from Rask!";
    private string? _clipboardRead;
    private string? _location;
    private string? _navigator;
    private string? _status;

    protected override RenderResult Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body d-flex flex-column gap-4")[
                StorageSection(),
                ClipboardSection(),
                GeolocationSection(),
                NavigatorSection(),
                Div()[
                    Span(Class: "text-secondary small text-uppercase")["Status"],
                    Div()[Code(Class: "fs-6", Id: "browser-status")[_status ?? "(idle)"]]
                ]
            ]
        ];

    private Component StorageSection() =>
        Div()[
            H6(Class: "fw-bold")[I(Class: "bi bi-hdd me-2"), "localStorage"],
            Div(Class: "input-group input-group-sm mb-2")[
                Input(
                    Id: "storage-input",
                    Class: "form-control",
                    Value: _storageInput,
                    Placeholder: "Value to persist",
                    OnInput: v => _storageInput = v),
                Button(Class: "btn btn-primary", Id: "storage-set", OnClickAsync: StorageSet)["Set"],
                Button(Class: "btn btn-outline-primary", Id: "storage-read", OnClickAsync: StorageRead)["Read"],
                Button(Class: "btn btn-outline-danger", Id: "storage-remove", OnClickAsync: StorageRemove)["Remove"]
            ],
            Div(Class: "small text-secondary")[
                "Last read: ", Code(Id: "storage-read-value")[_storageRead ?? "(null)"]
            ]
        ];

    private Component ClipboardSection() =>
        Div()[
            H6(Class: "fw-bold")[I(Class: "bi bi-clipboard me-2"), "Clipboard"],
            Div(Class: "input-group input-group-sm mb-2")[
                Input(
                    Id: "clipboard-input",
                    Class: "form-control",
                    Value: _clipboardInput,
                    OnInput: v => _clipboardInput = v),
                Button(Class: "btn btn-primary", Id: "clipboard-copy", OnClickAsync: ClipboardCopy)["Copy"],
                Button(Class: "btn btn-outline-primary", Id: "clipboard-paste", OnClickAsync: ClipboardPaste)["Paste"]
            ],
            Div(Class: "small text-secondary")[
                "Pasted: ", Code(Id: "clipboard-read-value")[_clipboardRead ?? "(nothing yet)"]
            ]
        ];

    private Component GeolocationSection() =>
        Div()[
            H6(Class: "fw-bold")[I(Class: "bi bi-geo-alt me-2"), "Geolocation"],
            Button(Class: "btn btn-outline-primary btn-sm mb-2", Id: "geo-get", OnClickAsync: GetLocation)[
                "Get current position"],
            Div(Class: "small text-secondary")[
                Code(Id: "geo-value")[_location ?? "(not requested)"]
            ]
        ];

    private Component NavigatorSection() =>
        Div()[
            H6(Class: "fw-bold")[I(Class: "bi bi-info-circle me-2"), "Navigator"],
            Button(Class: "btn btn-outline-primary btn-sm mb-2", Id: "nav-read", OnClickAsync: ReadNavigator)[
                "Read navigator info"],
            Div(Class: "small text-secondary")[
                Code(Id: "nav-value")[_navigator ?? "(not requested)"]
            ]
        ];

    private async Task StorageSet()
    {
        try
        {
            await storage.Local.SetAsync(StorageKey, _storageInput);
            _status = $"Stored: {_storageInput}";
        }
        catch (Exception ex)
        {
            _status = "Set failed: " + ex.Message;
        }
    }

    private async Task StorageRead()
    {
        try
        {
            _storageRead = await storage.Local.GetAsync(StorageKey);
            var count = await storage.Local.LengthAsync();
            _status = $"Read (localStorage holds {count} key(s))";
        }
        catch (Exception ex)
        {
            _status = "Read failed: " + ex.Message;
        }
    }

    private async Task StorageRemove()
    {
        try
        {
            await storage.Local.RemoveAsync(StorageKey);
            _storageRead = null;
            _status = "Removed";
        }
        catch (Exception ex)
        {
            _status = "Remove failed: " + ex.Message;
        }
    }

    private async Task ClipboardCopy()
    {
        try
        {
            await clipboard.WriteTextAsync(_clipboardInput);
            _status = "Copied to clipboard";
        }
        catch (Exception ex)
        {
            _status = "Copy failed: " + ex.Message;
        }
    }

    private async Task ClipboardPaste()
    {
        try
        {
            _clipboardRead = await clipboard.ReadTextAsync();
            _status = "Pasted from clipboard";
        }
        catch (Exception ex)
        {
            _status = "Paste failed: " + ex.Message;
        }
    }

    private async Task GetLocation()
    {
        try
        {
            var pos = await geolocation.GetCurrentPositionAsync(
                new GeolocationOptions { TimeoutMs = 10_000 });
            // Coordinates format invariantly (decimal point) — independent of the server's locale.
            _location = string.Create(
                CultureInfo.InvariantCulture,
                $"lat {pos.Latitude:F4}, lon {pos.Longitude:F4} (±{pos.Accuracy:F0} m)");
            _status = "Position acquired";
        }
        catch (Exception ex)
        {
            _location = null;
            _status = "Location failed: " + ex.Message;
        }
    }

    private async Task ReadNavigator()
    {
        try
        {
            var online = await navigator.OnLineAsync();
            var language = await navigator.LanguageAsync();
            _navigator = $"online: {online}, language: {language}";
            _status = "Navigator read";
        }
        catch (Exception ex)
        {
            _status = "Navigator read failed: " + ex.Message;
        }
    }
}
