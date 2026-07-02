using Rask.Core;
using Rask.Wasm.Browser;

namespace Rask.Example.Wasm.Features;

/// <summary>
///     <see cref="IMediaDevices" /> — capture the camera/microphone (or screen) and show it in a
///     <c>&lt;video&gt;</c>. The live stream lives JS-side; dispose the handle to stop every track and
///     release the hardware (the camera indicator turns off).
/// </summary>
public sealed class MediaDevicesDemo(IMediaDevices media) : Component, IAsyncDisposable
{
    private readonly ElementRef _video = ElementRef.New();
    private IMediaStreamHandle? _stream;
    private string _status = "(idle)";

    protected override Component? Render() =>
        Div(Class: "card shadow-sm border-0")[
            Div(Class: "card-body")[
                Video(Ref: _video, Width: 320, Height: 240, Muted: true, PlaysInline: true,
                    Class: "rounded border mb-2 bg-dark d-block"),
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-primary btn-sm", Id: "media-start", OnClickAsync: StartCamera)[
                        I(Class: "bi bi-camera-video me-1"), "Start camera"],
                    Button(Class: "btn btn-outline-primary btn-sm", Id: "media-screen", OnClickAsync: ShareScreen)[
                        I(Class: "bi bi-display me-1"), "Share screen"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "media-stop", Disabled: _stream is null,
                        OnClickAsync: Stop)["Stop"]
                ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "media-status")[_status]]
            ]
        ];

    private Task StartCamera() => Capture(() => media.GetUserMediaAsync(new MediaConstraints(Video: true)), "Camera live");

    private Task ShareScreen() => Capture(() => media.GetDisplayMediaAsync(), "Screen sharing");

    private async Task Capture(Func<ValueTask<IMediaStreamHandle>> request, string okStatus)
    {
        try
        {
            if (!await media.IsSupportedAsync())
            {
                _status = "Media capture not supported in this browser";
                return;
            }

            await StopInternal();
            _stream = await request();
            await _stream.AttachToAsync(_video);
            _status = okStatus;
        }
        catch (Exception ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task Stop()
    {
        await StopInternal();
        _status = "Stopped — hardware released";
    }

    private async Task StopInternal()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopInternal();
}
