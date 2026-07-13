using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.iOS;

namespace Rask.Native.Appium.Tests;

// On-device E2E for the Native + Local showcase (samples/Rask.Example.Native): Appium installs and launches
// the REAL app on an Android emulator / iOS simulator, then asserts two things. (1) In the WebView context:
// the full asset pipeline rendered — the boot shell + client + scoped CSS/JS + Bootstrap all served through
// Rask.Native's NativeOriginAssets. (2) In the NATIVE context: NativeShowcaseApp's NativeHeaderBar/NativeTabBar
// projected to REAL platform bars (iOS UINavigationBar/UITabBar, Android top/bottom bars), and tapping a
// native tab drives the WebView's route over the bridge. This is the device-level replacement for the old
// headless shim.
public sealed class NativeShowcaseAppiumTests
{
    // Appium's synthetic context for the app's native view tree (vs. the WEBVIEW_*/CHROMIUM contexts).
    private const string NativeContext = "NATIVE_APP";

    [SkippableFact]
    public void Android_showcase_renders_and_serves_scoped_assets()
    {
        Skip.IfNot(AppiumEnv.Enabled && AppiumEnv.AndroidApp is not null, AppiumEnv.SkipReason);

        var options = new AppiumOptions
        {
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            App = AppiumEnv.AndroidApp
        };
        options.AddAdditionalAppiumOption("appium:autoGrantPermissions", true);
        options.AddAdditionalAppiumOption("appium:newCommandTimeout", 180);
        // .NET Android names the launcher activity with a generated crc64 prefix; accept any activity so
        // the wait doesn't depend on that name.
        options.AddAdditionalAppiumOption("appium:appWaitActivity", "*");
        // Fetch a Chromedriver matching the device WebView's Chrome (the server must allow the insecure
        // feature: appium --allow-insecure=uiautomator2:chromedriver_autodownload).
        options.AddAdditionalAppiumOption("appium:chromedriverAutodownload", true);

        using var driver = new AndroidDriver(new Uri(AppiumEnv.ServerUrl!), options, TimeSpan.FromMinutes(3));
        var webContext = AssertShowcaseRendered(driver);
        AssertNativeChromeAndNavigation(driver, webContext);
    }

    [SkippableFact]
    public void Ios_showcase_renders_and_serves_scoped_assets()
    {
        Skip.IfNot(AppiumEnv.Enabled && AppiumEnv.IosApp is not null, AppiumEnv.SkipReason);

        var options = new AppiumOptions
        {
            PlatformName = "iOS",
            AutomationName = "XCUITest",
            App = AppiumEnv.IosApp,
            // deviceName is a reserved capability on AppiumOptions — setting it via
            // AddAdditionalAppiumOption("appium:deviceName", ...) throws; assign the property instead.
            DeviceName = AppiumEnv.IosDeviceName
        };
        options.AddAdditionalAppiumOption("appium:newCommandTimeout", 180);
        // First session on a fresh runner builds WebDriverAgent from source (xcodebuild build-for-testing),
        // which takes minutes — give the server a matching launch window so it doesn't abort mid-build.
        options.AddAdditionalAppiumOption("appium:wdaLaunchTimeout", 360_000);
        options.AddAdditionalAppiumOption("appium:wdaConnectionTimeout", 360_000);
        // Explicit UDID targeting when the CI job resolved it: name-only selection is flaky on the ARM64
        // macOS runners (same reason MAUI's XHarness pins --device UDID). Locally, unset → attach by name.
        if (AppiumEnv.IosUdid is not null)
        {
            options.AddAdditionalAppiumOption("appium:udid", AppiumEnv.IosUdid);
        }

        // The client's HTTP command timeout must outlast that cold WDA build too, or POST /session times
        // out (180s was too short) before the session is even created.
        using var driver = new IOSDriver(new Uri(AppiumEnv.ServerUrl!), options, TimeSpan.FromMinutes(8));
        var webContext = AssertShowcaseRendered(driver);
        AssertNativeChromeAndNavigation(driver, webContext);
    }

    // Switch into the app's WebView and assert the showcase rendered with its assets — the same evidence
    // the headless suite used to check, now against a real on-device WebView. Returns the WebView context
    // name so the native-chrome flow can hop back into it to read the route.
    private static string AssertShowcaseRendered(AppiumDriver driver)
    {
        var webContext = WaitForWebViewContext(driver);
        driver.Context = webContext;

        // The native boot is asynchronous (WebView loads the shell → the client posts `ready` → the host
        // renders → the first frame morphs in), so poll until the showcase has actually rendered.
        var bodyText = string.Empty;
        var title = string.Empty;
        var sheets = 0;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            bodyText = ExecuteScript(driver, "return document.body ? document.body.innerText : ''");
            title = ExecuteScript(driver, "return document.title || ''");
            int.TryParse(ExecuteScript(driver, "return String(document.styleSheets.length)"), out sheets);

            if (bodyText.Contains("Rask", StringComparison.OrdinalIgnoreCase) && sheets > 0)
            {
                break;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(500));
        }

        // On failure, surface enough to tell "didn't render" from "wrong context / client didn't boot".
        var diag = $" [context={driver.Context}; " +
                   $"url={ExecuteScript(driver, "return document.location ? document.location.href : '?'")}; " +
                   $"__raskNative={ExecuteScript(driver, "return typeof window.__raskNative")}; " +
                   $"sheets={sheets}; titleLen={title.Length}; bodyLen={bodyText.Length}]";

        Assert.False(string.IsNullOrWhiteSpace(title), "The showcase should set a document title." + diag);
        Assert.False(string.IsNullOrWhiteSpace(bodyText), "The showcase WebView body should render content." + diag);
        Assert.Contains("Rask", bodyText, StringComparison.OrdinalIgnoreCase);
        // Scoped CSS + Bootstrap were served through NativeOriginAssets → stylesheets are present.
        Assert.True(sheets > 0, "Scoped/Bootstrap stylesheets should have loaded via NativeOriginAssets." + diag);
        return webContext;
    }

    // Assert NativeShowcaseApp's native chrome projected to REAL platform bars, then prove a native tab tap
    // navigates the WebView. The bars live in the NATIVE context (not the WebView); the route it drives is
    // read back in the WebView context (native history keeps document.location in sync — Rask.Native's
    // applyHistory). Runs on both platforms — the a11y ids the projection sets resolve via AccessibilityId
    // (iOS accessibilityIdentifier / Android content-desc).
    private static void AssertNativeChromeAndNavigation(AppiumDriver driver, string webContext)
    {
        driver.Context = NativeContext;

        // The native header + the two native tabs render (NativeHeaderBar + NativeTabBar → real bars).
        // Guides is the site root ("/") now that the Welcome landing page is gone, so it doubles as Home.
        WaitForNativeElement(driver, "rask-native-header");
        WaitForNativeElement(driver, "Guides");
        WaitForNativeElement(driver, "Todos");

        // A native tab tap raises a `navigate` event over the bridge → NativeLiveSession → router → re-render;
        // native history moves the URL, so the WebView's pathname follows. Verify the full round trip and
        // that re-selecting tabs works (Guides "/" → Todos "/todos" → back to Guides "/").
        TapNativeTabAndAssertRoute(driver, webContext, "Todos", "/todos");
        TapNativeTabAndAssertRoute(driver, webContext, "Guides", "/");
    }

    private static void TapNativeTabAndAssertRoute(
        AppiumDriver driver, string webContext, string tab, string expectedPath)
    {
        driver.Context = NativeContext;
        WaitForNativeElement(driver, tab).Click();

        driver.Context = webContext;
        var pathname = string.Empty;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            pathname = ExecuteScript(driver, "return document.location ? document.location.pathname : ''");
            if (string.Equals(pathname, expectedPath, StringComparison.Ordinal))
            {
                return;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(500));
        }

        // Assert (not just fail) so the message shows the route the tap actually produced.
        Assert.Equal(expectedPath, pathname);
    }

    // Poll for a native element, addressing it by the a11y id the projection sets (AccessibilityId maps to
    // accessibilityIdentifier on iOS / content-desc on Android), and fall back to its visible name/text so a
    // platform that doesn't surface the identifier on a bar item still resolves it.
    private static AppiumElement WaitForNativeElement(AppiumDriver driver, string idOrText)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (TryFindNativeElement(driver, idOrText) is { } element)
            {
                return element;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(500));
        }

        throw new InvalidOperationException(
            $"The native element '{idOrText}' never appeared in the NATIVE context. The native chrome " +
            "(NativeHeaderBar/NativeTabBar) should project to real platform bars via INativeChrome.");
    }

    private static AppiumElement? TryFindNativeElement(AppiumDriver driver, string idOrText)
    {
        var byId = driver.FindElements(MobileBy.AccessibilityId(idOrText));
        if (byId.Count > 0)
        {
            return byId.First();
        }

        var byText = driver is IOSDriver
            ? driver.FindElements(MobileBy.IosNSPredicate($"name == '{idOrText}' OR label == '{idOrText}'"))
            : driver.FindElements(MobileBy.AndroidUIAutomator($"new UiSelector().text(\"{idOrText}\")"));
        return byText.Count > 0 ? byText.First() : null;
    }

    private static string WaitForWebViewContext(AppiumDriver driver)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            foreach (var context in driver.Contexts)
            {
                if (context.StartsWith("WEBVIEW", StringComparison.Ordinal) ||
                    context.StartsWith("CHROMIUM", StringComparison.Ordinal))
                {
                    return context;
                }
            }

            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        throw new InvalidOperationException(
            "No WEBVIEW context appeared. The app's WebView must be inspectable/debuggable for Appium " +
            "(the sample heads enable it — see RaskAndroidWebView / RaskWkWebView).");
    }

    private static string ExecuteScript(AppiumDriver driver, string script) =>
        ((IJavaScriptExecutor)driver).ExecuteScript(script)?.ToString() ?? string.Empty;
}
