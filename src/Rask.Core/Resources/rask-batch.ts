// The events one task produces leave as ONE frame, on both client runtimes.
//
// Sixty charts measured by one ResizeObserver callback announce sixty sizes in one task. Sent one by one, each
// was a handler, a walk of the page, a diff and a patch — sixty renders of a page that needed one. Held until
// the task ends they are one frame, which the host answers with its handlers in order and a single render.
//
// WHEN. At the end of the task that produced them (a microtask), not at the next animation frame: a click still
// leaves in the task it happened in, and no patch can land between an event being read off the page and its
// being sent — a patch arrives as a task of its own — so an id is never sent after the patch that retired it.
//
// ORDER. Only handler events wait here. Anything else a host sends (a navigation, an interop reply) goes at
// once, AFTER the host has flushed what waits: nothing overtakes an event that happened before it.

// WHICH PAGE. A handler id is a slot in its component, and the handler in a slot can change from one page to the
// next. So every event says which page it was read from: the number the last applied page carried (`v`; a page
// whose handlers did not move carries none, and the document itself is zero). The host uses it to find the handler
// the event was sent to, wherever that handler is by the time the event arrives — or to run nothing.
let pageVersion = 0;

/** Notes the page a reply carries, at the moment it is put on screen — not when it arrives. */
export function pageApplied(reply: { v?: unknown }): void {
    if (typeof reply.v === "number") {
        pageVersion = reply.v;
    }
}

/** The most events one frame carries; the host refuses a longer one, so the rest go in a frame of their own. */
export const MAX_BATCH = 256;

/** An event for a handler: it names one by `id`, and is not the reply to an interop call (which has an id too). */
export function isHandlerEvent(payload: unknown): boolean {
    const event = payload as { id?: unknown; type?: unknown } | null;
    return !!event && typeof event === "object" && event.id != null && event.type !== "jsResult";
}

/** What `events` travel as: the event itself when it is alone — the frame it always was — or one batch. */
export function batchFrame(events: unknown[]): unknown {
    return events.length === 1 ? events[0] : {type: "batch", events: events};
}

export interface EventBatch {
    /** Holds `event` until the task ends. The promise is the host's answer to the frame it leaves in. */
    add(event: unknown): Promise<void>;

    /** Sends what is held now, ahead of something that must not overtake it. */
    flush(): void;
}

/** Collects handler events and hands them to `ship` as one frame. `ship` may answer with a promise. */
export function eventBatch(ship: (frame: unknown) => unknown): EventBatch {
    let held: unknown[] = [];
    let answer: Promise<void> | null = null;
    let answered: () => void = function () {};

    function flush(): void {
        if (held.length === 0) {
            return;
        }
        const events = held;
        const done = answered;
        held = [];
        answer = null;
        Promise.resolve(ship(batchFrame(events))).then(done, done);
    }

    return {
        add(event: unknown): Promise<void> {
            if (!answer) {
                answer = new Promise<void>(function (resolve) { answered = resolve; });
                queueMicrotask(flush);
            }
            const mine = answer;
            (event as { v?: number }).v = pageVersion;
            held.push(event);
            if (held.length >= MAX_BATCH) {
                flush();
            }
            return mine;
        },
        flush,
    };
}
