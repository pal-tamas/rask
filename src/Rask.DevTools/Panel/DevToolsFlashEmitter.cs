using System.Buffers;
using System.Text;
using System.Text.Json;
using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     Tells the page what to flash, for as long as flashing is on, whichever tab is showing.
/// </summary>
/// <remarks>
///     <para>
///         Renders nothing visible. It carries two hidden attributes the panel's script reads: whether flashing is on
///         (<c>data-rask-devtools-flash</c>), which the script hands to the page's devtools host to start or stop its
///         own DOM-change flash and to remember; and, while on, the places of the components that rendered in the newest
///         commits (<c>data-rask-devtools-flashes</c>), which the script posts to the page as boxes to flash.
///     </para>
///     <para>
///         The setting lives on the page, in its storage, so a reload keeps it; the panel cannot read the page's storage
///         from C#. The script reads it and reports it back as a keydown on the same element (<c>flash:on</c> or
///         <c>flash:off</c>), the one event both panels forward with a value.
///     </para>
///     <para>
///         The newest commits, not just the newest one: refreshes are coalesced, so several commits can land between two.
///         Each carries its sequence number, and the script flashes only the ones it has not flashed yet.
///     </para>
/// </remarks>
internal sealed partial class DevToolsFlashEmitter : Component
{
    /// <summary>The prefix of the keydown <c>key</c> the stored setting arrives as.</summary>
    internal const string SettingKeyPrefix = "flash:";

    /// <summary>How many of the newest commits the attribute carries.</summary>
    internal const int CommitsCarried = 8;

    private DevToolsFeed? _following;
    private DevToolsRefreshGate? _gate;
    private IDisposable? _places;

    /// <summary>The inspected session's feed.</summary>
    public required DevToolsFeed Feed { get; set; }

    /// <summary>Whether flashing is on.</summary>
    public required bool On { get; set; }

    /// <summary>Raised with the setting the page remembered, when the panel's script reports it.</summary>
    public Callback<bool>? OnChange { get; set; }

    /// <inheritdoc />
    protected override Task OnMount()
    {
        _gate = new DevToolsRefreshGate(StateHasChanged, CancellationToken);
        _following = Feed;
        _following.Changed += OnFeedChanged;
        SyncPlaces();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnUpdated()
    {
        SyncPlaces();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnUnmount()
    {
        if (_following is { } feed)
        {
            feed.Changed -= OnFeedChanged;
            _following = null;
        }

        _places?.Dispose();
        _places = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Span.Key("flash").Hidden(true)
            .Data(new Dictionary<string, string?> { ["rask-devtools-flash"] = On ? "on" : "off" })
            .OnKeyDown(e => Reported(e.Key)),
        On
            ? Span.Key("flashes").Hidden(true)
                .Data(new Dictionary<string, string?> { ["rask-devtools-flashes"] = Flashes(Feed.CommitsSnapshot()) })
            : null
    ];

    // Places are worked out at commit only while someone flashes them: the render log itself needs none.
    private void SyncPlaces()
    {
        if (On && _places is null)
        {
            _places = Feed.WatchPlaces();
        }
        else if (!On && _places is not null)
        {
            _places.Dispose();
            _places = null;
        }
    }

    private void OnFeedChanged()
    {
        if (On)
        {
            _gate?.Notify();
        }
    }

    private Task Reported(string? key)
    {
        if (key is null || !key.StartsWith(SettingKeyPrefix, StringComparison.Ordinal) || OnChange is not { } changed)
        {
            return Task.CompletedTask;
        }

        return key.AsSpan(SettingKeyPrefix.Length) switch
        {
            "on" => changed.Invoke(true),
            "off" => changed.Invoke(false),
            _ => null,
        } ?? Task.CompletedTask;
    }

    /// <summary>
    ///     <c>[[sequence, [[at, label], …]], …]</c>, oldest first, for the newest commits that rendered anything with a
    ///     place. The label is the component's type and why it rendered.
    /// </summary>
    internal static string Flashes(DevToolsCommit[] commits)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartArray();
            var from = Math.Max(0, commits.Length - CommitsCarried);
            for (var i = from; i < commits.Length; i++)
            {
                var commit = commits[i];
                json.WriteStartArray();
                json.WriteNumberValue(commit.Sequence);
                json.WriteStartArray();
                foreach (var render in commit.Renders)
                {
                    if (render.At is null)
                    {
                        continue;
                    }

                    json.WriteStartArray();
                    json.WriteStringValue(render.At);
                    json.WriteStringValue(render.Type + " · " + DevToolsNames.Label(render.Reason));
                    json.WriteEndArray();
                }

                json.WriteEndArray();
                json.WriteEndArray();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
