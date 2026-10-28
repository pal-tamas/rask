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
// A FRAME WAITS A MOMENT, NOT FOR EVER. While an answer is owed, and while the connection is being opened again,
// the host keeps what the page sends and lets it go afterwards: on the connection that was there when the answer
// is "the same", on the new one otherwise. An answer that is slow to come — a loaded host, a busy page — holds
// it for HOLD_MS of the page's own time and no longer: then it leaves on the connection that is open, where the
// server checks every event against the session's user as it always does, and the answer, when it comes, is
// still acted on. No answer at all — a timeout, a network error, a server that could not say — is not "changed":
// the connection stays, and the next trigger asks again. Only an answer that names another principal opens it
// again; a tab that dropped its connection whenever the server was slow would drop it exactly when it is loaded.
//
// The HTTP transport needs none of it: every post carries the cookie, and the server adopts what it brings.

/** How long an answer is trusted in the window that has the focus. */
export const FRESH_FOCUSED_MS = 30_000;

/** How long an answer is trusted in a window that does not: only a pointer reaches it. */
export const FRESH_UNFOCUSED_MS = 2_000;

/** How long to wait before asking again when the server said "later". */
export const RETRY_MS = 500;

/** How many times "later" is taken before the question is let go, as one that got no answer. */
export const RETRIES = 5;

/** How long, in the page's own time, what the page sends waits for an answer before it leaves anyway. */
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
    /** Drops the connection and opens it again. The host calls `opened` when the new one is up. */
    reopen(): void;
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
    // Set when a question has been out for HOLD_MS: what the page sends meanwhile is no longer held.
    let late = false;
    let reopening = false;
    let hadUser = host.bootedWithUser;
    // Which question is out, so a hold that lapses after its answer came lets nothing go twice.
    let question = 0;

    function stale(): boolean {
        return owed || host.now() - answeredAt > (host.focused() ? FRESH_FOCUSED_MS : FRESH_UNFOCUSED_MS);
    }

    // Whether the question that is out was started by another tab's word, and how often it was put off.
    let told = false;
    let putOff = 0;

    function answered(answer: WhoAnswer | null): void {
        // Asked too often: not an answer. What waits goes on waiting, and the question is put again — a few times,
        // and then it is let go as one that got none.
        if (answer?.later === true && putOff < RETRIES) {
            putOff++;
            host.wait(put, RETRY_MS);
            return;
        }
        // `owed` is left as it is: the tab may have been left while the answer travelled, and that doubt is a
        // new one.
        asking = false;
        late = false;
        putOff = 0;
        answeredAt = host.now();
        if (answer?.same === false) {
            reopening = true;
            if (!told) host.tellOthers();
            host.reopen();
            return;
        }
        if (answer?.same === true && typeof answer.user === "boolean") hadUser = answer.user;
        host.release();
    }

    function put(): void {
        host.ask().then(answered, () => answered(null));
    }

    function ask(byWord: boolean): void {
        if (asking || reopening || host.cookieOnEveryFrame()) return;
        asking = true;
        late = false;
        owed = false;
        told = byWord;
        const asked = ++question;
        host.wait(() => {
            if (!asking || asked !== question) return;
            late = true;
            host.release();
        }, HOLD_MS);
        put();
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
            if (reopening) return true;
            if (asking) return !late;
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
