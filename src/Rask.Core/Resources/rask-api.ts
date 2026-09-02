// Shared framework Web-API / interop helpers, imported by both client runtimes (Server rask.ts and
// WASM rask.wasm.ts). Single source of truth so the two transports never drift. Each helper is
// assigned to a `window.__rask*` namespace so a dotted IJSRuntime identifier (e.g.
// "__raskApi.geolocation") resolves to it — that is why these are globals rather than exports: the
// caller is .NET, resolving the name against `window` at call time.
//
// The shapes are declared in rask-window.d.ts under a `__rask${string}` index signature rather than
// as thirty interfaces, because the authoritative contract for each is the C# wrapper that calls it.
// What IS checked here is every implementation: the arguments each helper takes and what it does
// with them.

// The extracted browser layer. Importing it registers window.__raskApi and window.__raskGeoWatch,
// which used to be defined in this file; the implementations now live in ./browser/ as ordinary
// modules a TypeScript front end can import directly. See ./browser/globals.ts.
import "./browser/globals.js";

// Element-ref helpers, invoked from C# via ElementRef.FocusAsync/Blur/ScrollIntoView.
// The JSON reviver resolves an ElementRef arg to the live DOM element, so each receives it.
window.__raskEl = window.__raskEl || {
    focus: (el: HTMLElement | null) => {
        if (el) el.focus();
    },
    blur: (el: HTMLElement | null) => {
        if (el) el.blur();
    },
    scrollIntoView: (el: Element | null, opts?: ScrollIntoViewOptions) => {
        if (el) el.scrollIntoView(opts || {behavior: "smooth", block: "nearest"});
    }
};

// Gesture-bridge DOM helpers — moved here from rask-wasm-api.js so they ship to the Server client too.
// They drive activation-gated browser APIs that must run inside a click gesture; the declarative
// FullscreenTrigger / EyeDropperTrigger components (and the data-rask-gesture click handler in
// rask-events.js) call these synchronously in the gesture, which is why they work even on the Server
// transport. The imperative IFullscreen / IEyeDropper services (WASM-only) call the same helpers.

// WebRTC signaling (driven by ISignaling) — the socket two peers trade an offer, an answer and their ICE
// candidates over before they can talk directly. The connection lives here rather than in C# for the same
// reason the peer connection does: it must work identically on both hosts, and on the Server host a C#-side
// socket would put the app's own server in the middle of a relay it is already hosting.
//
// A SEPARATE socket from the live render one, deliberately: that socket has its own frame contract, rate
// limits and shutdown-drain semantics, and signaling traffic has no business sharing them.
//
// The payload is an opaque string end to end — this helper never parses an SDP or a candidate either.
window.__raskSignal = window.__raskSignal || (() => {
    const conns = new Map<number, WebSocket>(); // id -> WebSocket
    return {
        isSupported: () => typeof window.WebSocket === "function",
        open: (id: number, path: string) => new Promise<boolean>((resolve, reject) => {
            const url = new URL(path, window.location.href);
            url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
            const ws = new WebSocket(url.href);
            conns.set(id, ws);
            ws.onmessage = (e) => {
                let m;
                try {
                    m = JSON.parse(e.data);
                } catch (_) {
                    return;
                }
                // One flat shape for every relay message, so the C# side has a single [JSInvokable]: the
                // peer it concerns (peerId on a join/leave, from on a signal), and one string slot that
                // carries the app payload, the error text, or — for our own join — the peer-list JSON.
                const peer = m.peerId || m.from || "";
                const text = m.type === "joined"
                    ? JSON.stringify(m.peers || [])
                    : (m.payload != null ? m.payload : (m.message || ""));
                invoke("RaskSignalMessage", id, m.type || "", peer, text);
            };
            ws.onclose = () => {
                conns.delete(id);
                invoke("RaskSignalClosed", id);
            };
            // Resolve on open, reject on a failure BEFORE it: after that, onclose is the channel for it.
            ws.onopen = () => resolve(true);
            ws.onerror = () => {
                if (ws.readyState !== WebSocket.OPEN) {
                    conns.delete(id);
                    reject(new Error("Rask signaling: could not connect to " + url.href));
                }
            };
        }),
        send: (id: number, json: string) => {
            const ws = conns.get(id);
            if (!ws || ws.readyState !== WebSocket.OPEN) {
                throw new Error("Rask signaling: connection " + id + " is closed.");
            }
            ws.send(json);
        },
        close: (id: number) => {
            const ws = conns.get(id);
            if (!ws) {
                return;
            }
            conns.delete(id);
            ws.onmessage = null;
            ws.onclose = null;
            ws.onerror = null;
            ws.close();
        }
    };

    function invoke(method: string, ...args: unknown[]) {
        return window.DotNet.invokeMethodAsync("Rask.Core", method, ...args);
    }
})();

// WebRTC (driven by IWebRtc) — an RTCPeerConnection and its data channels can't cross interop, so each is
// held here under an id: C# mints connection ids (it must register its handlers before ICE gathering
// starts), JS mints channel ids (a remote peer can open one at any time, so one minting side keeps the id
// space single). Shared here (not WASM-only): none of this needs a user gesture, so it works over the
// Server client too.
//
// Everything pushed back to C# is BATCHED, and that is load-bearing rather than an optimisation: on the
// Server host each push is one inbound WebSocket frame, and RaskServerLimits.MaxInboundFramesPerSecond
// (1000 by default) closes the socket past it. A busy data channel or an ICE gathering burst would trip
// that in well under a second. Buffering on a fixed FLUSH_MS timer bounds the frame rate to ~60/s no
// matter how fast the peer sends. A timer, not requestAnimationFrame: rAF stops firing in a background
// tab, which would stall delivery exactly when a call is backgrounded.
//
// Message buffers are capped. Past MAX_BUFFERED the oldest are dropped and counted, and the count rides
// the next push so C# can surface the loss — an unbounded buffer would trade a closed socket for an
// out-of-memory tab. ICE candidates are never dropped (a lost candidate can cost connectivity, and a
// gathering burst is tens of entries, not thousands).
window.__raskRtc = window.__raskRtc || (() => {
    const conns = new Map<number, RaskRtcConn>();
    const chans = new Map<number, RaskRtcChan>();
    let nextChan = 0;

    const FLUSH_MS = 16;
    const MAX_BUFFERED = 10000;

    const toBase64 = (buffer: ArrayBuffer) => {
        const bytes = new Uint8Array(buffer);
        let binary = "";
        for (let i = 0; i < bytes.length; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary);
    };

    const fromBase64 = (base64: string) => {
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    };

    const invoke = (method: string, ...args: unknown[]) =>
        window.DotNet.invokeMethodAsync("Rask.Core", method, ...args);

    // A call against an already-disposed connection/channel would otherwise surface as a TypeError on
    // `undefined`, which says nothing about what the app did wrong.
    const conn = (id: number): RaskRtcConn => {
        const c = conns.get(id);
        if (!c) {
            throw new Error("Rask WebRTC: peer connection " + id + " is closed.");
        }
        return c;
    };

    const chan = (id: number): RaskRtcChan => {
        const c = chans.get(id);
        if (!c) {
            throw new Error("Rask WebRTC: data channel " + id + " is closed.");
        }
        return c;
    };

    const flushIce = (id: number) => {
        const c = conns.get(id);
        if (!c) {
            return;
        }
        c.timer = 0;
        if (c.ice.length === 0) {
            return;
        }
        const batch = c.ice;
        c.ice = [];
        invoke("RaskRtcIce", id, batch);
    };

    const flushMessages = (id: number) => {
        const c = chans.get(id);
        if (!c) {
            return;
        }
        c.timer = 0;
        if (!c.listening || c.buf.length === 0) {
            return;
        }
        const batch = c.buf;
        const dropped = c.dropped;
        c.buf = [];
        c.dropped = 0;
        // The connection id rides along because it is the one C# mints: a Server host runs many sessions
        // in one process, and channel ids minted here would collide across them.
        invoke("RaskRtcMessages", c.connId, id, batch, dropped);
    };

    const schedule = (c: { timer: ReturnType<typeof setTimeout> | 0 }, run: () => void) => {
        if (c.timer === 0) {
            c.timer = setTimeout(run, FLUSH_MS);
        }
    };

    // Wires one channel — local or remote — into the id space and starts buffering immediately, so nothing
    // sent between "the channel exists" and "C# called listen" is lost.
    const adopt = (connId: number, ch: RTCDataChannel) => {
        const id = ++nextChan;
        ch.binaryType = "arraybuffer";
        const state: RaskRtcChan = {
            ch: ch, connId: connId, buf: [], dropped: 0, timer: 0, listening: false
        };
        chans.set(id, state);
        ch.onmessage = (e: MessageEvent) => {
            if (state.buf.length >= MAX_BUFFERED) {
                state.buf.shift();
                state.dropped++;
            }
            state.buf.push(typeof e.data === "string"
                ? {text: e.data, data: null}
                : {text: null, data: toBase64(e.data as ArrayBuffer)});
            schedule(state, () => flushMessages(id));
        };
        ch.onclose = () => invoke("RaskRtcChannelClosed", connId, id);
        return id;
    };

    const closeChannel = (id: number) => {
        const c = chans.get(id);
        if (!c) {
            return;
        }
        if (c.timer !== 0) {
            clearTimeout(c.timer);
        }
        chans.delete(id);
        c.ch.onmessage = null;
        c.ch.onclose = null;
        try {
            c.ch.close();
        } catch {
            // Already closed with the connection — nothing to release.
        }
    };

    return {
        isSupported: () => typeof window.RTCPeerConnection === "function",
        create: (id: number, config: RaskRtcConfig | null) => {
            const servers = (config && config.iceServers ? config.iceServers : [])
                .map((u: string) => ({urls: u}));
            const init: RTCConfiguration = {iceServers: servers};
            if (config && config.iceTransportPolicy) {
                init.iceTransportPolicy = config.iceTransportPolicy;
            }
            const pc = new RTCPeerConnection(init);
            // `remote` maps a peer stream's own id to the __raskMedia id we minted for it, so a second
            // ontrack for the same stream doesn't mint (and push) a duplicate. `senders` remembers what
            // AddStream added, so RemoveStream can take exactly those tracks back off.
            const state: RaskRtcConn = {
                pc: pc, ice: [], timer: 0, remote: new Map(), senders: new Map()
            };
            conns.set(id, state);
            pc.onicecandidate = (e: RTCPeerConnectionIceEvent) => {
                // A null candidate marks end-of-gathering; flush what's buffered rather than forwarding it.
                if (!e.candidate) {
                    flushIce(id);
                    return;
                }
                state.ice.push({
                    candidate: e.candidate.candidate,
                    sdpMid: e.candidate.sdpMid,
                    sdpMLineIndex: e.candidate.sdpMLineIndex
                });
                schedule(state, () => flushIce(id));
            };
            pc.onconnectionstatechange = () => invoke("RaskRtcState", id, pc.connectionState);
            pc.ondatachannel = (e: RTCDataChannelEvent) =>
                invoke("RaskRtcChannel", id, adopt(id, e.channel), e.channel.label);
            pc.ontrack = (e) => {
                // A peer's stream is as opaque to C# as a captured one, so it goes into __raskMedia's map
                // and C# gets an id — the same id shape IMediaDevices and MediaCaptureTrigger hand out, so
                // IMediaStreams.AttachAsync works on it unchanged. One push per stream, not per track: a
                // camera+mic peer fires ontrack twice for one stream, and the app wants the stream.
                const stream = (e.streams && e.streams[0]) || null;
                if (!stream || state.remote.has(stream.id)) {
                    return;
                }
                const streamId = window.__raskMedia.adopt(stream);
                state.remote.set(stream.id, streamId);
                invoke("RaskRtcTrack", id, streamId);
            };
        },
        createOffer: async (id: number) => {
            const c = conn(id);
            const offer = await c.pc.createOffer();
            return {type: offer.type, sdp: offer.sdp};
        },
        createAnswer: async (id: number) => {
            const c = conn(id);
            const answer = await c.pc.createAnswer();
            return {type: answer.type, sdp: answer.sdp};
        },
        setLocal: (id: number, d: RaskRtcDescription) =>
            conn(id).pc.setLocalDescription({type: d.type, sdp: d.sdp}),
        setRemote: (id: number, d: RaskRtcDescription) =>
            conn(id).pc.setRemoteDescription({type: d.type, sdp: d.sdp}),
        addIce: (id: number, cand: RTCIceCandidateInit) => conn(id).pc.addIceCandidate({
            candidate: cand.candidate,
            sdpMid: cand.sdpMid,
            sdpMLineIndex: cand.sdpMLineIndex
        }),
        addStream: (connId: number, streamId: number) => {
            const c = conn(connId);
            const stream = window.__raskMedia.get(streamId);
            if (!stream) {
                throw new Error("Rask WebRTC: media stream " + streamId + " is closed.");
            }
            if (c.senders.has(streamId)) {
                return;
            }
            c.senders.set(streamId, stream.getTracks().map((t) => c.pc.addTrack(t, stream)));
        },
        removeStream: (connId: number, streamId: number) => {
            const c = conn(connId);
            const senders = c.senders.get(streamId);
            if (!senders) {
                return;
            }
            c.senders.delete(streamId);
            senders.forEach((s) => {
                try {
                    c.pc.removeTrack(s);
                } catch {
                    // The sender goes away with the connection; removing it afterwards is not an error.
                }
            });
        },
        createChannel: (connId: number, label: string, options: RaskRtcChannelOptions | null) => {
            const init: RTCDataChannelInit = {};
            if (options) {
                if (options.ordered != null) {
                    init.ordered = options.ordered;
                }
                if (options.maxRetransmits != null) {
                    init.maxRetransmits = options.maxRetransmits;
                }
                if (options.protocol) {
                    init.protocol = options.protocol;
                }
            }
            return adopt(connId, conn(connId).pc.createDataChannel(label, init));
        },
        // Starts delivery for a channel. Anything the peer sent before this point is already buffered and
        // rides the first push.
        listen: (id: number) => {
            const c = chans.get(id);
            if (!c) {
                return;
            }
            c.listening = true;
            schedule(c, () => flushMessages(id));
        },
        sendText: (id: number, text: string) => chan(id).ch.send(text),
        sendBytes: (id: number, base64: string) => chan(id).ch.send(fromBase64(base64)),
        closeChannel: (id: number) => closeChannel(id),
        close: (id: number) => {
            const c = conns.get(id);
            if (!c) {
                return;
            }
            if (c.timer !== 0) {
                clearTimeout(c.timer);
            }
            conns.delete(id);
            // Snapshot first: closeChannel deletes from the map we'd otherwise be iterating.
            const owned: number[] = [];
            chans.forEach((chan, chanId) => {
                if (chan.connId === id) {
                    owned.push(chanId);
                }
            });
            owned.forEach(closeChannel);
            c.pc.onicecandidate = null;
            c.pc.onconnectionstatechange = null;
            c.pc.ondatachannel = null;
            c.pc.ontrack = null;
            // Remote streams were minted into __raskMedia by ontrack, so this connection owns them and has
            // to stop their tracks — nothing else holds a reference once the connection is gone. Streams
            // the app supplied to addStream are NOT stopped: the app still owns those.
            c.remote.forEach((streamId) => window.__raskMedia.stop(streamId));
            c.remote.clear();
            c.senders.clear();
            c.pc.close();
        }
    };
})();

// View Transitions (#695). The one Web API here that a user genuinely cannot bolt on: a same-document
// transition has to WRAP the DOM mutation, and the mutation is the framework's morph — an app never
// gets a callback positioned around it. So the runtimes route their commit closure through run()
// below, and this decides whether that commit happens inside document.startViewTransition.
//
// Disabled is the default and is byte-for-byte today's behaviour: run() calls commit synchronously and
// returns whatever it returned. That matters because both runtimes sometimes chain on the result and
// the render queue holds the next frame on it — deferring the commit into a microtask when nobody
// asked for a transition would be a timing change for every app.
window.__raskVt = window.__raskVt || {
    enabled: false,

    supported: () => typeof document !== "undefined" && typeof document.startViewTransition === "function",

    // prefers-reduced-motion is honoured HERE rather than left to the app's CSS, because the
    // animation this drives is the browser's own default cross-fade: there is no stylesheet of ours
    // for a user's motion preference to switch off. A reader who asked for less motion gets the plain
    // commit, and the app needs to know nothing about it.
    reducedMotion: () => typeof window.matchMedia === "function"
        && window.matchMedia("(prefers-reduced-motion: reduce)").matches,

    set(on: boolean) {
        window.__raskVt.enabled = !!on;
        return window.__raskVt.enabled;
    },

    active: () => window.__raskVt.enabled && window.__raskVt.supported() && !window.__raskVt.reducedMotion(),

    // Runs one DOM commit, inside a view transition when one is wanted and possible.
    //
    // Returns the transition's updateCallbackDone rather than its `finished`: the caller is the render
    // queue, which needs to know when the DOM is COMMITTED so it can release the next frame — not when
    // the animation has played out. Holding the queue for the full animation would make a fast
    // sequence of frames queue up behind their own cross-fades.
    run(commit: () => void) {
        if (!window.__raskVt.active()) return commit();
        try {
            const t = document.startViewTransition(commit);
            // A failed transition must never swallow the DOM update, so surface nothing and let the
            // commit stand — startViewTransition has already run it by the time this rejects.
            if (t.finished && typeof t.finished.catch === "function") t.finished.catch(() => {});
            return t.updateCallbackDone;
        } catch {
            // Any throw from the transition machinery (a nested transition, a detached document) falls
            // back to the plain commit rather than losing the frame.
            return commit();
        }
    }
};

// Web Animations (#695). An Animation object cannot cross interop, so this holds them in a map and
// hands C# an integer handle — the same shape __raskMedia uses for a MediaStream, and for the same
// reason.
//
// Unlike the view-transition helper above, prefers-reduced-motion is NOT applied here. These are the
// app's own animations, so the app owns the decision, and it already has IMediaQuery to read the
// preference. Silently refusing to run an animation an app explicitly asked for would be the framework
// overriding a choice it cannot see the intent behind — a loading spinner and a decorative parallax are
// not the same call.
window.__raskAnim = window.__raskAnim || (() => {
    const anims = new Map<number, Animation>();
    let next = 1;

    const get = (id: number) => anims.get(id) || null;

    return {
        supported: () => typeof Element !== "undefined" && typeof Element.prototype.animate === "function",

        // keyframes arrives as the OBJECT form — {opacity: ["0","1"], transform: [...]} — which is what
        // Element.animate takes natively and what serializes as a Dictionary<string, string[]> without
        // any new trim-unsafe JSON shape.
        start: (el: Element | null, keyframes: Record<string, string[]>, options: RaskAnimOptions | null) => {
            if (!el || typeof el.animate !== "function") return 0;
            const opts = options || {};
            const timing: KeyframeAnimationOptions = {
                duration: typeof opts.durationMs === "number" ? opts.durationMs : 400,
                delay: typeof opts.delayMs === "number" ? opts.delayMs : 0,
                // -1 is the wire spelling of Infinity: JSON has no literal for it, and a C# double
                // Infinity would not round-trip.
                iterations: opts.iterations === -1 ? Infinity : (opts.iterations || 1)
            };
            if (opts.easing) timing.easing = opts.easing;
            if (opts.direction) timing.direction = opts.direction;
            if (opts.fill) timing.fill = opts.fill;

            const anim = el.animate(keyframes, timing);
            const id = next++;
            anims.set(id, anim);
            // Drop the handle once the animation can no longer be acted on, so a page that animates on
            // every render does not grow the map forever. `finished` rejects on cancel, which is not an
            // error here — either way the animation is done with.
            const forget = () => anims.delete(id);
            anim.finished.then(forget, forget);
            return id;
        },

        // Each of these is a no-op on an unknown handle rather than a throw: the animation may simply
        // have finished and been forgotten, which is not a caller error.
        cancel: (id: number) => { const a = get(id); if (a) a.cancel(); },
        finish: (id: number) => { const a = get(id); if (a) a.finish(); },
        pause: (id: number) => { const a = get(id); if (a) a.pause(); },
        play: (id: number) => { const a = get(id); if (a) a.play(); },

        // true when it ran to completion, false when it was cancelled or is already gone. Never throws,
        // so `await` at a call site does not need a try/catch around an ordinary cancel.
        finished: (id: number) => {
            const a = get(id);
            if (!a) return Promise.resolve(false);
            return a.finished.then(() => true, () => false);
        }
    };
})();
