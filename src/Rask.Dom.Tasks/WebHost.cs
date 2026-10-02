using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rask.Core.Dom.Build;

// Which host a web API can run on. The server host sends each call over its socket, so two kinds of member work only in
// WebAssembly, and are generated into Rask.Wasm alone: calling one from a server app is then a compile error.
//   One that needs the user's click in progress (transient activation): by the time a click's frame has crossed the
//   socket and the call has come back, the browser has let the activation go and refuses with NotAllowedError.
//   A family driven once per frame (WebGL, WebGPU, audio worklets), where a round trip per call is no way to draw.
// The IDL does not mark activation, so that list is kept here; the tests check each entry against the snapshot.
internal static class WebHost
{
    public static readonly HashSet<string> Activation = new(StringComparer.Ordinal)
    {
        "Element.requestFullscreen", "Element.requestPointerLock", "HTMLInputElement.showPicker", "HTMLSelectElement.showPicker",
        "HTMLVideoElement.requestPictureInPicture", "Navigator.share", "Window.open", "PaymentRequest.show", "Document.requestStorageAccess",
        "ScreenOrientation.lock", "Notification.requestPermission", "MediaDevices.getDisplayMedia", "Serial.requestPort",
        "USB.requestDevice", "HID.requestDevice", "Bluetooth.requestDevice", "EyeDropper.open", "IdleDetector.requestPermission",
        "Window.showOpenFilePicker", "Window.showSaveFilePicker", "Window.showDirectoryPicker", "Window.queryLocalFonts",
        "DocumentPictureInPicture.requestWindow", "PresentationRequest.start", "ContactsManager.select",
        "DeviceOrientationEvent.requestPermission", "DeviceMotionEvent.requestPermission", "BeforeInstallPromptEvent.prompt",
    };

    private static readonly string[] Families = { "WebGL", "GPU", "AudioWorklet" };

    // A family's interfaces, and WebGL's extension objects, named VENDOR_name (OES_vertex_array_object), as nothing else is.
    public static bool IsWasmInterface(string name)
    {
        var underscore = name.IndexOf('_');
        return Families.Any(f => name.StartsWith(f, StringComparison.Ordinal)) || (underscore > 0 && name.Take(underscore).All(char.IsUpper));
    }

    public static bool IsWasmMember(string iface, string idl) => Activation.Contains(iface + "." + idl);

    // Whether generated C# names a per-frame family's type: a member that hands one out, or takes one, runs in WebAssembly.
    public static bool Mentions(string csharp) => TypesIn(csharp).Any(IsWasmInterface);

    // The Rask.Web types generated C# names, by MDN name.
    public static IEnumerable<string> TypesIn(string csharp) =>
        Regex.Matches(csharp, @"Types\.(?<type>\w+)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1))
            .Cast<Match>().Select(m => m.Groups["type"].Value);
}
