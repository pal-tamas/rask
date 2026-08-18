using Rask.Core;
using Rask.Core.Routing;
using static Rask.Core.Components.Generated;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

namespace Rask.Native.Tests.Infrastructure;

// A component that calls Navigator.Download from a click handler — the exact shape of the shared
// DownloadDemo in samples/Rask.Example.Shared, which is compiled into the native showcase and threw on this
// host until IDownloadSink was registered.
internal sealed partial class NativeDownloadApp : Component
{
    private readonly Navigator _nav;

    public NativeDownloadApp(Navigator nav) => _nav = nav;

    /// <summary>The name the handler downloads under. Set by a test before the click.</summary>
    public static string FileName { get; set; } = "report.txt";

    protected override Component? HeadAssets => Title["download"];
    protected override string? HtmlLang => null;

    protected override Component? Render() =>
    [
        Button.OnClick(() => _nav.Download(FileName, "hello native"u8.ToArray(), "text/plain"))["download"]
    ];
}
