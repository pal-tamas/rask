// Whether the page is still settling, said on <html> as `data-rask-busy` — what a browser test waits on
// (`await page.WaitForRask()`, Rask.Testing.Playwright), the way a Laravel Dusk test waits on `waitForLivewire()`.
//
// The page is BUSY while the runtime holds anything it has not finished, and IDLE when it holds nothing:
//
//   - an event the reader raised that is not answered yet: a handler event until the render answering it is on the
//     page (the Server's ack, then its render out of the queue; the browser host's dispatch and its reads), and a
//     navigation until its page is on screen (rask-owned.ts's `awaiting`, both hosts);
//   - a frame received and not applied yet (the render queue, including a body held for its stylesheet);
//   - what was typed and not sent yet: a live field's next animation frame, a debounce counting (rask-input.ts);
//   - a confirmed press waiting for the page to catch up before it is made again (rask-confirm.ts);
//   - the connection being opened, or opened again after a drop (Server), and the app booting (browser host);
//   - the behaviour hooks' bundle on its way (rask-hook-loader.ts).
//
// NOT covered, by design: the app's own timers, a fetch a script started, and a render the server pushes that has
// not reached the browser yet — a push that arrives later is a new busy period of its own. A deferred field waits
// for the next action by design, so a value typed into one is not busy either.
//
// Each source takes a HOLD and lets it go. The mark goes on synchronously with the first hold, in the same task as
// the event that took it, so a test that clicks and then waits can never look before the mark is there. It comes
// off in a microtask once nothing holds, so a hold handed from one source to the next (an ack to its render) never
// shows as idle in between: nothing outside this task can look before the microtask runs.
//
// The count lives on the global: the runtime and the hooks are separate bundles, and both may take holds. Nothing
// is sent anywhere, and nothing runs while the page is idle.

interface Busy {
    holds: number;
    clearing: boolean;
}

const scope = globalThis as typeof globalThis & {__raskBusy?: Busy};
const state: Busy = scope.__raskBusy || (scope.__raskBusy = {holds: 0, clearing: false});

/** The mark on <html> while the page is settling. */
export const BUSY_MARK = "data-rask-busy";

function html(): Element | null {
    return typeof document === "undefined" ? null : document.documentElement;
}

function clear(): void {
    state.clearing = false;
    const el = html();
    if (state.holds === 0 && el) el.removeAttribute(BUSY_MARK);
}

/** Holds the page busy until the returned function is called; calling it again does nothing. */
export function busy(): () => void {
    state.holds++;
    const el = html();
    if (el && !el.hasAttribute(BUSY_MARK)) el.setAttribute(BUSY_MARK, "");
    let held = true;
    return function (): void {
        if (!held) return;
        held = false;
        state.holds--;
        if (state.holds === 0 && !state.clearing) {
            state.clearing = true;
            queueMicrotask(clear);
        }
    };
}

/** Lets `release` go once `result` settles, or now when it is not a promise. */
export function busyUntil(result: unknown, release: () => void): void {
    const then = (result as PromiseLike<unknown> | null)?.then;
    if (typeof then === "function") then.call(result, release, release);
    else release();
}

/** A hold one source takes and drops as its state changes: `set(true)` holds, `set(false)` lets go. */
export function busyWhile(): (on: boolean) => void {
    let release: (() => void) | null = null;
    return function (on: boolean): void {
        if (on && !release) release = busy();
        else if (!on && release) {
            release();
            release = null;
        }
    };
}

/** Whether a morph must leave `name` on `el`: the mark is the runtime's, and no render writes it. */
export function busyOwnsAttr(el: Element, name: string): boolean {
    return name === BUSY_MARK && el === html();
}
