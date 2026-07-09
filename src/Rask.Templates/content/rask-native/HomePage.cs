using Rask.Core.Routing;

namespace Company.RaskNative;

[Route("/")]
public sealed class HomePage : Component
{
    protected override Component? Render() =>
        Div()[
            H1()["Hello, Rask — natively!"],
            P()["This is a native iOS/Android app. The same C# component code runs here as on the "
                + "server and in the browser — it's just packaged for the App Store / Play Store."],
            P()["Open Counter to see live, in-process state updates over the native WebView bridge."]
        ];
}
