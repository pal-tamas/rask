using Microsoft.JSInterop;
using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="ISignaling" /> — the relay two peers trade an offer, an answer and their ICE candidates
///     over before <see cref="IWebRtc" /> can connect them. This demo opens <em>two</em> connections to the
///     same room from one page, so you can watch the whole exchange: the second one is told who was already
///     there, the first is told someone arrived, and a payload sent to one id comes out at that peer and
///     nowhere else.
///     <para>
///         The payload is an opaque string — here it's plain text; in a real app it's a serialized
///         <c>RtcDescription</c> or <c>RtcIceCandidate</c>. Neither the relay nor the wrapper looks inside.
///     </para>
/// </summary>
public sealed partial class SignalingDemo(ISignaling signaling) : Component, IAsyncDisposable
{
    private const string Room = "rask-signaling-demo";

    private readonly List<string> _log = [];
    private ISignalingConnection? _first;
    private ISignalingConnection? _second;
    private string? _firstId;
    private string? _secondId;
    private bool _joining;
    private bool _unavailable;
    private int _sent;

    protected override Component? Render() =>
        BsCard.Class(Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody[
                Div.Class("d-flex gap-2 mb-2")[
                    BsButton
                        .Color(BsColor.Primary)
                        .Size(BsSize.Sm)
                        .Id("signal-join")
                        .Disabled(_joining)
                        .OnClickAsync(JoinAsync)["Join the room twice"],
                    BsButton
                        .Color(BsColor.Secondary)
                        .Size(BsSize.Sm)
                        .Id("signal-send")
                        .Disabled(_secondId is null)
                        .OnClickAsync(SendAsync)["Relay a payload"]
                ],
                _unavailable
                    ? Div.Class("small text-secondary fst-italic").Id("signal-status")[
                        "This host isn't running the relay — it needs AddRaskSignaling() + "
                        + "MapRaskSignaling() on the server."]
                    : Div.Class("small text-secondary mb-1")[
                    "Peers: ",
                    Span.Id("signal-peers")[_firstId is null ? "none" : $"{Short(_firstId)} + {Short(_secondId)}"]],
                Div.Class("small text-secondary mb-1")["What the relay reported:"],
                _log.Count == 0
                    ? Div.Class("small text-secondary fst-italic").Id("signal-log")["(nothing yet)"]
                    : Ul.Class("small mb-0").Id("signal-log")[
                        _log.Select(m => Li.Key(m)[m])
                    ]
            ]
        ];

    private static string Short(string? id) => id is null ? "?" : id[..Math.Min(6, id.Length)];

    private async Task JoinAsync()
    {
        if (_joining)
        {
            return;
        }

        _joining = true;
        StateHasChanged();

        // A host that doesn't map the relay refuses the socket. Say so plainly rather than failing
        // silently — the showcase's WASM host serves static files and has no relay to offer.
        try
        {
            await ConnectAsync();
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            _unavailable = true;
            StateHasChanged();
        }
    }

    private async Task ConnectAsync()
    {
        _first = await signaling.JoinAsync(Room, new SignalingHandlers
        {
            OnJoined = (self, peers) =>
            {
                _firstId = self;
                Log($"first joined as {Short(self)}, saw {peers.Count} peer(s)");
                return Task.CompletedTask;
            },
            // The relay tells everyone already in the room that someone arrived.
            OnPeerJoined = id =>
            {
                Log($"first was told {Short(id)} arrived");
                return Task.CompletedTask;
            },
            OnSignal = (from, payload) =>
            {
                Log($"first received \"{payload}\" from {Short(from)}");
                return Task.CompletedTask;
            },
            OnError = message =>
            {
                Log($"relay refused: {message}");
                return Task.CompletedTask;
            }
        });

        _second = await signaling.JoinAsync(Room, new SignalingHandlers
        {
            // The peers already present are the ones this connection would offer to — the rule that stops
            // both sides offering at once.
            OnJoined = (self, peers) =>
            {
                _secondId = self;
                Log($"second joined as {Short(self)}, saw {peers.Count} peer(s)");
                return Task.CompletedTask;
            },
            OnError = message =>
            {
                Log($"relay refused: {message}");
                return Task.CompletedTask;
            }
        });
    }

    private async Task SendAsync()
    {
        if (_second is null || _firstId is null)
        {
            return;
        }

        await _second.SendAsync(_firstId, $"payload #{++_sent}");
    }

    // The relay pushes into these, so state changes need StateHasChanged() — a subscription, not a binding.
    private void Log(string line)
    {
        _log.Insert(0, line);
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_first is not null)
        {
            await _first.DisposeAsync();
        }

        if (_second is not null)
        {
            await _second.DisposeAsync();
        }
    }
}
