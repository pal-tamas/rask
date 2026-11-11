// A clock a fixture turns by hand: `setTimeout`/`clearTimeout` replaced on the global, and time moved only by
// `advance`. The host's own virtual clock jumps to the next timer as soon as the fixture's task ends, which is
// right for "wait for it" and wrong for "149 ms later, it has not happened yet".

let now = 0;
let lastId = 0;
const due = new Map<number, {at: number; run: () => void}>();

/** Replaces the page's timers with this clock's. */
export function installClock(): void {
    const page = globalThis as unknown as {setTimeout: unknown; clearTimeout: unknown};
    page.setTimeout = (run: () => void, delay: number) => {
        due.set(++lastId, {at: now + (delay || 0), run});
        return lastId;
    };
    page.clearTimeout = (id: number) => { due.delete(id); };
}

/** Moves the clock on by `ms`, running each timer that falls due, in the order they are due. */
export function advance(ms: number): void {
    const until = now + ms;
    for (;;) {
        let next: [number, {at: number; run: () => void}] | null = null;
        for (const entry of due) {
            if (entry[1].at <= until && (!next || entry[1].at < next[1].at)) next = entry;
        }
        if (!next) break;
        due.delete(next[0]);
        now = next[1].at;
        next[1].run();
    }
    now = until;
}
