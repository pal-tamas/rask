using Microsoft.Extensions.DependencyInjection;
using Rask.Dashboard.Pages;
using Rask.Dashboard.Panels;
using Rask.Storage;
using Rask.Testing;

namespace Rask.Dashboard.Tests;

/// <summary>
/// The Storage tab reads the <c>StoredFile</c> table directly, the same way the cache page reads its entries — no
/// store is enumerated, so a bucket with a million objects costs one indexed query, not a listing.
/// </summary>
public sealed class StoragePageTests
{
    [Fact]
    public async Task Stats_count_files_bytes_and_public_ones_per_provider()
    {
        await using var h = new DashboardHarness(Batteries.Storage);
        var files = h.Get<IFiles>();
        await files.SaveAsync(new MemoryStream(new byte[100]), "a.bin");
        await files.SaveAsync(new MemoryStream(new byte[50]), "b.bin", o => o.Public = true);

        var stats = await h.Get<IStoragePanelReader>().StatsAsync(CancellationToken.None);

        Assert.Equal(2, stats.Files);
        Assert.Equal(150, stats.Bytes);
        Assert.Equal(1, stats.Public);
        var usage = Assert.Single(stats.ByProvider);
        Assert.Equal(StorageProvider.Disk, usage.Provider);
        Assert.Equal(StorageProvider.Disk, stats.ActiveProvider);
    }

    [Fact]
    public async Task An_empty_store_reports_zero_rather_than_failing()
    {
        // SUM over no rows is NULL in SQL; the per-provider grouping has to come back empty, not throw.
        await using var h = new DashboardHarness(Batteries.Storage);

        var stats = await h.Get<IStoragePanelReader>().StatsAsync(CancellationToken.None);

        Assert.Equal(0, stats.Files);
        Assert.Equal(0, stats.Bytes);
        Assert.Empty(stats.ByProvider);
    }

    [Fact]
    public async Task Files_list_newest_first_and_filter_by_name()
    {
        await using var h = new DashboardHarness(Batteries.Storage);
        var files = h.Get<IFiles>();
        await files.SaveAsync(new MemoryStream("one"u8.ToArray()), "invoice-1.txt");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await files.SaveAsync(new MemoryStream("two"u8.ToArray()), "photo.txt");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        await files.SaveAsync(new MemoryStream("three"u8.ToArray()), "invoice-2.txt");

        var reader = h.Get<IStoragePanelReader>();
        var (all, total) = await reader.PageAsync(null, 0, 10, CancellationToken.None);
        var (matching, matched) = await reader.PageAsync("invoice", 0, 10, CancellationToken.None);

        Assert.Equal(3, total);
        Assert.Equal(["invoice-2.txt", "photo.txt", "invoice-1.txt"], all.Select(r => r.Name));
        Assert.Equal(2, matched);
        Assert.All(matching, r => Assert.StartsWith("invoice", r.Name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unregistered_store_is_not_available()
    {
        await using var h = new DashboardHarness(Batteries.Cache);

        Assert.False(h.Get<IStoragePanelReader>().IsAvailable);
    }

    [Fact]
    public async Task An_unmapped_store_is_not_available()
    {
        await using var h = new DashboardHarness(registered: Batteries.All, mapped: Batteries.Jobs);

        var storage = h.Get<IStoragePanelReader>();

        Assert.False(storage.IsAvailable);
        Assert.Equal(0, (await storage.PageAsync(null, 0, 10, CancellationToken.None)).Total);
    }

    [Fact]
    public async Task The_page_lists_uploaded_names_as_text_and_says_disk_files_are_not_backed_up()
    {
        // A file name is text a stranger typed. The console shows it, so it has to arrive as text.
        await using var h = new DashboardHarness(Batteries.Storage);
        await h.Get<IFiles>().SaveAsync(new MemoryStream("x"u8.ToArray()), "report <img src=x onerror=alert(1)>.txt");

        var page = RaskTest.Render(ActivatorUtilities.CreateInstance<StoragePage>(h.Services), h.Services);
        var html = await page.WaitForAsync("report");

        Assert.Contains("report &lt;img", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.Contains("not covered by rask db backup", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_battery_the_page_says_how_to_add_it()
    {
        await using var h = new DashboardHarness(Batteries.Cache);

        var page = RaskTest.Render(ActivatorUtilities.CreateInstance<StoragePage>(h.Services), h.Services);
        // Waited on the call rather than the heading, whose apostrophe the page encodes.
        var html = await page.WaitForAsync("AddRaskStorage");

        Assert.Contains("modelBuilder.AddRaskStorage()", html, StringComparison.Ordinal);
    }
}
