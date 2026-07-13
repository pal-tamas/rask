using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.iOS;

namespace Rask.Native.Appium.Tests;

// On-device E2E for the Native + Local showcase (samples/Rask.Example.Native): Appium installs and launches
// the REAL app on an Android emulator / iOS simulator, switches into the WebView, and asserts the full
// asset pipeline rendered — the boot shell + client + scoped CSS/JS + Bootstrap all served through
// Rask.Native's NativeOriginAssets. This is the device-level replacement for the old headless shim.
public sealed class NativeShowcaseAppiumTests
{
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
        AssertShowcaseRendered(driver);
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
        // Explicit UDID targeting when the CI job resolved it: name-only selection is flaky on the ARM64
        // macOS runners (same reason MAUI's XHarness pins --device UDID). Locally, unset → attach by name.
        if (AppiumEnv.IosUdid is not null)
        {
            options.AddAdditionalAppiumOption("appium:udid", AppiumEnv.IosUdid);
        }

        using var driver = new IOSDriver(new Uri(AppiumEnv.ServerUrl!), options, TimeSpan.FromMinutes(3));
        AssertShowcaseRendered(driver);
    }

    // Switch into the app's WebView and assert the showcase rendered with its assets — the same evidence
    // the headless suite used to check, now against a real on-device WebView.
    private static void AssertShowcaseRendered(AppiumDriver driver)
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
