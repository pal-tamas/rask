using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Http;

namespace Rask.Core.Tests.Readers;

// The reader's own clock and address: the zone their browser names (else the app's), now and today on their wall,
// and the address — or for IPv6 the /64 — a throttle counts them by.
public sealed class ReaderTests
{
    private static readonly DateTimeOffset LateEveningUtc = new(2026, 11, 19, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_reader_in_tokyo_is_already_on_tomorrow_when_utc_is_late_in_the_evening()
    {
        var reader = new Reader(new FixedTime(LateEveningUtc));

        reader.Saw("Asia/Tokyo", address: null);

        Assert.True(reader.KnowsTimeZone);
        Assert.Equal(new DateOnly(2026, 11, 20), reader.Today);
        Assert.Equal(TimeSpan.FromHours(9), reader.Now.Offset);
        Assert.Equal(LateEveningUtc, reader.Now);
    }

    [Fact]
    public void Until_the_browser_names_a_zone_the_reader_is_on_the_apps_clock()
    {
        var reader = new Reader(new FixedTime(LateEveningUtc));

        var zone = reader.TimeZone;

        Assert.False(reader.KnowsTimeZone);
        Assert.Equal(Clock.Zone, zone);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("Europe/../../etc")]
    [InlineData("Nowhere/Land")]
    [InlineData("/Europe/Budapest")]
    [InlineData("Europe//Budapest")]
    [InlineData("")]
    public void A_zone_id_that_is_malformed_or_unknown_is_no_zone(string id)
    {
        var zone = Reader.ZoneOf(id);

        Assert.Null(zone);
    }

    [Fact]
    public void A_zone_the_browser_named_is_kept_when_a_later_request_names_none_or_nonsense()
    {
        var reader = new Reader(new FixedTime(LateEveningUtc));

        reader.Saw("Europe/Budapest", IPAddress.Parse("192.0.2.7"));
        reader.Saw("Not a zone", address: null);

        Assert.Equal(Reader.ZoneOf("Europe/Budapest"), reader.TimeZone);
        Assert.Equal(IPAddress.Parse("192.0.2.7"), reader.Address);
    }

    [Theory]
    [InlineData("192.0.2.7", "192.0.2.7")]
    [InlineData("::ffff:192.0.2.7", "192.0.2.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::")]
    public void A_reader_is_counted_by_their_ipv4_address_or_their_ipv6_slash_64(string address, string network)
    {
        var reader = new Reader();

        reader.Saw(zoneId: null, IPAddress.Parse(address));

        Assert.Equal(network, reader.Network);
        Assert.Equal(network, Reader.NetworkOf(IPAddress.Parse(address)));
    }

    [Fact]
    public void A_reader_with_no_address_is_counted_as_unknown()
    {
        var reader = new Reader();

        var network = reader.Network;

        Assert.Null(reader.Address);
        Assert.Equal("unknown", network);
    }

    [Fact]
    public void A_request_tells_the_reader_its_cookie_zone_and_its_address()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Cookie = "rask-tz=America%2FNew_York";
        http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        var services = new ServiceCollection()
            .AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = http });
        ReaderSeen.Register(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<Reader>();

        Assert.Equal(Reader.ZoneOf("America/New_York"), reader.TimeZone);
        Assert.Equal(IPAddress.Parse("203.0.113.9"), reader.Address);
    }

    [Fact]
    public void The_runtime_writes_the_cookie_the_server_reads_before_it_connects()
    {
        var zone = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Core", "Resources", "rask-zone.ts"));
        var runtime = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Rask.Server", "Resources", "rask.ts"));

        Assert.Contains($"const ZONE_COOKIE = \"{Reader.ZoneCookie}\";", zone, StringComparison.Ordinal);
        Assert.Contains("Intl.DateTimeFormat().resolvedOptions().timeZone", zone, StringComparison.Ordinal);
        Assert.Contains("import \"../../Rask.Core/Resources/rask-zone.js\";", runtime, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Rask.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
