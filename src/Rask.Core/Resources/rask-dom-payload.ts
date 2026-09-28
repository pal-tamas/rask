// Reads a DOM event into the object its generated C# type (MouseEvent, KeyboardEvent, …) reads, by the table
// the build generates from MDN's data (generated/rask-dom-events.ts). Shared by every listener that sends an
// event — rask-events.ts's delegation, the hosts' click listeners, rask-input.ts's coalesced scroll — so each
// sends MDN's fields and none keeps a list of its own.

import { domDataFormats, domEvents, domInterfaces, domTargetMedia, domTargetScroll, type DomField } from "./generated/rask-dom-events.js";

/**
 * The object a generated C# event type reads: MDN's own field names (clientX, shiftKey, …), taken off the
 * event by the table the build generates from MDN's data (generated/rask-dom-events.ts), so the two halves
 * cannot drift. A loose record on purpose: the key set is the table's, not this file's.
 */
export type EventPayload = Record<string, unknown>;

type DomRow = (typeof domEvents)[number];
var raskDomRows = new Map<string, DomRow>(domEvents.map(function (row) { return [row[0], row] as [string, DomRow]; }));

/** The listed fields of `source`: plain values as they are, a list of snapshots, or a DataTransfer's data. */
function raskRead(source: unknown, fields: readonly DomField[]): EventPayload {
    var out: EventPayload = {};
    var o = source as Record<string, unknown>;
    for (var f of fields) {
        if (typeof f === "string") {
            var v = o[f];
            if (v === undefined || v === null) { continue; }
            if (typeof v === "number") { out[f] = isFinite(v) ? v : 0; }
            else if (typeof v === "string" || typeof v === "boolean") { out[f] = v; }
            else if (typeof (v as ArrayLike<unknown>).length === "number") { out[f] = Array.from(v as ArrayLike<unknown>); }
            continue;
        }
        var name = f[0], of = f[1], held = o[name];
        if (held === undefined || held === null) { continue; }
        out[name] = of === "DataTransfer"
            ? raskDataTransfer(held as DataTransfer)
            : Array.from(held as ArrayLike<unknown>, function (item) { return raskRead(item, domInterfaces[of] ?? []); });
    }
    return out;
}

/** A DataTransfer's plain fields, and its data in each format the table lists, read while the event can. */
function raskDataTransfer(dt: DataTransfer): EventPayload {
    var out = raskRead(dt, domInterfaces.DataTransfer ?? []);
    var data: Record<string, string> = {};
    for (var format of domDataFormats) {
        try { var value = dt.getData(format); if (value) { data[format] = value; } } catch { /* access blocked */ }
    }
    out.data = data;
    return out;
}

/** The state of an event's target that travels with it as e.Target: the scroll box (1) or playback (2). */
export function raskTargetState(el: EventTarget | null, kind: number): EventPayload | undefined {
    if (!el || kind === 0) { return undefined; }
    return raskRead(el, kind === 1 ? domTargetScroll : domTargetMedia);
}

/** The payload for event `type`, from its MDN interface's fields and, where the table says so, its target. */
export function raskDomPayload(e: Event, type: string): EventPayload {
    var row = raskDomRows.get(type);
    if (!row) { return {}; }
    var payload = raskRead(e, domInterfaces[row[1]] ?? []);
    var target = raskTargetState(e.target, row[4]);
    if (target) { payload.target = target; }
    return payload;
}

