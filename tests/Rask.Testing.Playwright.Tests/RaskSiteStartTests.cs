namespace Rask.Testing.Playwright.Tests;

public sealed class RaskSiteStartTests
{
    [Fact]
    public async Task A_site_runs_its_app_on_a_port_of_its_own_and_ends_it_when_disposed()
    {
        using var http = new HttpClient();
        var site = new RaskSite<Program>();

        await site.Start();
        var address = site.Address;
        using var answered = await http.GetAsync(address, TestContext.Current.CancellationToken);
        var said = await answered.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        await site.DisposeAsync();

        Assert.Contains("Hello from Program.cs", said, StringComparison.Ordinal);
        Assert.Equal("localhost", address.Host);
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(address, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(site.Folder));
    }

    [Fact]
    public async Task A_site_runs_the_apps_own_build_from_the_apps_own_folder()
    {
        await using var site = new RaskSite<Program>();

        var host = site.Host;
        var folder = site.ContentRoot;

        Assert.Equal("Rask.Testing.Fixture.App.dll", Path.GetFileName(host));
        Assert.True(File.Exists(Path.Combine(folder, "Rask.Testing.Fixture.App.csproj")), folder);
        Assert.StartsWith(folder, host, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_site_writes_what_the_app_says_to_its_host_log_while_the_app_still_runs()
    {
        var log = Path.Combine(Path.GetTempPath(), $"rask-host-log-{Guid.NewGuid():N}", "host.log");
        await using var site = new RaskSite<Program> { HostLog = log };

        await site.Start();
        var written = await File.ReadAllTextAsync(log, TestContext.Current.CancellationToken);
        var said = site.Log;

        Assert.NotEqual("", said);
        Assert.All(said.ReplaceLineEndings("\n").Split('\n'), line => Assert.Contains(line, written, StringComparison.Ordinal));
        Directory.Delete(Path.GetDirectoryName(log)!, recursive: true);
    }

    [Fact]
    public async Task A_site_that_keeps_stand_ins_of_its_own_disposes_them_with_it()
    {
        var site = new SiteWithAStandIn();

        await site.DisposeAsync();

        Assert.True(site.StandInDisposed);
        Assert.False(Directory.Exists(site.Folder));
    }

    [Fact]
    public async Task An_app_that_stops_as_it_starts_fails_the_start_with_what_it_said()
    {
        // A library is no app: dotnet says what it is missing, and stops.
        await using var site = new RaskSite(typeof(RaskSite).Assembly.Location);

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(site.Start);

        Assert.Contains("stopped as it started", failed.Message, StringComparison.Ordinal);
        Assert.Contains("runtimeconfig.json", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_was_never_built_fails_the_start_naming_where_its_build_should_be()
    {
        var missing = Path.Combine(Path.GetTempPath(), "rask-site-nowhere", "Nowhere.dll");
        await using var site = new RaskSite(missing);

        var failed = await Assert.ThrowsAsync<InvalidOperationException>(site.Start);

        Assert.Contains(missing, failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_app_that_never_answers_its_health_check_times_out_naming_the_path()
    {
        await using var site = new RaskSite<Program> { Health = "/never-healthy", StartWithin = TimeSpan.FromSeconds(3) };

        var failed = await Assert.ThrowsAsync<TimeoutException>(site.Start);

        Assert.Contains("/never-healthy", failed.Message, StringComparison.Ordinal);
        Assert.Contains("site.Health", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_site_asked_where_it_is_before_it_starts_says_how_to_start_it()
    {
        await using var site = new RaskSite<Program>();

        var failed = Assert.Throws<InvalidOperationException>(() => site.Address);

        Assert.Contains("await site.Start()", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_that_lost_its_port_is_told_apart_from_a_broken_one()
    {
        const string kestrel = "System.IO.IOException: Failed to bind to address http://[::1]:5123: address already in use.";

        var taken = LoopbackPort.LooksLikeAddressInUse(kestrel);
        var broken = LoopbackPort.LooksLikeAddressInUse("Unhandled exception. System.InvalidOperationException: no database");

        Assert.True(taken);
        Assert.False(broken);
    }

    private sealed class SiteWithAStandIn : RaskSite<Program>
    {
        public bool StandInDisposed { get; private set; }

        public override async ValueTask DisposeAsync()
        {
            StandInDisposed = true;
            await base.DisposeAsync();
        }
    }
}
