// Whether the browser's credential still names the user this tab draws its page for — the browser host's wiring of
// rask-auth-watch.ts, which a Server tab uses too.
//
// A WASM tab reads its user once, at boot. Every request it makes carries the cookie as it is now, so the server is
// never fooled; the page is. Sign out in another tab, let the cookie lapse, and this one goes on drawing the page for
// whoever it booted with. So it asks, at the moments a Server tab does: as it becomes the reader's again, when another
// tab says the credential changed, and before the first event after the answer went stale. The question is .NET's
// (WasmUserWatch: the user provider asked again), and so is acting on the answer — another user is simply taken, and
// nobody where there was somebody sends the page to the sign-in page with the way back.
//
// What the reader does while an answer is owed waits for it, as a Server tab's frames do, and then goes to .NET in
// order. When the answer is that the browser carries somebody else now, or nobody, it is dropped instead, as a Server
// tab's is with the page it reloads: it was done on a page drawn for a user who has gone, and nothing runs for them.
// A slow answer holds it HOLD_MS and no longer, and no answer at all is no change.

import { ASK_TIMEOUT_MS, createAuthWatch, type WhoAnswer } from "../../Rask.Core/Resources/rask-auth-watch.js";

/** What WasmUserWatch answers: a WhoAnswer, or `off` when the app has no user to watch. An empty answer is none. */
interface WasmWhoAnswer extends WhoAnswer {
    off?: boolean;
}

export interface UserWatch {
    /** Sends what the reader did through `go` now, or once it is known who the browser carries now. */
    leave(go: () => Promise<void>): Promise<void>;
}

/**
 * Watches the tab's user. `asker` hands back .NET's question, or nothing while .NET is not up — and nothing waits
 * while it is not, since there is nobody yet to ask.
 */
export function watchUser(asker: () => (() => Promise<string>) | undefined): UserWatch {
    // The app has no user to watch: nothing is ever held again.
    let off = false;
    const held: Array<{ go: () => void; drop: () => void }> = [];
    const channel = typeof BroadcastChannel === "function" ? new BroadcastChannel("rask-auth") : null;
    // Node (which the fixtures import this bundle in) keeps a process alive while a channel is open; a page has no
    // such method, and goes away with its tab.
    (channel as { unref?: () => void } | null)?.unref?.();

    function read(json: string): WhoAnswer | null {
        if (!json) return null;
        const answer = JSON.parse(json) as WasmWhoAnswer;
        if (answer.off === true) {
            off = true;
            return {same: true};
        }
        return answer;
    }

    // Bounded like a Server tab's question: one that does not come back in time is no answer.
    function askDotnet(): Promise<WhoAnswer | null> {
        const ask = asker();
        if (!ask) return Promise.resolve(null);
        return new Promise((resolve) => {
            const timer = setTimeout(() => resolve(null), ASK_TIMEOUT_MS);
            ask().then((json) => resolve(read(json)), () => resolve(null))
                .finally(() => clearTimeout(timer));
        });
    }

    function release(): void {
        for (const waiting of held.splice(0)) waiting.go();
    }

    const watch = createAuthWatch({
        ask: askDotnet,
        // There is no connection to open again: .NET has taken the answer already, and draws the page for whoever
        // the browser carries now. What waited is dropped, and as nothing opens, the watch lets go at once.
        reopen: () => {
            for (const waiting of held.splice(0)) waiting.drop();
            return false;
        },
        release,
        tellOthers: () => channel?.postMessage(1),
        wait: (again, ms) => {
            setTimeout(again, ms);
        },
        now: () => Date.now(),
        focused: () => document.hasFocus(),
        visible: () => document.visibilityState === "visible",
        // Every request already carries the cookie; the page's user is what can be stale. Nothing is held before .NET
        // can be asked, nor in an app with no user.
        cookieOnEveryFrame: () => off || !asker(),
        bootedWithUser: false,
    });

    if (channel) channel.onmessage = () => watch.noticed(true);
    document.addEventListener("visibilitychange", () => {
        if (document.visibilityState === "visible") watch.noticed();
        else watch.left();
    });
    window.addEventListener("focus", () => watch.noticed());
    window.addEventListener("blur", () => watch.left());
    // A page brought back from the back/forward cache was never told anything while it was there.
    window.addEventListener("pageshow", (e: PageTransitionEvent) => {
        if (e.persisted) watch.noticed();
    });

    return {
        leave(go) {
            if (held.length === 0 && !watch.holds()) return go();
            return new Promise<void>((resolve) => {
                held.push({go: () => { go().then(resolve, resolve); }, drop: resolve});
            });
        },
    };
}
