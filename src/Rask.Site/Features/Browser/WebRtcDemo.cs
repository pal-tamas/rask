using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary>
///     <see cref="IWebRtc" /> — a peer-to-peer data channel between two browsers. This demo puts
///     <em>both</em> peers in one page, so the signaling step is a plain method call rather than a network
///     hop; in a real app that is exactly where your WebSocket, HTTP endpoint or
///     <c>BroadcastChannel</c> goes. Everything else is the real thing: a real offer/answer
///     exchange, real ICE candidates, and a real <c>RTCDataChannel</c> carrying the messages.
///     <para>
///         Two details are worth copying. Candidates are <b>buffered until the remote description is
///         applied</b> — a candidate that arrives first is rejected by the browser, and this is the single
///         most common way a first WebRTC integration fails. And messages arrive as a <b>batch</b>: the
///         framework coalesces them, because on the Server host one push per message would be one
///         WebSocket frame per message.
///     </para>
/// </summary>
public sealed partial class WebRtcDemo(IWebRtc rtc) : Component, IAsyncDisposable
{
    private readonly List<string> _log = [];
    private readonly List<RtcIceCandidate> _pendingForCaller = [];
    private readonly List<RtcIceCandidate> _pendingForCallee = [];

    private IPeerConnection? _caller;
    private IPeerConnection? _callee;
    private IRtcDataChannel? _chat;

    private bool _callerReady;
    private bool _calleeReady;
    private bool _connecting;
    private bool _everConnected;
    private int _localCandidates;
    private int _sent;
    private string _state = "not connected";
    private bool _supported = true;

    protected override async Task OnFirstRender()
    {
        _supported = await rtc.IsSupported();
        if (!_supported)
        {
            StateHasChanged();
        }
    }

    protected override Component? Render() =>
        Ui.Card[
                !_supported
                    ? Div.Class("text-sm text-ui-muted italic").Id("rtc-state")[
                        "This browser has no WebRTC support."]
                    : Div[
                        Div.Class("flex gap-2 mb-2")[
                            Ui.Button.Primary
                                .Id("rtc-connect")
                                .Disabled(_connecting)
                                .OnClick(Connect)["Connect the two peers"],
                            Ui.Button.Filled
                                .Id("rtc-send")
                                .Disabled(!_everConnected)
                                .OnClick(Send)["Send a message"]
                        ],
                        Div.Class("text-sm text-ui-muted mb-1")[
                            "Connection state: ", Span.Id("rtc-state")[_state]],
                        Div.Class("text-sm text-ui-muted mb-1")[
                            "Local ICE candidates gathered: ",
                            Span.Id("rtc-candidates")[_localCandidates]],
                        Div.Class("text-sm text-ui-muted mb-1")["Received by the other peer:"],
                        MessageLog()
                    ]
            ];

    private Component MessageLog() =>
        _log.Count == 0
            ? Div.Class("text-sm text-ui-muted italic").Id("rtc-log")["(nothing yet)"]
            : Ul.Class("text-sm mb-0").Id("rtc-log")[_log.Select(m => Li.Key(m)[m])];

    private async Task Connect()
    {
        if (_connecting)
        {
            return;
        }

        _connecting = true;
        _state = "connecting";
        StateHasChanged();

        // The caller. Its local candidates belong to the callee — in a real app, this is a signaling send.
        _caller = await rtc.Create(new RtcConfiguration(), new RtcHandlers
        {
            OnIceCandidates = candidates => Deliver(candidates, toCaller: false),
            OnConnectionStateChanged = state =>
            {
                _state = state.ToString().ToLowerInvariant();
                _everConnected |= state == RtcConnectionState.Connected;
                StateHasChanged();
                return Task.CompletedTask;
            }
        });

        // The callee. It learns about the channel through OnDataChannel, the way a remote peer always does.
        _callee = await rtc.Create(new RtcConfiguration(), new RtcHandlers
        {
            OnIceCandidates = candidates => Deliver(candidates, toCaller: true),
            OnDataChannel = channel => channel.Listen(Receive).AsTask()
        });

        _chat = await _caller.CreateDataChannel("chat");
        await _chat.Listen(Receive);

        var offer = await _caller.CreateOffer();
        await _caller.SetLocalDescription(offer);
        await _callee.SetRemoteDescription(offer);
        _calleeReady = true;

        var answer = await _callee.CreateAnswer();
        await _callee.SetLocalDescription(answer);
        await _caller.SetRemoteDescription(answer);
        _callerReady = true;

        await Flush();
        StateHasChanged();
    }

    // Hands a batch of candidates to the other peer, holding them back until that peer has a remote
    // description. addIceCandidate throws before then, and gathering can easily outrun the answer.
    private async Task Deliver(IReadOnlyList<RtcIceCandidate> candidates, bool toCaller)
    {
        var target = toCaller ? _caller : _callee;
        var ready = toCaller ? _callerReady : _calleeReady;
        var pending = toCaller ? _pendingForCaller : _pendingForCallee;

        // Counted for the demo's own display: this is the batch the browser pushed into C#, so a non-zero
        // count is proof the whole gather → coalesce → [JSInvokable] → callback path ran.
        _localCandidates += candidates.Count;
        StateHasChanged();

        if (target is null || !ready)
        {
            pending.AddRange(candidates);
            return;
        }

        foreach (var candidate in candidates)
        {
            await target.AddIceCandidate(candidate);
        }
    }

    private async Task Flush()
    {
        await Drain(_pendingForCaller, _caller, _callerReady);
        await Drain(_pendingForCallee, _callee, _calleeReady);
        return;

        static async Task Drain(List<RtcIceCandidate> pending, IPeerConnection? target, bool ready)
        {
            if (target is null || !ready)
            {
                return;
            }

            var buffered = pending.ToArray();
            pending.Clear();
            foreach (var candidate in buffered)
            {
                await target.AddIceCandidate(candidate);
            }
        }
    }

    private async Task Send()
    {
        if (_chat is null)
        {
            return;
        }

        await _chat.Send($"Message #{++_sent}");
    }

    // The browser pushes here, so state changes need StateHasChanged() — a subscription, not a binding.
    private Task Receive(IReadOnlyList<RtcMessage> messages)
    {
        foreach (var message in messages)
        {
            _log.Insert(0, message.Text ?? $"{message.Data?.Length ?? 0} bytes");
        }

        StateHasChanged();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_caller is not null)
        {
            await _caller.DisposeAsync();
        }

        if (_callee is not null)
        {
            await _callee.DisposeAsync();
        }
    }
}
