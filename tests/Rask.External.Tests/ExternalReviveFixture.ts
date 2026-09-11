// Node-driven fixture for the props revival in the external-component client runtime
// (src/Rask.External/wwwroot/rask-external.js).
//
// A package island's callbacks carry `$a`, the argument positions that cross back to C#. A package's first
// argument is usually a DOM or synthetic event, which holds `view: window` — a cycle — so forwarding every
// argument makes the dispatch throw inside JSON.stringify and the call is lost without a trace. And a date
// crosses tagged as `{"$d": iso}`, so the runtime turns exactly that value into a Date rather than guessing at
// strings that merely look like timestamps.
//
// The C# test (ExternalReviveTests) runs this and asserts the JSON on stdout. Every value written below has to
// serialize, which is the point: had an event leaked into a dispatched payload, writing the result would throw.

globalThis.document = {body: {querySelectorAll: () => []}, createElement: () => ({})};
globalThis.MutationObserver = class {
    observe() {}
    disconnect() {}
};
globalThis.IntersectionObserver = undefined;

const dispatched = [];
globalThis.__raskHost = {send: (payload) => dispatched.push(JSON.parse(JSON.stringify(payload)))};

// Hold the runtime back: only its revival is under test.
globalThis.__raskExternalManual = true;

const runtime = await import("../../src/Rask.External/wwwroot/rask-external.js");
const {revive} = runtime.__internals;

const props = revive(JSON.parse(JSON.stringify({
    onPick: {$h: "pick", $a: [1]},
    onClose: {$h: "close", $a: []},
    onLegacy: {$h: "legacy"},
    when: {$d: "2026-09-11T08:00:00.000Z"},
    look: {$d: "not a date", other: 1},
    nested: [{$h: "deep", $a: [0]}],
})), new Map());

// An event-shaped argument with a cycle, as a real one has through `view`.
const event = {type: "change"};
event.view = event;

let failure = null;
try {
    props.onPick(event, 7);
    props.onClose(event);
    props.onLegacy(1, "two");
    props.nested[0]("only", "the first");
} catch (error) {
    failure = String(error);
}

process.stdout.write(JSON.stringify({
    failure,
    dispatched,
    whenIsDate: props.when instanceof Date,
    whenIso: props.when instanceof Date ? props.when.toISOString() : null,
    lookKept: typeof props.look === "object" && props.look.$d === "not a date" && props.look.other === 1,
}) + "\n");
