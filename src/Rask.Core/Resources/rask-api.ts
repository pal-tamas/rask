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

// The extracted browser layer. Importing it registers window.__raskApi and the other window.__rask* namespaces,
// which used to be defined in this file; the implementations now live in ./browser/ as ordinary
// modules a TypeScript front end can import directly. See ./browser/globals.ts.
import "./browser/globals.js";

// A typed ElementRef's DOM members (generated from MDN into ElementRefMembers): one operation, read or write on the
// element, by the member's MDN name, which only the generated C# supplies. The JSON reviver has already resolved the
// ref to the live element. A call on an element no longer on the page does nothing, as focusing a closed dialog's
// input would; a read or write there is an error the caller hears about.
type RaskElementMembers = Record<string, unknown>;
const raskMember = (el: Element | null, name: string, verb: string): RaskElementMembers => {
    if (!el) throw new Error(`Rask: an element ref's ${verb} of ${name} found no element on the page`);
    return el as unknown as RaskElementMembers;
};
window.__raskEl = window.__raskEl || {
    call: (el: Element | null, name: string, ...args: unknown[]) => {
        if (!el) return undefined;
        const fn = (el as unknown as RaskElementMembers)[name];
        if (typeof fn !== "function") throw new Error(`Rask: <${el.localName}> has no ${name}()`);
        return (fn as (...a: unknown[]) => unknown).apply(el, args);
    },
    get: (el: Element | null, name: string) => raskMember(el, name, "read")[name],
    set: (el: Element | null, name: string, value: unknown) => {
        raskMember(el, name, "write")[name] = value;
    }
};

// Rask.Web's generated globals and MDN interfaces (`Navigator.Clipboard.WriteText("hi")`): a chain of property reads
// ("g"), method calls ("c"), constructors ("n") and one write ("s") from the window, from an object a chain kept (an IJSObjectReference,
// revived to the object), or from an element ref's element ("e", the ref revived beside the steps), run in one round trip. The steps arrive as JSON Rask.Web wrote with its own metadata; their
// names come from its generated code, so only MDN's members are ever reached.
type RaskWebStep = [kind: "e" | "g" | "c" | "n" | "s", name: string, args?: unknown[]];
const raskWebUnsafe = new Set(["__proto__", "prototype", "constructor"]);
const raskWebWalk = (root: unknown, steps: RaskWebStep[]): unknown => {
    let target = (root ?? window) as Record<string, unknown> | null | undefined;
    for (const [kind, name, args] of steps) {
        if (raskWebUnsafe.has(name)) throw new Error(`Rask: ${name} is not a web API member`);
        if (target == null) throw new Error(`Rask: there is no object to read ${name} from`);
        if (kind === "e") {
            // Never the window in its place: a ref whose element has left the page is an error, not a write to window.
            target = args?.[0] as Record<string, unknown> | null | undefined;
            if (target == null) throw new Error("Rask: an element ref found no element on the page");
        } else if (kind === "g") {
            target = target[name] as Record<string, unknown> | null | undefined;
        } else if (kind === "c") {
            const fn = target[name];
            if (typeof fn !== "function") throw new Error(`Rask: ${name} is not a function here`);
            target = (fn as (...a: unknown[]) => unknown).apply(target, args ?? []) as Record<string, unknown>;
        } else if (kind === "n") {
            const ctor = target[name];
            if (typeof ctor !== "function") throw new Error(`Rask: ${name} is not a constructor here`);
            target = new (ctor as new (...a: unknown[]) => Record<string, unknown>)(...(args ?? []));
        } else {
            target[name] = args?.[0];
            return undefined;
        }
    }
    return target;
};
// What JSON cannot carry — a C# handler, a kept object, an element — arrives as its own argument, revived by the host,
// and the steps name it by position: {"__raskArg__": 0}.
// A C# handler among them is handed what the browser calls it with as data, and the browser gets back the promise of
// its finishing, which a lock waits on. C#'s bytes arrive as {"__raskBytes__": base64}, and go in as a Uint8Array.
// The app's own value (an `any`) is written by Rask.Web and revived by the host, {"__raskAny__": 0}, so a kept object
// inside it is the object; its bytes are turned back into Uint8Arrays wherever they are in it.
const raskWebBytes = (value: unknown): Uint8Array | undefined => {
    const bytes = value && typeof value === "object" ? (value as { __raskBytes__?: unknown }).__raskBytes__ : undefined;
    return typeof bytes === "string" ? Uint8Array.from(atob(bytes), c => c.charCodeAt(0)) : undefined;
};
const raskWebAny = (value: unknown, depth = 0): unknown => {
    if (value === null || typeof value !== "object" || depth > 32) return value;
    const bytes = raskWebBytes(value);
    if (bytes) return bytes;
    if (Array.isArray(value)) return value.map(v => raskWebAny(v, depth + 1));
    // What the host revived (a kept object, an element) is itself, not data to walk.
    if (Object.getPrototypeOf(value) !== Object.prototype) return value;
    const data: Record<string, unknown> = {};
    for (const key of Object.keys(value)) {
        if (!raskWebUnsafe.has(key)) data[key] = raskWebAny((value as Record<string, unknown>)[key], depth + 1);
    }
    return data;
};
const raskWebSteps = (steps: string, extras: unknown[]): RaskWebStep[] =>
    JSON.parse(steps, (_key, value: unknown) => {
        const bytes = raskWebBytes(value);
        if (bytes) return bytes;
        const any = value && typeof value === "object" ? (value as { __raskAny__?: unknown }).__raskAny__ : undefined;
        if (typeof any === "number") return raskWebAny(extras[any]);
        const index = value && typeof value === "object" ? (value as { __raskArg__?: unknown }).__raskArg__ : undefined;
        if (typeof index !== "number") return value;
        const extra = extras[index];
        return typeof extra === "function"
            ? (...args: unknown[]) => (extra as (...a: unknown[]) => unknown)(...args.map(a => raskWebData(a)))
            : extra;
    }) as RaskWebStep[];
// What crosses back to C# as a value. An object that serializes itself (DOMRect's toJSON) or is plain stays as it is;
// one that is only the browser's — an IntersectionObserverEntry, a DOMException — crosses as the fields it can be
// read for, which JSON alone would turn into {}. A buffer, or a view of one, crosses as its bytes in base64, which C#
// reads as a byte[]. An element crosses as the app's ElementRef to it, by its data-rask-ref, or not at all; another node,
// a window or a function does not cross.
const raskWebBase64 = (value: ArrayBuffer | ArrayBufferView): string => {
    const bytes = value instanceof ArrayBuffer ? new Uint8Array(value) : new Uint8Array(value.buffer, value.byteOffset, value.byteLength);
    let binary = "";
    for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
    return btoa(binary);
};
const raskWebData = (value: unknown, depth = 0): unknown => {
    if (typeof value === "function") return undefined;
    if (value instanceof ArrayBuffer || ArrayBuffer.isView(value)) return raskWebBase64(value);
    if (value === null || typeof value !== "object") return value;
    if (value instanceof Element && value.hasAttribute("data-rask-ref")) return { __raskRef__: value.getAttribute("data-rask-ref") };
    if (depth > 4 || value instanceof Node || value === window) return undefined;
    if (Array.isArray(value)) return value.map(v => raskWebData(v, depth + 1));
    if (typeof (value as { toJSON?: unknown }).toJSON === "function") return value;
    const own = Object.getPrototypeOf(value) === Object.prototype || Object.getPrototypeOf(value) === null;
    const data: Record<string, unknown> = {};
    for (const key in value) {
        if (raskWebUnsafe.has(key) || (own && !Object.prototype.hasOwnProperty.call(value, key))) continue;
        try {
            const field = raskWebData((value as Record<string, unknown>)[key], depth + 1);
            if (field !== undefined) data[key] = field;
        } catch {
            // A getter that throws in this state (a detached object's) has nothing to say.
        }
    }
    return data;
};
// A subscription's listener, by the id C# holds to remove it, and the timer of its throttle while one runs.
const raskWebListeners = new Map<number, { target: EventTarget; type: string; listener: (event: Event) => void; stop: () => void }>();
let raskWebNextListener = 0;
window.__raskWeb = window.__raskWeb || {
    run: (root: unknown, steps: string, ...extras: unknown[]) => raskWebWalk(root, raskWebSteps(steps, extras)),
    // The same, for a value C# reads: what it ends at (or resolves to) as data.
    read: async (root: unknown, steps: string, ...extras: unknown[]) => raskWebData(await raskWebWalk(root, raskWebSteps(steps, extras))),
    // Which slots of the array the chain ends at hold an item (a gamepad's can be empty): C# then keeps each by index.
    slots: async (root: unknown, steps: string, ...extras: unknown[]) =>
        Array.from(((await raskWebWalk(root, raskWebSteps(steps, extras))) ?? []) as ArrayLike<unknown>, item => item != null),
    // Whether the browser has what the chain ends at: the object before it exists and holds a member of that name.
    has: (root: unknown, steps: string, ...extras: unknown[]) => {
        const parsed = raskWebSteps(steps, extras);
        const last = parsed.pop();
        try {
            const owner = raskWebWalk(root, parsed);
            return last === undefined || (owner != null && last[1] in Object(owner));
        } catch {
            return false;
        }
    },
    // Listens on what the chain ends at; each event reaches C# as the fields its payload type reads, and nothing else
    // (an event's `view` is a Window, which does not serialize). A `*device` field is a live object, kept for C# as
    // a handle the host holds until C# lets it go. With `every` (ms), a hot event (a sensor's, ~60 a second) is
    // throttled here: the first crosses at once, then at most one per interval — the latest, so the last reading always
    // arrives. The events between are dropped before any payload is built, so none of their live fields is kept.
    listen: (root: unknown, steps: string, type: string, fields: string, handler: (payload: unknown) => void, every: number | null, ...extras: unknown[]) => {
        const target = raskWebWalk(root, raskWebSteps(steps, extras)) as EventTarget | null;
        if (!target || typeof target.addEventListener !== "function") throw new Error(`Rask: there is nothing to listen to for ${type}`);
        const names = JSON.parse(fields) as string[];
        const send = (event: Event) => {
            const payload: Record<string, unknown> = {};
            for (const field of names) {
                const name = field.replace(/^\*/, "");
                // A bare `*` is the event itself, kept for its own methods (a BeforeInstallPromptEvent's prompt()).
                const value = name ? (event as unknown as Record<string, unknown>)[name] : event;
                payload[name] = field === name ? raskWebData(value)
                    : value == null ? null : window.DotNet.createJSObjectReference?.(value);
            }
            handler(payload);
        };
        const interval = every ?? 0;
        let timer = 0;
        let pending: Event | null = null;
        const flush = () => {
            if (pending) {
                send(pending);
                pending = null;
                timer = window.setTimeout(flush, interval);
            } else {
                timer = 0;
            }
        };
        const listener = !interval ? send : (event: Event) => {
            if (timer) {
                pending = event;
                return;
            }
            send(event);
            timer = window.setTimeout(flush, interval);
        };
        target.addEventListener(type, listener);
        const id = ++raskWebNextListener;
        raskWebListeners.set(id, { target, type, listener, stop: () => window.clearTimeout(timer) });
        return id;
    },
    unlisten: (id: number) => {
        const entry = raskWebListeners.get(id);
        if (!entry) return;
        entry.target.removeEventListener(entry.type, entry.listener);
        entry.stop();
        raskWebListeners.delete(id);
    }
};

// Gesture-bridge DOM helpers — moved here from rask-wasm-api.js so they ship to the Server client too.
// They drive activation-gated browser APIs that must run inside a click gesture; the declarative
// FullscreenTrigger / EyeDropperTrigger components (and the data-rask-gesture click handler in
// rask-events.js) call these synchronously in the gesture, which is why they work even on the Server
// transport. An imperative RequestFullscreen() / EyeDropper.Open() from Rask.Web runs in WebAssembly only.

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
