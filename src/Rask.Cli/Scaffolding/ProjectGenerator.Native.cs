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

        // Shared files (both hosts): the multi-targeted csproj, both platform manifests and the iOS entry
        // point. The Android manifest carries an inline IsLocal conditional (the notification permission the
        // native backends need) resolved per host — see NativeAndroidManifest.
        var files = new List<(string Path, string Content)>
        {
            ($"{NameToken}.csproj", NativeCsproj(version)),
            ("Platforms/Android/AndroidManifest.xml", NativeAndroidManifest(isLocal)),
            ("Platforms/iOS/Info.plist", NativeInfoPlist()),
            ("Platforms/iOS/Main.cs", NativeMainCs),
        };

        if (isLocal)
        {
            // Native + Local: the component tree (App shell in Features/Shared, welcome screen in Features/Home)
            // plus the in-process platform heads, each of which boots a NativeAppHost + RunLocalAsync<App>.
            files.Add(("Features/Shared/App.cs", NativeAppShell));
            files.Add(("Features/Home/HomePage.cs", NativeHomePage));
            files.Add(("Platforms/iOS/AppDelegate.cs", NativeIosAppDelegate));
            files.Add(("Platforms/Android/MainActivity.cs", NativeAndroidMainActivity));
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
            component code (App.cs) compiles for both, and each platform head under
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

    private static string NativeInfoPlist() =>
        """
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
        	<dict/>

        	<key>UISupportedInterfaceOrientations</key>
        	<array>
        		<string>UIInterfaceOrientationPortrait</string>
        		<string>UIInterfaceOrientationLandscapeLeft</string>
        		<string>UIInterfaceOrientationLandscapeRight</string>
        	</array>
        </dict>
        </plist>

        """;

    // AndroidManifest.xml carries an inline `<!--#if (IsLocal) -->` block (the POST_NOTIFICATIONS permission
    // the native backends need). Resolved per host, markers stripped.
    private static string NativeAndroidManifest(bool isLocal)
    {
        var permissionsBlock = isLocal
            ? "\n\t<!-- Required by NativeNotifications (the INotifications backend) on API 33+. Remove if unused. -->"
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
        // Shared with the `rask dev` native refusal, which points at these same two commands — the
        // two would otherwise drift and start telling people different things.
        foreach (var line in Commands.NativeRunCommands.Lines)
        {
            steps.Append("  ").Append(line).Append('\n');
        }

        return steps.ToString();
    }

    // ---- native-only template files ----

    // The root shell, in the Features/Shared bucket. A native page is a small COMPOSED tree: the native bars
    // (NativeHeaderBar / NativeTabBar) as siblings of a NativeWebView, which hosts the ordinary page shell
    // (Doctype/Html/Head/Body, RASK021). The native host projects the bars to REAL platform chrome — a
    // UINavigationBar + UITabBar on iOS, a top bar + bottom tab bar on Android — and serializes the
    // NativeWebView's HTML into the WebView between them.
    private const string NativeAppShell =
        """
        using Rask.Core.Routing;

        // NativeHeaderBar / NativeWebView factories come from a global using the generator emits automatically
        // for any project referencing Rask.Native — no `using static` needed here.

        namespace Company.RaskServer.Features.Shared;

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
                ]

                // Add a real native bottom tab bar here once you have somewhere to navigate:
                //   NativeTabBar(Tabs: [NativeTab(Title: "Home", Icon: NativeIcon.Home, To: Routes.HomePage())])
                // Tapping a tab routes to its type-safe To:; the framework highlights the matching route.
            ];
        }

        """;

    // The welcome screen, its own Features/Home slice — a new native project already models the CLI's
    // "screens are feature slices" convention.
    private const string NativeHomePage =
        """
        using Rask.Core.Routing;

        namespace Company.RaskServer.Features.Home;

        [Route("/")]
        public sealed class HomePage : Component
        {
            protected override Component? Render() =>
                Div(Style: "padding:1.25rem;font-family:system-ui,-apple-system,sans-serif")[
                    H1(Style: "font-size:1.5rem;margin:0 0 .5rem")["Hello, Rask! 👋"],
                    P(Style: "margin:0 0 1rem;color:#374151")["Your native app is ready. Scaffold the rest with the rask CLI:"],
                    Ul(Style: "margin:0 0 1rem;padding-left:1.1rem;line-height:1.75;color:#374151")[
                        Li()[Code()["rask generate page About"], " — a routed page"],
                        Li()[Code()["rask generate component Card"], " — a reusable component"]
                    ],
                    P(Style: "margin:0;font-size:.9rem;color:#6b7280")[
                        "Edit this page in ",
                        Code()["HomePage.cs"],
                        " — drop a ",
                        Code()["HomePage.css"],
                        " beside it and its rules are scoped to this page. Full guides at ",
                        A(Href: "https://github.com/pal-tamas/rask")["the Rask docs"],
                        "."
                    ]
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
        using Company.RaskServer.Features.Shared;
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

    private const string NativeAndroidMainActivity =
        """
        using Android.App;
        using Android.Content.PM;
        using Android.OS;
        using Company.RaskServer.Features.Shared;
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
}
