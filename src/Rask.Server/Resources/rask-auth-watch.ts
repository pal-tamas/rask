// Whether the browser's credential still names the user this tab's connection opened with.
//
// A request carries the browser's cookie as it is at that moment. A socket carries it once, at its upgrade, and
// then stays open for hours: sign in or out in another tab, or let another page of the site issue the token
// again with another tenant, and this tab goes on acting for whoever it was opened by. Nothing tells it.
//
// So it asks. A small request that is authenticated like any other (`/_rask/auth/who/{session}`) says only
// "the same" or "not the same", and anything but "the same" makes the tab drop its connection and open it
// again, so the upgrade carries the cookie as it is now and the server adopts the principal or refuses it.
//
// WHEN. A reader can only start something in a tab they are looking at, and "changed it over there, came back
// here" always passes through the tab becoming visible or its window taking focus. Those ask at once. What
// they cannot see — a script in another frame swapping the cookie while this window keeps the focus, a pointer
// moving over a window that never took it — is bounded instead: an answer is trusted for FRESH_FOCUSED_MS in
// the focused window and FRESH_UNFOCUSED_MS in any other, and the first frame after that waits for a new one.
//
// NOTHING IS SENT UNDER A DOUBT. While an answer is owed, and while the connection is being opened again, the
// host keeps what the page sends and lets it go afterwards: on the connection that was there when the answer
// is "the same", on the new one otherwise. So no frame leaves on the old connection once this tab knows, or
// should have asked, and none is lost.
//
// The HTTP transport needs none of it: every post carries the cookie, and the server adopts what it brings.

/** How long an answer is trusted in the window that has the focus. */
export const FRESH_FOCUSED_MS = 30_000;

/** How long an answer is trusted in a window that does not: only a pointer reaches it. */
export const FRESH_UNFOCUSED_MS = 2_000;

/** How long to wait before asking again when the server said "later". */
export const RETRY_MS = 500;

/** How many times "later" is taken before the connection is opened again instead. */
export const RETRIES = 5;

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
    /** Asks the server. A rejection, or anything that is not an answer, is taken as "not the same". */
    ask(): Promise<WhoAnswer>;
    /** Drops the connection and opens it again. The host calls `opened` when the new one is up. */
    reopen(): void;
    /** Sends what was kept while an answer was owed, on the connection that is open. */
    release(): void;
    /** Tells the browser's other tabs that this one found the credential changed. */
    tellOthers(): void;
    /** Runs `again` after `ms`. */
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
    let reopening = false;
    let hadUser = host.bootedWithUser;

    function stale(): boolean {
        return owed || host.now() - answeredAt > (host.focused() ? FRESH_FOCUSED_MS : FRESH_UNFOCUSED_MS);
    }

    // Whether the question that is out was started by another tab's word, and how often it was put off.
    let told = false;
    let putOff = 0;

    function answered(answer: WhoAnswer | null): void {
        // Asked too often: not an answer. What waits goes on waiting, and the question is put again — a few times,
        // and then the connection is opened again, where the server's own attach decides.
        if (answer && answer.later === true && putOff < RETRIES) {
            putOff++;
            host.wait(put, RETRY_MS);
            return;
        }
        // `owed` is left as it is: the tab may have been left while the answer travelled, and that doubt is a
        // new one.
        asking = false;
        putOff = 0;
        answeredAt = host.now();
        if (answer && answer.same === true) {
            if (typeof answer.user === "boolean") hadUser = answer.user;
            host.release();
            return;
        }
        reopening = true;
        if (!told) host.tellOthers();
        host.reopen();
    }

    function put(): void {
        host.ask().then(answered, () => answered(null));
    }

    function ask(byWord: boolean): void {
        if (asking || reopening || host.cookieOnEveryFrame()) return;
        asking = true;
        owed = false;
        told = byWord;
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
            if (asking || reopening) return true;
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
