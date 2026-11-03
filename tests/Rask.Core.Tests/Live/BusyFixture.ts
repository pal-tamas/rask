// Node-driven fixture for rask-busy.ts — `data-rask-busy` on <html> while the page is settling — and for the one
// source every host shares, rask-owned.ts's `awaiting` (an event or a navigation not answered yet).
//
// A <html> that keeps its attributes. The C# test (BusyMarkClientTests) runs this in a node subprocess and asserts
// the JSON line. "Now" is read in the same task as the step before it; "later" after the microtasks have run, which
// is the earliest anything outside this task — a test's poll — could look.

const attributes = new Set<string>();
const root = {
    hasAttribute(name: string) { return attributes.has(name); },
    setAttribute(name: string) { attributes.add(name); },
    removeAttribute(name: string) { attributes.delete(name); },
};
(globalThis as unknown as Record<string, unknown>).document = {documentElement: root};

const {busy, busyUntil, busyWhile, busyOwnsAttr} = await import("../../../src/Rask.Core/Resources/rask-busy.js");
const {awaiting} = await import("../../../src/Rask.Core/Resources/rask-owned.js");
const marked = () => attributes.has("data-rask-busy");
const later = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

const idleAtFirst = marked();

const one = busy();
const markedInTheSameTask = marked();
one();
const stillMarkedInTheTaskThatLetGo = marked();
await later();
const clearedOnceNothingHolds = marked();

const first = busy();
const second = busy();
first();
await later();
const markedWhileTheSecondHolds = marked();
second();
await later();
const clearedWhenBothLetGo = marked();

// An ack handing over to the render it answers: one lets go, the next takes hold, in the same task.
const ack = busy();
ack();
const render = busy();
await later();
const neverIdleAcrossAHandOver = marked();
render();
await later();

const twice = busy();
twice();
twice();
const other = busy();
await later();
const aSecondReleaseLetsNothingElseGo = marked();
other();
await later();

let resolveFrame: () => void = () => {};
const frame = new Promise<void>((resolve) => { resolveFrame = resolve; });
busyUntil(frame, busy());
await later();
const markedWhileTheFrameIsPending = marked();
resolveFrame();
await later();
const clearedWhenTheFrameApplied = marked();

const typing = busyWhile();
typing(true);
typing(true);
const markedWhileTyping = marked();
typing(false);
await later();
const clearedWhenTypingStops = marked();

const answered = awaiting();
const markedWhileAwaitingAnAnswer = marked();
answered();
await later();
const clearedWhenAnswered = marked();

const ownsItOnHtml = busyOwnsAttr(root as unknown as Element, "data-rask-busy");
const ownsNothingElse = busyOwnsAttr({} as Element, "data-rask-busy") || busyOwnsAttr(root as unknown as Element, "class");

process.stdout.write(JSON.stringify({
    idleAtFirst,
    markedInTheSameTask,
    stillMarkedInTheTaskThatLetGo,
    clearedOnceNothingHolds,
    markedWhileTheSecondHolds,
    clearedWhenBothLetGo,
    neverIdleAcrossAHandOver,
    aSecondReleaseLetsNothingElseGo,
    markedWhileTheFrameIsPending,
    clearedWhenTheFrameApplied,
    markedWhileTyping,
    clearedWhenTypingStops,
    markedWhileAwaitingAnAnswer,
    clearedWhenAnswered,
    ownsItOnHtml,
    ownsNothingElse,
}) + "\n");
