// A tab out of sight long enough is asked to hibernate (SessionHibernation.cs): the server lets its session go, and
// the page is rebuilt from the tab's resume record when the tab is looked at again — the address and what the app
// declared, as after a restart. What the page holds that the record does not carry would be lost, so the tab says
// yes only when it holds none of it:
//
//   - shown:   the tab is in sight again — the reader is back, and the session is the one they are using;
//   - input:   something the reader typed or chose that the page has not saved — a guarded form's edits
//              (rask-leave.ts), a field still waiting to be sent (rask-input.ts), a field edited away from what
//              the page drew before the edit (outside a guarded form, which knows better whether it was saved);
//   - media:   a video or a sound playing;
//   - interop: a call into the browser under way, or a call into .NET waiting on its answer;
//   - busy:    the page is settling (rask-busy.ts), or holds frames for the connection or not yet answered.
//
// The server asks again later, so "not now" costs nothing. A tab the browser has frozen cannot answer, and keeps
// its session.

import {raskDirtyFieldBase, raskIsDirtyField, raskRadioGroup} from "../../Rask.Core/Resources/rask-morph.js";

/** What the runtime knows about the tab when it is asked. */
export interface HibernationFacts {
    /** The tab is out of sight. */
    hidden: boolean;
    /** Something the reader typed, chose or edited the page has not saved. */
    unsaved: boolean;
    /** A video or a sound is playing. */
    media: boolean;
    /** Interop calls under way, either way. */
    calls: number;
    /** The page is settling (`data-rask-busy`). */
    busy: boolean;
    /** Frames waiting for the connection, and frames sent that no ack has answered. */
    waiting: number;
}

/** Why the tab will not hibernate now, as the server's metric names it — or null when it may. */
export function hibernationRefusal(facts: HibernationFacts): string | null {
    if (!facts.hidden) return "shown";
    if (facts.unsaved) return "input";
    if (facts.media) return "media";
    if (facts.calls > 0) return "interop";
    if (facts.busy || facts.waiting > 0) return "busy";
    return null;
}

/** Whether a video or a sound on the page is playing. */
export function mediaPlaying(within: ParentNode): boolean {
    const media = within.querySelectorAll<HTMLMediaElement>("audio, video");
    for (let i = 0; i < media.length; i++) {
        if (!media[i].paused && !media[i].ended) return true;
    }
    return false;
}

/**
 * Whether a field outside a guarded form holds something other than what the page drew before the reader first
 * edited it: the page holds an edit in its session alone, and rebuilt, would draw the field as it was.
 */
export function holdsEdits(within: ParentNode): boolean {
    const fields = within.querySelectorAll<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>(
        "input, textarea, select");
    for (let i = 0; i < fields.length; i++) {
        const field = fields[i];
        if (!raskIsDirtyField(field) || field.closest("form[data-rask-confirm-leave]")) continue;
        if (edited(field)) return true;
    }
    return false;
}

function edited(field: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement): boolean {
    // A select's base is not what it shows: chosen in at all, it is taken as edited.
    if (field.tagName === "SELECT") return true;
    const base = raskDirtyFieldBase(field);
    const type = (field.getAttribute("type") || "").toLowerCase();
    if (type === "checkbox") return base !== (field as HTMLInputElement).checked;
    if (type === "radio") return base !== checkedIn(field as HTMLInputElement);
    return (base ?? "") !== field.value;
}

function checkedIn(radio: HTMLInputElement): string {
    for (const r of raskRadioGroup(radio)) {
        if (r.checked) return r.value;
    }
    return "";
}
