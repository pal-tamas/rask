using System.Text;

namespace Rask.Cli.Scaffolding;

// The native template: a WebView-hybrid iOS + Android app.
internal static partial class ProjectGenerator
{
    /// <summary>
    /// Generates the <c>native</c> template (a WebView-hybrid iOS + Android app hosting a Rask app) into
    /// <paramref name="targetDirectory"/>. <paramref name="host"/> is <c>"local"</c> (the Rask component code
    /// runs in-process on the device) or <c>"server"</c> (a thin native shell over a remote Rask Server).
    /// </summary>
    public static ScaffoldResult GenerateNative(string targetDirectory, string name, string host, string version)
    {
        var isLocal = string.Equals(host, "local", StringComparison.Ordinal);

        // Shared files (both hosts): the multi-targeted csproj, both platform manifests, the iOS entry point,
        // and the docs. The platform manifests carry an inline IsLocal conditional (the native device
        // permissions) resolved per host — see NativeInfoPlist / NativeAndroidManifest.
        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.csproj", NativeCsproj(version)),
            ("Platforms/Android/AndroidManifest.xml", NativeAndroidManifest(isLocal)),
            ("Platforms/iOS/Info.plist", NativeInfoPlist(isLocal)),
            ("Platforms/iOS/Main.cs", NativeMainCs),
            ("README.md", NativeReadme),
            ("AGENTS.md", NativeAgents),
        };

        if (isLocal)
        {
            // Native + Local: the shared component tree plus the in-process platform heads (each boots a
            // NativeAppHost + RunLocalAsync<App>) and the native geolocation backends they register.
            files.Add(("App.cs", NativeApp));
            files.Add(("HomePage.cs", NativeHomePage));
            files.Add(("Counter.cs", NativeCounter));
            files.Add(("Platforms/iOS/AppDelegate.cs", NativeIosAppDelegate));
            files.Add(("Platforms/iOS/NativeGeolocation.cs", NativeIosGeolocation));
            files.Add(("Platforms/Android/MainActivity.cs", NativeAndroidMainActivity));
            files.Add(("Platforms/Android/NativeGeolocation.cs", NativeAndroidGeolocation));
        }
        else
        {
            // Native + Server: a thin shell — just the two platform heads that point RaskServerWebView at a
            // remote Rask Server. No shared component code runs on the device.
            files.Add(("Platforms/iOS/ServerAppDelegate.cs", NativeIosServerAppDelegate));
            files.Add(("Platforms/Android/ServerActivity.cs", NativeAndroidServerActivity));
        }

        var scaffoldFiles = Materialize(targetDirectory, name, files);

        return new ScaffoldResult(scaffoldFiles, NativeNextSteps(name, host))
        {
            Packages = ["Rask.Native"],
        };
    }

    private static string NativeCsproj(string version) =>
        $"""
        <Project Sdk="Microsoft.NET.Sdk">

          <!--
            A native iOS + Android app that hosts a Rask app in-process inside a platform WebView (the WebView
            hybrid — C# runs natively, the view is a WebView). Multi-targets the two native TFMs; the shared
            component code (App.cs / HomePage.cs / Counter.cs) compiles for both, and each platform head under
            Platforms/ provides the INativeWebView implementation for its WebView control.

            Requires the iOS and/or Android SDK workloads:
                dotnet workload install ios android
            Run on a simulator/emulator:
                dotnet build -t:Run -f net10.0-android
                dotnet build -t:Run -f net10.0-ios
          -->
          <PropertyGroup>
            <TargetFrameworks>net10.0-ios;net10.0-android</TargetFrameworks>
            <OutputType>Exe</OutputType>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RootNamespace>Company.RaskServer</RootNamespace>
            <ApplicationTitle>Rask App</ApplicationTitle>
            <ApplicationId>com.example.raskapp</ApplicationId>
            <ApplicationDisplayVersion>1.0</ApplicationDisplayVersion>
            <ApplicationVersion>1</ApplicationVersion>
          </PropertyGroup>

          <PropertyGroup Condition="$(TargetFramework.Contains('-ios'))">
            <SupportedOSPlatformVersion>15.0</SupportedOSPlatformVersion>
          </PropertyGroup>

          <!-- Wire Platforms/iOS/Info.plist as THE app manifest (a None item whose Link filename is "Info.plist"
               is how .NET iOS finds it). Its UILaunchScreen makes the app render full-screen at the device's native
               resolution — without a launch screen iOS runs it letterboxed (black bars + everything scaled up). -->
          <ItemGroup Condition="$(TargetFramework.Contains('-ios'))">
            <None Remove="Platforms/iOS/Info.plist"/>
            <None Include="Platforms/iOS/Info.plist" Link="Info.plist"/>
          </ItemGroup>

          <PropertyGroup Condition="$(TargetFramework.Contains('-android'))">
            <SupportedOSPlatformVersion>24.0</SupportedOSPlatformVersion>
            <AndroidManifest>Platforms/Android/AndroidManifest.xml</AndroidManifest>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="Rask.Native" Version="{version}"/>
          </ItemGroup>

          <!-- Each platform head compiles only for its own TFM. -->
          <ItemGroup Condition="!$(TargetFramework.Contains('-ios'))">
            <Compile Remove="Platforms/iOS/**/*.cs"/>
          </ItemGroup>
          <ItemGroup Condition="!$(TargetFramework.Contains('-android'))">
            <Compile Remove="Platforms/Android/**/*.cs"/>
          </ItemGroup>

        </Project>

        """;

    // Info.plist carries an inline `<!--#if (IsLocal) -->` block (the NSLocationWhenInUseUsageDescription for
    // the native geolocation backend). Include it only for the local host; strip the marker comment lines in
    // both cases — the same resolution the template engine did for the `#if`.
    private static string NativeInfoPlist(bool isLocal)
    {
        var locationBlock = isLocal
            ? "\n\t<!-- Shown in the iOS location prompt for NativeGeolocation (the IGeolocation backend). -->"
              + "\n\t<key>NSLocationWhenInUseUsageDescription</key>"
              + "\n\t<string>Shows your current location in the app.</string>"
            : "";

        return $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
        	<key>CFBundleDisplayName</key>
        	<string>Rask App</string>
        	<key>CFBundleIdentifier</key>
        	<string>com.example.raskapp</string>
        	<key>CFBundleShortVersionString</key>
        	<string>1.0</string>
        	<key>CFBundleVersion</key>
        	<string>1</string>
        	<key>LSRequiresIPhoneOS</key>
        	<true/>
        	<key>MinimumOSVersion</key>
        	<string>15.0</string>
        	<key>UIDeviceFamily</key>
        	<array>
        		<integer>1</integer>
        		<integer>2</integer>
        	</array>
        	<!-- A launch screen is REQUIRED for iOS to render at the device's native resolution; without one the
        	     app runs letterboxed at a legacy size (black bars + everything scaled up). An empty UILaunchScreen
        	     dict opts into full-screen with a blank system launch screen (no storyboard file needed). -->
        	<key>UILaunchScreen</key>
        	<dict/>{locationBlock}

        	<key>UISupportedInterfaceOrientations</key>
        	<array>
        		<string>UIInterfaceOrientationPortrait</string>
        		<string>UIInterfaceOrientationLandscapeLeft</string>
        		<string>UIInterfaceOrientationLandscapeRight</string>
        	</array>
        </dict>
        </plist>

        """;
    }

    // AndroidManifest.xml carries the same inline `<!--#if (IsLocal) -->` block (the ACCESS_FINE_LOCATION +
    // POST_NOTIFICATIONS permissions the native backends need). Resolved per host, markers stripped.
    private static string NativeAndroidManifest(bool isLocal)
    {
        var permissionsBlock = isLocal
            ? "\n\t<!-- Required by NativeGeolocation (the IGeolocation backend). Remove if you don't use native location. -->"
              + "\n\t<uses-permission android:name=\"android.permission.ACCESS_FINE_LOCATION\"/>"
              + "\n\t<!-- Required by NativeNotifications (the INotifications backend) on API 33+. Remove if unused. -->"
              + "\n\t<uses-permission android:name=\"android.permission.POST_NOTIFICATIONS\"/>"
            : "";

        return $"""
        <?xml version="1.0" encoding="utf-8"?>
        <manifest xmlns:android="http://schemas.android.com/apk/res/android">
        	<uses-permission android:name="android.permission.INTERNET"/>{permissionsBlock}
        	<application android:label="Rask App" android:allowBackup="true" android:supportsRtl="true"/>
        </manifest>

        """;
    }

    private static string NativeNextSteps(string name, string host)
    {
        var steps = new StringBuilder();
        steps.Append("Created ").Append(name).Append(" (Rask native mobile app, ").Append(host).Append(" host).\n\nNext steps:\n");
        steps.Append("  cd ").Append(name).Append('\n');
        steps.Append("  dotnet workload install ios android   # once, if not already installed\n");
        steps.Append("  dotnet build -t:Run -f net10.0-android     # Android emulator\n");
        steps.Append("  dotnet build -t:Run -f net10.0-ios         # iOS simulator (macOS + Xcode)\n");
        return steps.ToString();
    }

    // ---- native-only template files ----

    private const string NativeApp =
        """
        using static Company.RaskServer.Routes;
        using NativeIcon = Rask.Native.Components.NativeIcon;

        // NativeHeaderBar / NativeTabBar / NativeTab / NativeWebView factories come from a global using the generator
        // emits automatically for any project referencing Rask.Native — no `using static` needed here.

        namespace Company.RaskServer;

        // The root component. A native page is a small COMPOSED tree: the native bars (NativeHeaderBar / NativeTabBar)
        // as siblings of a NativeWebView, which hosts the ordinary page shell (Doctype/Html/Head/Body, RASK021). The
        // native host projects the bars to REAL platform chrome — a UINavigationBar + UITabBar on iOS, a top bar +
        // bottom tab bar on Android — and serializes the NativeWebView's HTML into the WebView between them.
        public sealed class App : Component
        {
            protected override Component? Head =>
            [
                Title()["Rask App"],
                Meta("utf-8"),
                Meta(Name: "viewport", Content: "width=device-width, initial-scale=1, viewport-fit=cover")
            ];

            protected override Component? Render() =>
            [
                // Real native top bar. Opt in by hosting webView.ChromeView + registering the head as INativeChrome —
                // see Platforms/iOS/AppDelegate.cs and Platforms/Android/MainActivity.cs.
                NativeHeaderBar(Title: "Rask App"),

                // The HTML surface — its children are the normal page shell, morphed into the platform WebView.
                NativeWebView()[
                    Doctype(),
                    Html("en")[
                        Head(),
                        // Pad the body by the device safe-area insets so content clears the status bar / notch /
                        // home indicator (the boot shell requests an edge-to-edge viewport with viewport-fit=cover).
                        Body(Style: "margin:0;padding:env(safe-area-inset-top) env(safe-area-inset-right) " +
                                    "env(safe-area-inset-bottom) env(safe-area-inset-left)")[
                            Router()
                        ]
                    ]
                ],

                // Real native bottom tab bar — primary navigation. Tapping a tab routes to its type-safe To:.
                NativeTabBar(
                    Tabs:
                    [
                        NativeTab(Title: "Home", Icon: NativeIcon.Home, To: HomePage()),
                        NativeTab(Title: "Counter", Icon: NativeIcon.Add, To: Counter())
                    ])
                // Selected is omitted — the framework highlights the tab matching the current route.
            ];
        }

        """;

    private const string NativeHomePage =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer;

        [Route("/")]
        public sealed class HomePage : Component
        {
            protected override Component? Render() =>
                Div(Style: "padding:1rem;font-family:system-ui,-apple-system,sans-serif")[
                    H1()["Hello, Rask — natively! 👋"],
                    P()["This is a native iOS/Android app. The same C# component code runs here as on the "
                        + "server and in the browser — it's just packaged for the App Store / Play Store."],
                    P()["Scaffold the rest with the rask CLI:"],
                    Ul()[
                        Li()[Code()["rask generate feature Product Name:string Price:decimal"], " — a full CRUD slice"],
                        Li()[Code()["rask generate page About"], " — a routed page"],
                        Li()[Code()["rask generate component Card"], " — a reusable component"]
                    ],
                    P()["Open Counter to see live, in-process state updates over the native WebView bridge."]
                ];
        }

        """;

    private const string NativeCounter =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer;

        [Route("/counter")]
        public sealed class Counter : Component
        {
            private int _count;

            protected override Component? Render() =>
            [
                H1()["Counter"],
                P()[$"Current count: {_count}"],
                Button(OnClick: () => _count++)["Click me"]
            ];
        }

        """;

    private const string NativeMainCs =
        """
        using UIKit;

        namespace Company.RaskServer;

        // iOS entry point. Hands control to UIKit with our AppDelegate.
        public static class Program
        {
            private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
        }

        """;

    private const string NativeIosAppDelegate =
        """
        using Foundation;
        using Microsoft.Extensions.DependencyInjection;
        using Rask.Client.Browser;
        using Rask.Core.Browser;
        using Rask.Native;
        using UIKit;

        namespace Company.RaskServer;

        [Register("AppDelegate")]
        public class AppDelegate : UIApplicationDelegate
        {
            public override UIWindow? Window { get; set; }
            private NativeApp? _app;

            public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
            {
                Window = new UIWindow(UIScreen.MainScreen.Bounds);

                var webView = new RaskWkWebView();
                // Host ChromeView (the container that lays the native header/footer bars around the WebView) rather
                // than the bare WebView, so the App's composed NativeHeaderBar/NativeTabBar become a real
                // UINavigationBar/UITabBar. The INativeChrome backend is registered below; without it the bars render
                // nothing (the WebView fills the screen), so a native app that navigates via the tab bar should keep it.
                Window.RootViewController = new UIViewController { View = webView.ChromeView };
                Window.MakeKeyAndVisible();

                // Wire the in-process session BEFORE loading the shell, so the session is ready to receive the
                // client's `ready` handshake and push the first frame. Native + Local mode.
                _ = StartAsync(webView);
                return true;
            }

            private async Task StartAsync(RaskWkWebView webView)
            {
                var host = NativeAppHost.CreateDefault();
                // Native device backends: override Rask.Native's JS-backed defaults with the platform APIs. Register
                // any native backend on host.Services before RunLocalAsync — the last registration wins. See
                // docs/native.md "Native device backends".
                host.Services.AddSingleton<IShare>(_ => new NativeShare(() => Window?.RootViewController));  // share sheet
                host.Services.AddSingleton<IGeolocation>(_ => new NativeGeolocation());                     // CoreLocation
                host.Services.AddSingleton<INotifications>(_ => new NativeNotifications());                 // UNUserNotificationCenter
                host.Services.AddSingleton<IBadge>(_ => new NativeBadge());                                 // app-icon badge
                host.Services.AddSingleton<INativeChrome>(webView);                                         // native header/footer bars
                // host.Services.AddSingleton<IMyService, MyService>();   // register app services here
                _app = await host.RunLocalAsync<App>(webView);
                webView.LoadShell();
            }

            public override void WillTerminate(UIApplication application) => _ = _app?.DisposeAsync();
        }

        """;

    private const string NativeIosServerAppDelegate =
        """
        using Foundation;
        using Rask.Native;
        using UIKit;

        namespace Company.RaskServer;

        // Native + Server mode (iOS): a thin native shell over a REMOTE Rask Server. The WebView machinery — the
        // native capability bridge (so the remote page's Shareable / IShare reach the device's native backends),
        // the trusted-origin gating, and the off-origin-to-Safari diversion — lives in Rask.Native's
        // RaskServerViewController. This head just points it at your server and supplies the native share backend.
        [Register("AppDelegate")]
        public class AppDelegate : UIApplicationDelegate
        {
            // Your remote Rask Server (a real deployment is https).
            private static readonly Uri ServerOrigin = new("https://app.example.com/");

            public override UIWindow? Window { get; set; }

            public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
            {
                var shell = NativeAppHost.ConnectToServer(ServerOrigin);
                Window = new UIWindow(UIScreen.MainScreen.Bounds)
                {
                    RootViewController = new RaskServerViewController(
                        shell.ServerBaseUrl, new NativeShare(() => Window?.RootViewController))
                };
                Window.MakeKeyAndVisible();
                return true;
            }
        }

        """;

    private const string NativeIosGeolocation =
        """
        using CoreLocation;
        using Foundation;
        using Rask.Core.Browser;
        using UIKit;

        namespace Company.RaskServer;

        // Native iOS backend for IGeolocation — CoreLocation instead of the WebView's navigator.geolocation.
        // Registered in AppDelegate before RunLocalAsync (overrides Rask's JS-backed default). Native location
        // gives the real iOS permission prompt (Info.plist NSLocationWhenInUseUsageDescription) and CLLocationManager
        // accuracy, and works even where the WebView's geolocation is restricted.
        //
        // This is the same framework-default → native-head-override pattern as NativeShare, for a request/response
        // (+ subscription) capability: GetCurrentPositionAsync resolves one fix; WatchAsync streams them.
        public sealed class NativeGeolocation : IGeolocation
        {
            public ValueTask<GeolocationPosition> GetCurrentPositionAsync(GeolocationOptions? options = null)
            {
                var tcs = new TaskCompletionSource<GeolocationPosition>();
                UIApplication.SharedApplication.InvokeOnMainThread(() =>
                {
                    // The delegate keeps the manager alive until it fires (or faults), then tears it down.
                    var manager = new CLLocationManager
                    {
                        DesiredAccuracy = (options?.EnableHighAccuracy ?? false)
                            ? CLLocation.AccuracyBest
                            : CLLocation.AccuracyHundredMeters
                    };
                    manager.Delegate = new OneShotDelegate(manager, tcs);
                    manager.RequestWhenInUseAuthorization();
                    RequestWhenAuthorized(manager);
                });
                return new ValueTask<GeolocationPosition>(tcs.Task);
            }

            public ValueTask<IAsyncDisposable> WatchAsync(
                Func<GeolocationPosition, Task> onPosition, GeolocationOptions? options = null)
            {
                var watch = new Watch(onPosition, options?.EnableHighAccuracy ?? false);
                return new ValueTask<IAsyncDisposable>(watch);
            }

            // RequestLocation only works once authorization is at least WhenInUse; if it's still NotDetermined the
            // delegate re-requests from DidChangeAuthorization once the user answers the prompt.
            private static void RequestWhenAuthorized(CLLocationManager manager)
            {
                if (manager.AuthorizationStatus is CLAuthorizationStatus.AuthorizedWhenInUse
                    or CLAuthorizationStatus.AuthorizedAlways)
                {
                    manager.RequestLocation();
                }
            }

            private sealed class OneShotDelegate(CLLocationManager manager, TaskCompletionSource<GeolocationPosition> tcs)
                : CLLocationManagerDelegate
            {
                public override void AuthorizationChanged(CLLocationManager mgr, CLAuthorizationStatus status)
                {
                    if (status is CLAuthorizationStatus.AuthorizedWhenInUse or CLAuthorizationStatus.AuthorizedAlways)
                    {
                        mgr.RequestLocation();
                    }
                    else if (status is CLAuthorizationStatus.Denied or CLAuthorizationStatus.Restricted)
                    {
                        Finish(() => tcs.TrySetException(new InvalidOperationException("Location permission denied.")));
                    }
                }

                public override void LocationsUpdated(CLLocationManager mgr, CLLocation[] locations)
                {
                    if (locations.Length > 0)
                    {
                        Finish(() => tcs.TrySetResult(Map(locations[^1])));
                    }
                }

                public override void Failed(CLLocationManager mgr, NSError error) =>
                    Finish(() => tcs.TrySetException(new InvalidOperationException(error.LocalizedDescription)));

                private void Finish(Action complete)
                {
                    manager.Delegate = null;   // release the retain cycle so the manager can be collected
                    complete();
                }
            }

            // A watchPosition subscription: streams every CLLocation update until disposed.
            private sealed class Watch : CLLocationManagerDelegate, IAsyncDisposable
            {
                private readonly Func<GeolocationPosition, Task> _onPosition;
                private CLLocationManager? _manager;

                public Watch(Func<GeolocationPosition, Task> onPosition, bool highAccuracy)
                {
                    _onPosition = onPosition;
                    UIApplication.SharedApplication.InvokeOnMainThread(() =>
                    {
                        _manager = new CLLocationManager
                        {
                            DesiredAccuracy = highAccuracy ? CLLocation.AccuracyBest : CLLocation.AccuracyHundredMeters,
                            Delegate = this
                        };
                        _manager.RequestWhenInUseAuthorization();
                        if (_manager.AuthorizationStatus is CLAuthorizationStatus.AuthorizedWhenInUse
                            or CLAuthorizationStatus.AuthorizedAlways)
                        {
                            _manager.StartUpdatingLocation();
                        }
                    });
                }

                public override void AuthorizationChanged(CLLocationManager mgr, CLAuthorizationStatus status)
                {
                    if (status is CLAuthorizationStatus.AuthorizedWhenInUse or CLAuthorizationStatus.AuthorizedAlways)
                    {
                        mgr.StartUpdatingLocation();
                    }
                }

                public override void LocationsUpdated(CLLocationManager mgr, CLLocation[] locations)
                {
                    if (locations.Length > 0)
                    {
                        _ = _onPosition(Map(locations[^1]));
                    }
                }

                public ValueTask DisposeAsync()
                {
                    UIApplication.SharedApplication.InvokeOnMainThread(() =>
                    {
                        _manager?.StopUpdatingLocation();
                        if (_manager is not null)
                        {
                            _manager.Delegate = null;
                        }
                    });
                    return default;
                }
            }

            private static GeolocationPosition Map(CLLocation l) => new(
                Latitude: l.Coordinate.Latitude,
                Longitude: l.Coordinate.Longitude,
                Accuracy: l.HorizontalAccuracy,
                Altitude: l.VerticalAccuracy > 0 ? l.EllipsoidalAltitude : null,
                AltitudeAccuracy: l.VerticalAccuracy > 0 ? l.VerticalAccuracy : null,
                Heading: l.Course >= 0 ? l.Course : null,
                Speed: l.Speed >= 0 ? l.Speed : null,
                TimestampMs: (l.Timestamp.SecondsSince1970) * 1000.0);
        }

        """;

    private const string NativeAndroidMainActivity =
        """
        using Android.App;
        using Android.Content.PM;
        using Android.OS;
        using Microsoft.Extensions.DependencyInjection;
        using Rask.Client.Browser;
        using Rask.Core.Browser;
        using Rask.Native;

        namespace Company.RaskServer;

        [Activity(Label = "Rask App", MainLauncher = true, Exported = true,
            Theme = "@android:style/Theme.Material.Light.NoActionBar")]
        public class MainActivity : Activity
        {
            private NativeApp? _app;
            private RaskAndroidWebView? _webView;

            protected override void OnCreate(Bundle? savedInstanceState)
            {
                base.OnCreate(savedInstanceState);

                // NativeGeolocation (registered below) needs the runtime location grant — request it up front so a
                // later GetCurrentPositionAsync finds it granted (declare ACCESS_FINE_LOCATION in AndroidManifest.xml).
                if (CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) != Permission.Granted)
                {
                    RequestPermissions([Android.Manifest.Permission.AccessFineLocation], 100);
                }

                // NativeNotifications (registered below) needs the POST_NOTIFICATIONS runtime grant on API 33+ —
                // request it up front so a later ShowAsync posts (declare POST_NOTIFICATIONS in AndroidManifest.xml).
                if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
                    CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Permission.Granted)
                {
                    RequestPermissions([Android.Manifest.Permission.PostNotifications], 101);
                }

                _webView = new RaskAndroidWebView(this);
                // Host ChromeView (the container that lays the native header/footer bars around the WebView) rather
                // than the bare WebView, so the App's composed NativeHeaderBar/NativeTabBar become real top/bottom bars.
                // The INativeChrome backend is registered below; without it the bars render nothing (the WebView fills
                // the screen), so a native app that navigates via the tab bar should keep that registration.
                SetContentView(_webView.ChromeView);

                // Wire the in-process session before loading the shell so it's ready for the client's `ready`
                // handshake. Native + Local mode.
                _ = StartAsync(_webView);
            }

            private async Task StartAsync(RaskAndroidWebView webView)
            {
                var host = NativeAppHost.CreateDefault();
                // Native device backends: override Rask.Native's JS-backed defaults with the platform APIs. Register
                // any native backend on host.Services before RunLocalAsync — the last registration wins. See
                // docs/native.md "Native device backends".
                host.Services.AddSingleton<IShare>(_ => new NativeShare(this));                  // OS share sheet
                host.Services.AddSingleton<IGeolocation>(_ => new NativeGeolocation(this));       // LocationManager
                host.Services.AddSingleton<INotifications>(_ => new NativeNotifications(this));   // NotificationManager
                host.Services.AddSingleton<IBadge>(_ => new NativeBadge(this));                   // app badge notification
                host.Services.AddSingleton<INativeChrome>(webView);                              // native header/footer bars
                // host.Services.AddSingleton<IMyService, MyService>();   // register app services here
                _app = await host.RunLocalAsync<App>(webView);
                webView.LoadShell();
            }

            // Forward runtime-permission results so NativeNotifications' RequestPermissionAsync can await the grant.
            public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
            {
                NativePermissions.OnResult(requestCode, grantResults);
                base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            }

            protected override void OnDestroy()
            {
                _ = _app?.DisposeAsync();
                base.OnDestroy();
            }
        }

        """;

    private const string NativeAndroidServerActivity =
        """
        using Android.App;
        using Android.OS;
        using Rask.Native;

        namespace Company.RaskServer;

        // Native + Server mode: a thin native shell over a REMOTE Rask Server. The WebView machinery — the native
        // capability bridge (so the remote page's Shareable / IShare reach the device's native backends), the
        // trusted-origin gating, and the off-origin-to-system-browser diversion — lives in Rask.Native's
        // RaskServerWebView. This head just points it at your server and supplies the native share backend.
        [Activity(Label = "Rask App", MainLauncher = true, Exported = true,
            Theme = "@android:style/Theme.Material.Light.NoActionBar")]
        public class ServerActivity : Activity
        {
            // Your remote Rask Server. (Android emulator → host machine is http://10.0.2.2:<port>; a real
            // deployment is https. For http during development, allow cleartext in AndroidManifest.xml.)
            private static readonly Uri ServerOrigin = new("https://app.example.com/");

            protected override void OnCreate(Bundle? savedInstanceState)
            {
                base.OnCreate(savedInstanceState);
                SetContentView(RaskServerWebView.Create(this, ServerOrigin, new NativeShare(this)));
            }
        }

        """;

    private const string NativeAndroidGeolocation =
        """
        using Android.App;
        using Android.Content;
        using Android.Content.PM;
        using Android.Locations;
        using Android.OS;
        using Android.Runtime;
        using Rask.Core.Browser;

        namespace Company.RaskServer;

        // Native Android backend for IGeolocation — the platform LocationManager instead of the WebView's
        // navigator.geolocation. Registered in MainActivity before RunLocalAsync (overrides Rask's JS-backed
        // default). Needs ACCESS_FINE_LOCATION in AndroidManifest.xml + a runtime grant (MainActivity requests it
        // on launch). Same framework-default → native-head-override pattern as NativeShare, for a request/response
        // (+ subscription) capability.
        public sealed class NativeGeolocation(Activity activity) : IGeolocation
        {
            public ValueTask<GeolocationPosition> GetCurrentPositionAsync(GeolocationOptions? options = null)
            {
                var tcs = new TaskCompletionSource<GeolocationPosition>();
                activity.RunOnUiThread(() =>
                {
                    if (!HasPermission())
                    {
                        tcs.TrySetException(new InvalidOperationException("Location permission not granted."));
                        return;
                    }

                    var manager = LocationManagerFor();
                    var provider = BestProvider(manager, options?.EnableHighAccuracy ?? false);
                    if (provider is null)
                    {
                        tcs.TrySetException(new InvalidOperationException("No enabled location provider."));
                        return;
                    }

                    // One-shot: subscribe, take the first fix, then unsubscribe.
                    OneShotListener? listener = null;
                    listener = new OneShotListener(fix =>
                    {
                        manager.RemoveUpdates(listener!);
                        tcs.TrySetResult(fix);
                    });
                    manager.RequestLocationUpdates(provider, 0L, 0f, listener, Looper.MainLooper);
                });
                return new ValueTask<GeolocationPosition>(tcs.Task);
            }

            public ValueTask<IAsyncDisposable> WatchAsync(
                Func<GeolocationPosition, Task> onPosition, GeolocationOptions? options = null)
            {
                var watch = new Watch(activity, HasPermission, onPosition, options?.EnableHighAccuracy ?? false);
                return new ValueTask<IAsyncDisposable>(watch);
            }

            private bool HasPermission() =>
                activity.CheckSelfPermission(Android.Manifest.Permission.AccessFineLocation) == Permission.Granted;

            private LocationManager LocationManagerFor() =>
                (LocationManager)activity.GetSystemService(Context.LocationService)!;

            private static string? BestProvider(LocationManager manager, bool highAccuracy)
            {
                // Prefer GPS for high accuracy; otherwise take whatever's enabled (network is faster/cheaper).
                var order = highAccuracy
                    ? new[] { LocationManager.GpsProvider, LocationManager.NetworkProvider }
                    : new[] { LocationManager.NetworkProvider, LocationManager.GpsProvider };
                foreach (var p in order)
                {
                    if (manager.IsProviderEnabled(p))
                    {
                        return p;
                    }
                }

                return null;
            }

            private static GeolocationPosition Map(Location l) => new(
                Latitude: l.Latitude,
                Longitude: l.Longitude,
                Accuracy: l.HasAccuracy ? l.Accuracy : 0,
                Altitude: l.HasAltitude ? l.Altitude : null,
                AltitudeAccuracy: l is { HasVerticalAccuracy: true } ? l.VerticalAccuracyMeters : null,
                Heading: l.HasBearing ? l.Bearing : null,
                Speed: l.HasSpeed ? l.Speed : null,
                TimestampMs: l.Time);

            private sealed class OneShotListener(Action<GeolocationPosition> onFix) : Java.Lang.Object, ILocationListener
            {
                public void OnLocationChanged(Location location) => onFix(Map(location));

                public void OnProviderDisabled(string provider) { }

                public void OnProviderEnabled(string provider) { }

                public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras) { }
            }

            private sealed class Watch : Java.Lang.Object, ILocationListener, IAsyncDisposable
            {
                private readonly Activity _activity;
                private readonly Func<GeolocationPosition, Task> _onPosition;
                private LocationManager? _manager;

                public Watch(Activity activity, Func<bool> hasPermission, Func<GeolocationPosition, Task> onPosition, bool highAccuracy)
                {
                    _activity = activity;
                    _onPosition = onPosition;
                    activity.RunOnUiThread(() =>
                    {
                        if (!hasPermission())
                        {
                            return;
                        }

                        _manager = (LocationManager)activity.GetSystemService(Context.LocationService)!;
                        var provider = BestProvider(_manager, highAccuracy);
                        if (provider is not null)
                        {
                            _manager.RequestLocationUpdates(provider, 1000L, 0f, this, Looper.MainLooper);
                        }
                    });
                }

                public void OnLocationChanged(Location location) => _ = _onPosition(Map(location));

                public void OnProviderDisabled(string provider) { }

                public void OnProviderEnabled(string provider) { }

                public void OnStatusChanged(string? provider, [GeneratedEnum] Availability status, Bundle? extras) { }

                public ValueTask DisposeAsync()
                {
                    _activity.RunOnUiThread(() => _manager?.RemoveUpdates(this));
                    return default;
                }
            }
        }

        """;

    private const string NativeReadme =
        """
        # Company.RaskServer

        A **native iOS + Android** app built with [Rask](https://github.com/pal-tamas/rask). The same C#
        component code that runs on the Rask server and in the browser runs here too — packaged as a real,
        store-distributable mobile app. It's a **WebView hybrid**: your C# runs natively on the device, and the
        UI renders in a platform WebView driven by Rask's live diff pipeline.

        ## Prerequisites

        Install the iOS and/or Android SDK workloads:

        ```bash
        dotnet workload install ios android
        ```

        ## Run

        ```bash
        dotnet build -t:Run -f net10.0-android     # Android emulator
        dotnet build -t:Run -f net10.0-ios         # iOS simulator (macOS + Xcode)
        ```

        ## What's here

        - `App.cs`, `HomePage.cs`, `Counter.cs` — your Rask components (shared across both platforms). `App.cs`
          pads `Body` by `env(safe-area-inset-*)` (with a `viewport-fit=cover` viewport) so content clears the
          notch / status bar / home indicator — keep both if you restructure it.
        - `Platforms/iOS/` — the iOS head: `AppDelegate` boots a `NativeAppHost`, and `RaskWkWebView` implements
          `INativeWebView` over a `WKWebView` (custom `raskapp://` scheme + script-message bridge).
        - `Platforms/Android/` — the Android head: `MainActivity` boots the host, and `RaskAndroidWebView`
          implements `INativeWebView` over an `android.webkit.WebView` (asset-serving `WebViewClient` + a
          `@JavascriptInterface` bridge).

        Register app services on `host.Services` in the platform head's `StartAsync`, then add pages as
        `[Route("/…")]` components — exactly as in any other Rask app.

        ## Native + Server mode

        To make the app a thin native shell over a remote Rask Server instead of running in-process, point the
        WebView at the server URL:

        ```csharp
        var shell = NativeAppHost.ConnectToServer(new Uri("https://app.example.com/"));
        // iOS:     webView.View.LoadRequest(new NSUrlRequest(new NSUrl(shell.ServerBaseUrl.ToString())));
        // Android: webView.View... LoadUrl(shell.ServerBaseUrl.ToString());
        ```

        The server serves its own client and connects back over `wss://`; native device APIs remain available to
        the page.

        See the framework docs: [Native mobile apps with Rask](https://github.com/pal-tamas/rask/blob/main/docs/native.md).

        """;

    private const string NativeAgents =
        """
        # AGENTS.md — building this app with an AI assistant

        This is a **Rask** native mobile app (iOS + Android) for .NET 10. Rask is the .NET One Person Framework —
        one C# codebase, one server, every UI surface. This app is a **WebView hybrid**: your C#
        runs natively on the device via `Rask.Native`, and the UI renders in a platform WebView driven by Rask's
        live diff pipeline. The **same component code** as any other Rask host works here. Full docs:
        https://github.com/pal-tamas/rask/tree/main/docs — native specifics: docs/native.md.

        ## Mental model
        - Components are **plain C# classes** deriving from `Component`. Override `Component? Render()` and return
          a tree built with **generated factory methods** — no `.razor`, no JSX. Use factories, never `new`
          (RASK014). Children go through the indexer: `Div()[Span()["hi"], "text"]`. Props are factory params
          (nullable ⇒ optional, non-nullable no-initializer ⇒ required).
        - A page/root component must render the **full shell** `[Doctype(), Html(...)[Head(...), Body(...)]]`
          (RASK021). The framework injects its runtime automatically.
        - Route with `[Route("/path")]`; navigate only from event handlers via the injected `Navigator`. Inject
          services (`HttpClient`, `IJSRuntime`, the typed `Rask.Core.Browser` APIs, your own) through the ctor.

        ## Native structure — don't restructure these
        - **Shared components** (`App.cs`, `HomePage.cs`, `Counter.cs`) compile for both `net10.0-ios` and
          `net10.0-android`. Keep platform-specific types OUT of them. `App.cs` pads `Body` by
          `env(safe-area-inset-*)` (paired with the `viewport-fit=cover` viewport meta) so content clears the
          notch / status bar / home indicator — keep both together if you edit the shell. `App.cs` also declares the
          native header/footer bars (see "Native header & footer bars" below).
        - **Platform heads** live under `Platforms/iOS/` and `Platforms/Android/`. Each boots a
          `NativeAppHost`, calls `host.RunLocalAsync<App>(webView)`, and provides the `INativeWebView`
          implementation for its WebView (`WKWebView` on iOS, `android.webkit.WebView` on Android). Register app
          services on `host.Services` in the head's `StartAsync` before `RunLocalAsync`.
        - **Two modes:** `RunLocalAsync<App>(webView)` runs the app in-process (offline). To be a shell over a
          remote Rask Server instead, `NativeAppHost.ConnectToServer(uri)` and load that URL in the WebView.

        ## Device APIs
        - Inject the typed `Rask.Core.Browser` wrappers (`IGeolocation`, `IClipboard`, `IVibration`,
          `IBrowserStorage`, `INotifications`, `IBadge`, `IWakeLock`, …) — they work through the WebView's JS engine.
        - Sharing: use the headless `Shareable` (`Rask.Core`) to attach share behaviour to your own element, or
          inject `IShare` (`Rask.Client.Browser`) to share from code. Both hit the OS share sheet.
        - **Native backends** override a JS default with real platform code. The head registers one on
          `host.Services` **before `RunLocalAsync`** (last-wins). The template ships `NativeShare` for `IShare`
          (iOS `UIActivityViewController`, Android `Intent.ACTION_SEND`), `NativeGeolocation` for `IGeolocation`
          (iOS `CLLocationManager`, Android `LocationManager`), and `NativeNotifications` / `NativeBadge` for
          `INotifications` / `IBadge` (iOS `UNUserNotificationCenter`, Android `NotificationManager` + a badge
          notification) under `Platforms/`; register your own the same way. Geolocation needs the location
          permission and notifications need `POST_NOTIFICATIONS` on Android 33+ (both already in
          `AndroidManifest.xml` / `Info.plist`; `MainActivity` requests the runtime grants). Further native
          backends (biometrics, push) are a framework work-in-progress.

        ## Native header & footer bars
        - A native page is a small **composed tree**: the native bars (`NativeHeaderBar` / `NativeTabBar` /
          `NativeToolbar`) as siblings of a **`NativeWebView`**, which hosts the ordinary page shell
          (`Doctype`/`Html`/`Head`/`Body`). `App.cs` shows the shape. The bars are ordinary factory-built components —
          compose them in `Render()`, they are not magic base-class slots.
        - The native host projects the bars to **real platform bars** — a `UINavigationBar` + `UITabBar` on iOS, a top
          bar + bottom tab bar on Android — and serializes the `NativeWebView`'s HTML into the WebView between them.
          Build bars from `NativeBarButton` / `NativeTab` / `NativeBackButton` and type-safe `NativeIcon`s. A
          `NativeTab` also takes an optional `Badge` string (unread count) → `UITabBarItem.BadgeValue` / icon overlay.
          `NativeHeaderBar` takes optional `Segments` (shown in place of the title) → a `UISegmentedControl` / button
          row, controlled via `SelectedSegment` + `OnSegmentChanged(int)`. A `NativeMenuButton` bar item (with
          `NativeMenuItem` entries) opens a native overflow pull-down → `UIMenu` (iOS) / `PopupMenu` (Android). A
          `NativeBackButton` (header `Leading`) pops WebView history like hardware Back.
        - **Style the bars** with `NativeColor` (the color sibling of `NativeIcon`: `Hex` / `Rgba` / `Adaptive(light,
          dark)` / `System`) — set `Background` / `Tint` / `TitleColor` per bar (`NativeTabBar` also `UnselectedTint`),
          or register an app-wide `NativeTheme` on `host.Services`. Per-bar wins, then the theme, then the platform
          default; an unset color keeps the OS look.
        - **Opt-in wiring (already done in the heads):** host `webView.ChromeView` (not `webView.View`) and register
          the WebView head as `INativeChrome` on `host.Services` before `RunLocalAsync`. With no `INativeChrome`
          registered the bars are inert (they render nothing; the WebView fills the screen) — fully backward compatible.
        - Tabs navigate their type-safe `To:` route; bar buttons run their `OnClick`. Put a native chrome component
          **inside** the HTML (an element child, or inside `NativeWebView`'s content) and you get **RASK032** — bars
          belong at the layout level, as siblings of `NativeWebView`.
        - Sharing an app across web + native? Branch with `IsNative` / `IsServer` / `IsWasm` / `IsIOS` / `IsAndroid`
          (or `HostShell` / `HostEngine` / `HostPlatform`): compose the native tree under `IsNative`, return the plain
          shell otherwise.

        ## Build & run (needs the iOS/Android SDK workloads)
        ```bash
        dotnet workload install ios android
        dotnet build -t:Run -f net10.0-android     # emulator
        dotnet build -t:Run -f net10.0-ios         # simulator (macOS + Xcode)
        ```

        If you hit a `RASKxxx` compile error, see https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md

        """;
}
