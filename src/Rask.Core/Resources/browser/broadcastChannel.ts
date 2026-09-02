// Cross-tab messaging — BroadcastChannel.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Every tab, worker and iframe on the same ORIGIN that opened the same channel name receives what you
// post. A channel never receives its own posts, so a tab does not have to filter itself out.

export interface Channel {
    /** Send to every other listener on this channel name. */
    post(message: string): void;
    close(): void;
}

/**
 * Open a channel and start listening. Nothing arrives before this returns, so opening late means
 * missing what was posted meanwhile — there is no buffer and no replay.
 *
 * Non-string payloads posted by another sender are JSON-stringified on the way in, so a handler
 * always receives a string.
 */
export function open(name: string, onMessage: (message: string) => void): Channel {
    const channel = new BroadcastChannel(name);
    channel.onmessage = (e: MessageEvent) => {
        onMessage(typeof e.data === "string" ? e.data : JSON.stringify(e.data));
    };

    let closed = false;
    return {
        post: (message: string) => {
            if (!closed) {
                channel.postMessage(message);
            }
        },
        close: () => {
            if (closed) {
                return;
            }
            closed = true;
            channel.close();
        }
    };
}
