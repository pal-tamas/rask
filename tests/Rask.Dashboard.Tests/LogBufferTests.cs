using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rask.Dashboard.Logging;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The log tail is bounded in-memory state fed by the standard logging pipeline. What matters is that it
/// stays bounded, that it filters before formatting, and that it really is wired into <c>ILogger</c> —
/// a buffer nobody writes to would look identical to a quiet application.
/// </summary>
public sealed class LogBufferTests
{
    [Fact]
    public void The_buffer_keeps_only_the_newest_entries()
    {
        var buffer = Buffer(size: 3);

        for (var i = 1; i <= 10; i++)
        {
            buffer.Add(LogLevel.Information, "Cat", $"message {i}", exception: null);
        }

        // Bounded by count, so a chatty application can't grow it without limit.
        Assert.Equal(
            ["message 10", "message 9", "message 8"],
            buffer.Snapshot().Select(e => e.Message));
    }

    [Fact]
    public void Entries_below_the_minimum_level_are_never_captured()
    {
        var buffer = Buffer(minimum: LogLevel.Warning);

        Assert.False(buffer.IsEnabled(LogLevel.Information));
        Assert.True(buffer.IsEnabled(LogLevel.Error));

        buffer.Add(LogLevel.Warning, "Cat", "kept", exception: null);
        Assert.Single(buffer.Snapshot());
    }

    [Fact]
    public void Capture_can_be_turned_off_entirely()
    {
        var buffer = Buffer(capture: false);

        // IsEnabled is what the logging infrastructure checks before formatting a message, so switching
        // capture off has to cost nothing rather than merely discarding afterwards.
        Assert.False(buffer.IsEnabled(LogLevel.Critical));
    }

    [Fact]
    public void Snapshot_filters_by_level_and_category()
    {
        var buffer = Buffer();
        buffer.Add(LogLevel.Information, "Rask.Live", "info", null);
        buffer.Add(LogLevel.Error, "Rask.Diff", "boom", null);
        buffer.Add(LogLevel.Error, "App.Orders", "order failed", null);

        Assert.Equal(2, buffer.Snapshot(minimumLevel: LogLevel.Error).Count);
        Assert.Equal("boom", Assert.Single(buffer.Snapshot(category: "Rask.Diff")).Message);
        Assert.Equal(["App.Orders", "Rask.Diff", "Rask.Live"], buffer.Categories());
    }

    [Fact]
    public void An_exception_is_captured_alongside_the_message()
    {
        var buffer = Buffer();
        buffer.Add(LogLevel.Error, "Rask.Jobs", "Job 7 failed", new InvalidOperationException("boom"));

        var entry = Assert.Single(buffer.Snapshot());
        Assert.Contains("boom", entry.Exception!, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_raises_Changed_so_the_panel_can_push_instead_of_poll()
    {
        var buffer = Buffer();
        var raised = 0;
        buffer.Changed += () => raised++;

        buffer.Add(LogLevel.Information, "Cat", "one", null);
        buffer.Clear();

        Assert.Equal(2, raised);   // once for the entry, once for the clear
    }

    [Fact]
    public async Task Logging_through_ILogger_reaches_the_buffer()
    {
        // The wiring test: registration has to actually attach a provider to the logging pipeline, or the
        // panel silently shows nothing on a perfectly busy application.
        await using var h = new DashboardHarness(Batteries.Jobs);

        h.Get<ILoggerFactory>().CreateLogger("App.Thing").LogWarning("through the pipeline");

        var entry = Assert.Single(h.Get<DashboardLogBuffer>().Snapshot(category: "App.Thing"));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("through the pipeline", entry.Message);
    }

    [Fact]
    public async Task Capture_off_registers_no_provider()
    {
        await using var h = new DashboardHarness(Batteries.Jobs, configure: o => o.CaptureLogs = false);

        h.Get<ILoggerFactory>().CreateLogger("App.Thing").LogError("ignored");

        Assert.Empty(h.Get<DashboardLogBuffer>().Snapshot());
    }

    private static DashboardLogBuffer Buffer(
        int size = 100, LogLevel minimum = LogLevel.Information, bool capture = true) =>
        new(
            new RaskDashboardOptions { LogBufferSize = size, LogMinimumLevel = minimum, CaptureLogs = capture },
            TimeProvider.System);
}
