using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

public sealed class LiveRuntimeRobustnessTests
{
    // The first "<body" anywhere was taken for the page's, so a head script holding the text got the session id.
    [Theory]
    [InlineData("<html><head><script>const t = \"<body class=x>\";</script></head><body><p>x</p></body></html>")]
    [InlineData("<html><head><!-- <body> --></head><body><p>x</p></body></html>")]
    [InlineData("<html><head><title>a <body> tag</title><template><body></template></head><body><p>x</p></body></html>")]
    [InlineData("<html><head><STYLE>/* <body> */</STYLE></head><BODY><p>x</p></BODY></html>")]
    public void The_session_id_goes_on_the_body_the_browser_opens(string html)
    {
        var injected = LivePayload.InjectRootAttr(html, "abc");
        var utf8 = Encoding.UTF8.GetBytes(html);

        var at = injected.IndexOf("data-rask-root", StringComparison.Ordinal);
        Assert.True(at > 0);
        Assert.StartsWith("<body data-rask-root", injected[(at - 6)..], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(html.IndexOf("<body><p>", StringComparison.OrdinalIgnoreCase), LivePayload.IndexOfBodyOpenUtf8(utf8));
    }

    [Fact]
    public void A_head_script_that_never_closes_has_no_body_after_it()
    {
        var html = "<html><head><script>const t = \"<body>\";";

        var injected = LivePayload.InjectRootAttr(html, "abc");

        Assert.Equal(html, injected);
    }

    // A frame is the client's to write: `[]` or `"x"` for a payload used to throw out of the event's handler.
    [Theory]
    [InlineData("[]")]
    [InlineData("\"x\"")]
    [InlineData("7")]
    public void Every_payload_reader_answers_a_payload_that_is_no_object_with_its_default(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var p = doc.RootElement;

        Assert.Equal(0, EventPayload.ReadLong(p, "n"));
        Assert.Empty(EventPayload.ReadList(p, "touches", static e => e));
        Assert.Empty(EventPayload.ReadStrings(p, "files"));
        Assert.Null(EventPayload.ReadMap(p, "data"));
    }

    // A keyed scoped service was taken from the session, so one keyed DbContext served every event it ever handled.
    [Fact]
    public void A_keyed_scoped_service_is_made_per_event_and_a_keyed_singleton_is_not()
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<Probe>("orders");
        services.AddKeyedTransient<Other>(KeyedService.AnyKey);
        services.AddKeyedSingleton<Shared>("orders");
        var catalog = new EventScopeCatalog(services);

        Assert.True(catalog.OwnsPerEvent(typeof(Probe), "orders"));
        Assert.True(catalog.OwnsPerEvent(typeof(IEnumerable<Probe>), "orders"));
        Assert.True(catalog.OwnsPerEvent(typeof(Other), "anything"));
        Assert.False(catalog.OwnsPerEvent(typeof(Probe), "other"));
        Assert.False(catalog.OwnsPerEvent(typeof(Shared), "orders"));
        Assert.False(catalog.OwnsPerEvent(typeof(Probe)));
    }

    private sealed class Probe;

    private sealed class Other;

    private sealed class Shared;
}
