using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Core.Diagnostics;
using Rask.Wasm.Diagnostics;

namespace Rask.Wasm.Tests.Hosting;

// #1096: on a WASM host the framework's diagnostics are forwarded into ILogger, and a logger factory with no
// provider writes nowhere. These pin that a default host has somewhere to write, that an app's own providers are
// left alone, and that the bridge maps levels the way the server's does.
//
// Drives Emit and the built container directly rather than installing RaskDiagnostics.Sink, which is
// process-global and would race every other test that reports through it.
public class RaskWasmDiagnosticsTests
{
    // Mirrors RaskServerDiagnosticsBridgeTests' table: the two bridges are separate copies, and this pair of
    // tests is what keeps their level mapping from drifting apart.
    [Theory]
    [InlineData((int)RaskLogLevel.Error, LogLevel.Error)]
    [InlineData((int)RaskLogLevel.Warning, LogLevel.Warning)]
    [InlineData((int)RaskLogLevel.Information, LogLevel.Information)]
    public void An_emitted_diagnostic_writes_its_mapped_level_category_and_message_to_the_console(int raskLevel, LogLevel expected)
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        using var factory = LoggerFactory.Create(b => b.AddProvider(new BrowserConsoleLoggerProvider(output, error)));

        RaskWasmDiagnostics.Emit(
            factory, new RaskDiagnosticEvent((RaskLogLevel)raskLevel, "Rask.Test.Bridge", "bridged fault", null));

        var written = output.ToString() + error.ToString();

        Assert.Contains($"[Rask.Test.Bridge] {expected}: bridged fault", written);
    }

    [Fact]
    public void An_error_goes_to_standard_error_and_a_warning_to_standard_output()
    {
        var (output, error) = (new StringWriter(), new StringWriter());
        using var factory = LoggerFactory.Create(b => b.AddProvider(new BrowserConsoleLoggerProvider(output, error)));
        var boom = new InvalidOperationException("boom");

        RaskWasmDiagnostics.Emit(factory, new RaskDiagnosticEvent(RaskLogLevel.Warning, "Rask.Test", "careful", null));
        RaskWasmDiagnostics.Emit(factory, new RaskDiagnosticEvent(RaskLogLevel.Error, "Rask.Test", "failed", boom));

        Assert.Contains("careful", output.ToString());
        Assert.DoesNotContain("failed", output.ToString());
        Assert.Contains("failed", error.ToString());
        Assert.Contains("InvalidOperationException: boom", error.ToString());
    }

    // The seam the issue was about: the container a default host boots with. Built through the same method
    // BootAsync and the prerender pass use, so removing the registration from it fails here.
    [Fact]
    public void A_default_host_writes_framework_diagnostics_to_the_browser_console()
    {
        using var provider = WasmHostBuilder.CreateDefault().BuildServices();

        Assert.Contains(provider.GetServices<ILoggerProvider>(), p => p is BrowserConsoleLoggerProvider);
        Assert.True(provider.GetRequiredService<ILoggerFactory>().CreateLogger("Rask.Test").IsEnabled(LogLevel.Warning));
    }

    [Fact]
    public void An_app_that_registered_a_provider_keeps_only_its_own()
    {
        var builder = WasmHostBuilder.CreateDefault();
        var own = new BrowserConsoleLoggerProvider(TextWriter.Null, TextWriter.Null);
        builder.Services.AddSingleton<ILoggerProvider>(own);

        using var provider = builder.BuildServices();

        Assert.Same(own, Assert.Single(provider.GetServices<ILoggerProvider>()));
    }

    [Fact]
    public void Building_twice_registers_the_console_provider_once()
    {
        var builder = WasmHostBuilder.CreateDefault();

        using (builder.BuildServices())
        {
        }

        using var provider = builder.BuildServices();

        Assert.Single(provider.GetServices<ILoggerProvider>());
    }
}
