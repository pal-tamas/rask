using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="IWebRtc" /> — a peer-to-peer data channel between two browsers. This demo puts
///     <em>both</em> peers in one page, so the signaling step is a plain method call rather than a network
///     hop; in a real app that is exactly where your WebSocket, HTTP endpoint or
///     <see cref="IBroadcastChannel" /> goes. Everything else is the real thing: a real offer/answer
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

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _supported = await rtc.IsSupportedAsync();
        if (!_supported)
        {
            StateHasChanged();
        }
    }

    protected override Component? Render() =>
        Div.Class($"{Ui.Card} shadow-sm border-0")[
            Div.Class(Ui.CardBody)[
                !_supported
                    ? Div.Class("small text-secondary fst-italic").Id("rtc-state")[
                        "This browser has no WebRTC support."]
                    : Div[
                        Div.Class("d-flex gap-2 mb-2")[
                            Button.Type("button").Class(Ui.BtnPrimary)
                                .Id("rtc-connect")
                                .Disabled(_connecting)
                                .OnClickAsync(ConnectAsync)["Connect the two peers"],
                            Button.Type("button").Class(Ui.BtnSecondary)
                                .Id("rtc-send")
                                .Disabled(!_everConnected)
                                .OnClickAsync(SendAsync)["Send a message"]
                        ],
                        Div.Class("small text-secondary mb-1")[
                            "Connection state: ", Span.Id("rtc-state")[_state]],
                        Div.Class("small text-secondary mb-1")[
                            "Local ICE candidates gathered: ",
                            Span.Id("rtc-candidates")[_localCandidates.ToString()]],
                        Div.Class("small text-secondary mb-1")["Received by the other peer:"],
                        _log.Count == 0
                            ? Div.Class("small text-secondary fst-italic").Id("rtc-log")["(nothing yet)"]
                            : Ul.Class("small mb-0").Id("rtc-log")[
                                _log.Select(m => Li.Key(m)[m])
                            ]
                    ]
            ]
        ];

    private async Task ConnectAsync()
    {
        if (_connecting)
        {
            return;
        }

        _connecting = true;
        _state = "connecting";
        StateHasChanged();

        // The caller. Its local candidates belong to the callee — in a real app, this is a signaling send.
        _caller = await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers
        {
            OnIceCandidates = candidates => DeliverAsync(candidates, toCaller: false),
            OnConnectionStateChanged = state =>
            {
                _state = state.ToString().ToLowerInvariant();
                _everConnected |= state == RtcConnectionState.Connected;
                StateHasChanged();
                return Task.CompletedTask;
            }
        });

        // The callee. It learns about the channel through OnDataChannel, the way a remote peer always does.
        _callee = await rtc.CreateAsync(new RtcConfiguration(), new RtcHandlers
        {
            OnIceCandidates = candidates => DeliverAsync(candidates, toCaller: true),
            OnDataChannel = channel => channel.ListenAsync(ReceiveAsync).AsTask()
        });

        _chat = await _caller.CreateDataChannelAsync("chat");
        await _chat.ListenAsync(ReceiveAsync);

        var offer = await _caller.CreateOfferAsync();
        await _caller.SetLocalDescriptionAsync(offer);
        await _callee.SetRemoteDescriptionAsync(offer);
        _calleeReady = true;

        var answer = await _callee.CreateAnswerAsync();
        await _callee.SetLocalDescriptionAsync(answer);
        await _caller.SetRemoteDescriptionAsync(answer);
        _callerReady = true;

        await FlushAsync();
        StateHasChanged();
    }

    // Hands a batch of candidates to the other peer, holding them back until that peer has a remote
    // description. addIceCandidate throws before then, and gathering can easily outrun the answer.
    private async Task DeliverAsync(IReadOnlyList<RtcIceCandidate> candidates, bool toCaller)
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
            await target.AddIceCandidateAsync(candidate);
        }
    }

    private async Task FlushAsync()
    {
        await DrainAsync(_pendingForCaller, _caller, _callerReady);
        await DrainAsync(_pendingForCallee, _callee, _calleeReady);
        return;

        static async Task DrainAsync(List<RtcIceCandidate> pending, IPeerConnection? target, bool ready)
        {
            if (target is null || !ready)
            {
                return;
            }

            var buffered = pending.ToArray();
            pending.Clear();
            foreach (var candidate in buffered)
            {
                await target.AddIceCandidateAsync(candidate);
            }
        }
    }

    private async Task SendAsync()
    {
        if (_chat is null)
        {
            return;
        }

        await _chat.SendAsync($"Message #{++_sent}");
    }

    // The browser pushes here, so state changes need StateHasChanged() — a subscription, not a binding.
    private Task ReceiveAsync(IReadOnlyList<RtcMessage> messages)
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
