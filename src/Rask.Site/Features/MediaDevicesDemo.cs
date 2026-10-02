using Microsoft.JSInterop;
using Rask.Web;

namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>MediaDevices</c> from Rask.Web — capture the camera/microphone (or the screen) and show it in a
///     <c>&lt;video&gt;</c>. The stream is a live object the browser holds: stopping its tracks releases the hardware
///     (the camera indicator turns off), and disposing of it lets the browser drop it.
/// </summary>
public sealed partial class MediaDevicesDemo : Component
{
    private readonly ElementRef<HTMLVideoElement> _video = new();
    private Rask.Web.Types.MediaStream? _stream;
    private string _status = "(idle)";

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Video
                    .Ref(_video)
                    .Width(320)
                    .Height(240)
                    .Muted()
                    .PlaysInline()
                    .Class("rounded border mb-2 bg-slate-900 block"),
                Div.Class("flex gap-2 flex-wrap mb-2")[
                    Ui.Button.Primary.Id("media-start").OnClick(StartCamera)[Ui.Icon.Name(Ui.IconName.VideoCamera), "Start camera"],
                    Ui.Button.Primary.Outline.Id("media-screen").OnClick(ShareScreen)[Ui.Icon.Name(Ui.IconName.Desktop), "Share screen"],
                    Ui.Button.Error.Outline
                        .Id("media-stop")
                        .Disabled(_stream is null)
                        .OnClick(Stop)["Stop"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("media-status")[_status]]
            ];

    // An empty constraints object asks for the camera with whatever the browser picks; `Video = null` would not ask.
    private Task StartCamera() =>
        Capture(() => Navigator.MediaDevices.GetUserMedia(new() { Video = new() }), "Camera live");

    private Task ShareScreen() => Capture(() => Navigator.MediaDevices.GetDisplayMedia(), "Screen sharing");

    private async Task Capture(Func<ValueTask<Rask.Web.Types.MediaStream>> request, string okStatus)
    {
        try
        {
            if (!await Navigator.MediaDevices.IsSupported)
            {
                _status = "Media capture not supported in this browser";
                return;
            }

            await Release();
            _stream = await request();
            await _video.SetSrcObject(_stream);
            await _video.Play();
            _status = okStatus;
        }
        catch (JSException ex)
        {
            // A denied or dismissed prompt rejects (NotAllowedError), as does a page with no camera (NotFoundError).
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task Stop()
    {
        await Release();
        _status = "Stopped — hardware released";
    }

    // Stopping every track is what turns the camera off; disposing only lets go of the handles.
    private async Task Release()
    {
        if (_stream is null)
        {
            return;
        }

        foreach (var track in await _stream.GetTracks())
        {
            await track.Stop();
            await track.DisposeAsync();
        }

        await _video.SetSrcObject(null);
        await _stream.DisposeAsync();
        _stream = null;
    }

    protected override Task OnUnmount() => Release();
}
