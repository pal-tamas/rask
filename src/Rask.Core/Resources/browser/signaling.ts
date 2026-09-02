// The signaling channel two peers meet over — a WebSocket to Rask's relay.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// WebRTC cannot bootstrap itself: before two browsers can talk directly they have to trade an offer,
// an answer and their ICE candidates, and something has to carry that. This is that something — a
// room on the relay `Rask.Signaling` hosts (`MapRaskSignaling()`), which is one of the two pieces of
// this layer a front end genuinely cannot write for itself, because the server half is ours.
//
// The payload is an OPAQUE STRING end to end. Nothing here parses an SDP or a candidate, which is why
// the same relay serves any peer-to-peer protocol you care to put over it.

export interface SignalingMessage {
    /** "joined", "peer-joined", "peer-left", "signal", "error" — whatever the relay sends. */
    type: string;
    /** The peer this concerns: the sender for a signal, the joiner or leaver otherwise. */
    peer: string;
    /**
     * One string slot, per message type: the application payload for a signal, the error text for an
     * error, and for your own join, the peer list as JSON.
     */
    payload: string;
}

export interface SignalingHandlers {
    onMessage: (message: SignalingMessage) => void;
    onClose: () => void;
}

export interface SignalingConnection {
    /** Send a raw JSON frame to the relay. Throws once the socket is closed. */
    send(json: string): void;
    close(): void;
}

export function isSupported(): boolean {
    return typeof WebSocket === "function";
}

/**
 * Open a signaling connection. `path` is relative to the page — the scheme is upgraded to ws/wss to
 * match, so an https page never opens an insecure socket.
 *
 * Resolves once the socket is OPEN, and rejects only on a failure before that. After it is open, a
 * drop arrives through `onClose` rather than as a rejection: there is nothing left to reject.
 */
export function open(path: string, handlers: SignalingHandlers): Promise<SignalingConnection> {
    return new Promise<SignalingConnection>((resolve, reject) => {
        const url = new URL(path, window.location.href);
        url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
        const ws = new WebSocket(url.href);

        ws.onmessage = (e: MessageEvent) => {
            let m;
            try {
                m = JSON.parse(e.data);
            } catch (_) {
                return; // not ours to interpret; drop it rather than tear the connection down
            }
            handlers.onMessage({
                type: m.type || "",
                peer: m.peerId || m.from || "",
                payload: m.type === "joined"
                    ? JSON.stringify(m.peers || [])
                    : (m.payload != null ? m.payload : (m.message || ""))
            });
        };

        ws.onclose = () => handlers.onClose();

        ws.onopen = () => resolve({
            send: (json: string) => {
                if (ws.readyState !== WebSocket.OPEN) {
                    throw new Error("Rask signaling: the connection is closed.");
                }
                ws.send(json);
            },
            close: () => {
                ws.onmessage = null;
                ws.onclose = null;
                ws.onerror = null;
                ws.close();
            }
        });

        ws.onerror = () => {
            if (ws.readyState !== WebSocket.OPEN) {
                reject(new Error("Rask signaling: could not connect to " + url.href));
            }
        };
    });
}
