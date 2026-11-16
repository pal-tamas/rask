// Whether the browser's credential still names the user this tab's connection opened with.
//
// A request carries the browser's cookie as it is at that moment. A socket carries it once, at its upgrade, and
// then stays open for hours: sign in or out in another tab, or let another page of the site issue the token
// again with another tenant, and this tab goes on acting for whoever it was opened by. Nothing tells it.
//
// So it asks. A small request that is authenticated like any other (`/_rask/auth/who`, the session in its
// Rask-Session header) says only "the same" or "not the same", and "not the same" makes the tab drop its
// connection and open it again, so the upgrade carries the cookie as it is now and the server adopts the
// principal or refuses it.
//
// WHEN. A reader can only start something in a tab they are looking at, and "changed it over there, came back
// here" always passes through the tab becoming visible or its window taking focus. Those ask at once. What
// they cannot see — a script in another frame swapping the cookie while this window keeps the focus, a pointer
// moving over a window that never took it — is bounded instead: an answer is trusted for FRESH_FOCUSED_MS in
// the focused window and FRESH_UNFOCUSED_MS in any other, and the first frame after that waits for a new one.
//
// A FRAME NEVER LEAVES IN DOUBT ON A SOCKET. While an answer is owed, and while the connection is being opened
// again, the host keeps what the page sends and lets it go afterwards: on the connection that was there when the
// answer is "the same", on the new one otherwise. An answer that names another principal opens it again. So does a
// question that has not been answered within HOLD_MS of the page's own time — a loaded host, a slow network — and one
// that will not be answered at all: a timeout, a network error, an offline page, a server that could not say, "later"
// for as long as it is put. What waited then leaves on a connection whose upgrade carried the cookie as it is NOW,
// never on the one opened for the user before: the server runs it for that user when the browser still carries them,
// under their new rights when those changed, and drops it with the page when it carries another user or nobody —
// the tab loads again as them, or goes to the sign-in page, exactly as when the answer is in time, and the reader
// presses again. An answer that comes after that is not acted on: the connection it would have opened is open. A
// loaded host makes its signed-in tabs reconnect; that is the price of never acting for a user who has gone. A tab
// that had nobody's page has no user to act for by mistake: it lets what waited go on the connection it has, and
// still acts on the answer when it comes.
//
// The HTTP transport needs none of it: every post carries the cookie, and the server adopts what it brings.
//
// BOTH HOSTS. The Server runtime (rask.ts) asks /_rask/auth/who and opens its connection again. The WASM runtime
// (rask-wasm-auth.ts) has no connection whose cookie can be stale — every request carries the one there is now — but
// it has a page drawn for the user it booted with: it asks .NET, which asks its user provider again, and "not the
// same" drops what the page held and draws it for whoever the browser carries now. Its requests carry the cookie, so
// a question in doubt lets what waited go to .NET (`socket` is false there): the server is never fooled by it.

/** How long an answer is trusted in the window that has the focus. */
export const FRESH_FOCUSED_MS = 30_000;

/** How long an answer is trusted in a window that does not: only a pointer reaches it. */
export const FRESH_UNFOCUSED_MS = 2_000;

/** How long to wait before asking again when the server said "later". */
export const RETRY_MS = 500;

/** How many times "later" is taken before the question is let go, as one that got no answer. */
export const RETRIES = 5;

/**
 * How long, in the page's own time, what the page sends waits for an answer before the connection is opened again
 * for it (or, where frames carry the cookie themselves, before it leaves anyway).
 */
export const HOLD_MS = 1_000;

/** How long, in the page's own time, a question is given before it counts as unanswered. */
export const ASK_TIMEOUT_MS = 5_000;

/**
 * What the server said: the same user with the same rights, and whether that is somebody — or `later`, when this
 * tab has asked too often and is to ask again.
 */
export interface WhoAnswer {
    same?: boolean;
    user?: boolean;
    later?: boolean;
}

export interface AuthWatchHost {
    /** Asks the server. A rejection, or anything that is not an answer, is no answer: nothing changes. */
    ask(): Promise<WhoAnswer | null>;
    /**
     * Drops the connection and opens it again. The host calls `opened` when the new one is up. Returns false when it
     * dropped nothing — no connection was open, or closing it failed — and so no `opened` is coming for it.
     */
    reopen(): boolean;
    /** Sends what was kept while an answer was owed, on the connection that is open. */
    release(): void;
    /** Tells the browser's other tabs that this one found the credential changed. */
    tellOthers(): void;
    /** Runs `again` after `ms` of the page's own time. */
    wait(again: () => void, ms: number): void;
    now(): number;
    focused(): boolean;
    visible(): boolean;
    /** Whether every frame already travels in a request of its own, and so carries the cookie itself. */
    cookieOnEveryFrame(): boolean;
    /** Whether the page this tab booted from was rendered for somebody. */
    bootedWithUser: boolean;
    /**
     * Whether what the page sends rides a connection that saw the cookie only as it opened: then nothing leaves on it
     * while it is in doubt who the browser carries, and doubt opens it again.
     */
    socket: boolean;
}

export interface AuthWatch {
    /**
     * The tab is the reader's again, or (`told`) another tab said the credential changed: ask, now if it can be
     * seen. What a tab finds out because it was told, it does not tell on: the tabs would wake each other for ever.
     */
    noticed(told?: boolean): void;
    /** The tab was left: the next frame asks first. */
    left(): void;
    /** Whether the frame about to leave must wait. Asking starts here when an answer is owed. */
    holds(): boolean;
    /** A connection has opened, so the server has just seen the cookie as it is. */
    opened(): void;
    /** Whether this tab last knew itself to be somebody's. */
    readonly hadUser: boolean;
}

export function createAuthWatch(host: AuthWatchHost): AuthWatch {
    let answeredAt = host.now();
    let owed = false;
    let asking = false;
    let reopening = false;
    let hadUser = host.bootedWithUser;
    // Which question is out, so an answer, a "later" or a lapsed hold of one given up on acts on nothing.
    let question = 0;

    function stale(): boolean {
        return owed || host.now() - answeredAt > (host.focused() ? FRESH_FOCUSED_MS : FRESH_UNFOCUSED_MS);
    }

    // Whether the question that is out was started by another tab's word, and how often it was put off.
    let told = false;
    let putOff = 0;

    // The connection is dropped so the next upgrade carries the cookie as it is now, and what waited leaves on it.
    function openAgain(): void {
        reopening = true;
        if (host.reopen()) return;
        // Nothing was dropped, so nothing will open to end the hold: let it go now, or what the page sends waits
        // for ever. With no connection open it waits for the next, whose upgrade carries the cookie as it is then.
        reopening = false;
        host.release();
    }

    // Unanswered — in time or at all. Not "changed", so the other tabs are not told; but on a socket, and for a tab
    // that had somebody's page, nothing waited leaves on the connection opened for them. An answer that comes after
    // the connection was opened again is not acted on; one that comes after what waited was let go still is.
    function doubted(): void {
        asking = false;
        putOff = 0;
        answeredAt = host.now();
        if (host.socket && hadUser) {
            question++;
            openAgain();
        } else {
            host.release();
        }
    }

    function answered(asked: number, answer: WhoAnswer | null): void {
        if (asked !== question) return;
        if (!asking) {
            if (answer?.same === false && !reopening) {
                if (!told) host.tellOthers();
                openAgain();
            } else if (answer?.same === true && typeof answer.user === "boolean") {
                hadUser = answer.user;
            }
            return;
        }
        // Asked too often: not an answer. What waits goes on waiting, and the question is put again — a few times,
        // and then it is let go as one that got none.
        if (answer?.later === true && putOff < RETRIES) {
            putOff++;
            host.wait(() => put(asked), RETRY_MS);
            return;
        }
        if (answer?.same === false) {
            // `owed` is left as it is: the tab may have been left while the answer travelled, and that doubt is a
            // new one.
            asking = false;
            putOff = 0;
            answeredAt = host.now();
            if (!told) host.tellOthers();
            openAgain();
            return;
        }
        if (answer?.same !== true) {
            doubted();
            return;
        }
        asking = false;
        putOff = 0;
        answeredAt = host.now();
        if (typeof answer.user === "boolean") hadUser = answer.user;
        host.release();
    }

    function put(asked: number): void {
        if (!asking || asked !== question) return;
        host.ask().then((answer) => answered(asked, answer), () => answered(asked, null));
    }

    function ask(byWord: boolean): void {
        if (asking || reopening || host.cookieOnEveryFrame()) return;
        asking = true;
        owed = false;
        told = byWord;
        putOff = 0;
        const asked = ++question;
        host.wait(() => {
            if (asking && asked === question) doubted();
        }, HOLD_MS);
        put(asked);
    }

    return {
        noticed(byWord) {
            owed = true;
            if (host.visible()) ask(byWord === true);
        },
        left() {
            owed = true;
        },
        holds() {
            if (host.cookieOnEveryFrame()) return false;
            if (reopening || asking) return true;
            if (!stale()) return false;
            ask(false);
            return true;
        },
        opened() {
            reopening = false;
            owed = false;
            answeredAt = host.now();
        },
        get hadUser() {
            return hadUser;
        },
    };
}
