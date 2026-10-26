// What a datetime picker adds to the calendar it is built on (rask-calendar.ts, `time` in its JSON): the times of
// day — how one is written, which cannot be picked on a given day, where a list opens. Pure helpers; the calendar
// draws and listens.
//
// THE RULES, measured on Flux's live datetime picker (2026-10-22):
//   * `min-time` / `max-time` leave a time out of the list (the page does not draw it); `min` / `max` rule out the
//     times before or after them on their own day only; an unavailable time is drawn and cannot be picked — every
//     day, or only on the day an entry names; with no day chosen only the every-day entries count;
//   * a list opens with its row two rows down from its top: the chosen time where it is listed, else the last
//     time that can be picked not after `open-to`, else not after now — the first such when every one is later.

/** The `time` part of a datetime picker's calendar configuration. */
export interface TimeConfig {
    input: boolean;          // typed times (`time="input"`)
    pattern: string;         // the .NET pattern the times are written in: "h:mm tt", "HH:mm"…
    am: string; pm: string;
    join: string;            // between a moment's date and its time: a word, spaced (" at "), or the whole
                             // moment's pattern, {1} the date and {0} the time ("{1}, {0}") — CLDR's, by locale
    atClass: string;         // and the class it is drawn in
    wordsClass: string;      // the class of what the button says
    emptyClass: string;      // and of its placeholder
    placeholderClass: string;    // the class of a time field's placeholder
    monthDay: string;        // a range end's date in this year: "MMM d"
    comma: string;           // between a range end's date and its time
    placeholder: string;     // a time field's words while it holds no time
    min?: string; max?: string;   // `yyyy-mm-ddTHH:mm`
    unavailable?: string[];  // Flux's entries that name times: `12:00-13:00`, `2026-10-14T15:00-16:00`
    defaults?: number[];     // the times a picked day is given: a moment's, or a range's start and end
    openTo?: number;
}

interface Slot { day: string; from: number; to: number; }

const DAY_MS = 86400000;

/** Minutes since midnight of `HH:mm`, or NaN. */
export function minutesOf(text: string): number {
    const m = /^(\d{2}):(\d{2})$/.exec(text.trim());
    return m ? Number(m[1]) * 60 + Number(m[2]) : NaN;
}

/** `HH:mm`. */
export function hhmm(minutes: number): string {
    return two(Math.floor(minutes / 60)) + ":" + two(minutes % 60);
}

function two(n: number): string {
    return n < 10 ? "0" + n : String(n);
}

/** A time written with a .NET pattern's h, hh, H, HH, m, mm, t and tt; quotes and anything else as they stand. */
export function formatTime(minutes: number, pattern: string, am: string, pm: string): string {
    const hour = Math.floor(minutes / 60), minute = minutes % 60;
    let out = "";
    for (let i = 0; i < pattern.length;) {
        const ch = pattern[i];
        if (ch === "'" || ch === "\"") {
            const end = pattern.indexOf(ch, i + 1);
            out += pattern.slice(i + 1, end < 0 ? pattern.length : end);
            i = end < 0 ? pattern.length : end + 1;
            continue;
        }
        let n = 1;
        while (pattern[i + n] === ch) n++;
        const twelve = hour % 12 === 0 ? 12 : hour % 12;
        if (ch === "h") out += n > 1 ? two(twelve) : twelve;
        else if (ch === "H") out += n > 1 ? two(hour) : hour;
        else if (ch === "m") out += n > 1 ? two(minute) : minute;
        else if (ch === "t") out += n > 1 ? (hour < 12 ? am : pm) : (hour < 12 ? am : pm).charAt(0);
        else out += pattern.slice(i, i + n);
        i += n;
    }
    // ICU separates the period with a narrow no-break space; Flux's list has a plain one.
    return out.replace(/ /g, " ");
}

/** The pattern without its period: a one-day range's start, when both ends share it. */
export function unperiod(pattern: string): string {
    return pattern.replace(/t+/g, "").trim();
}

/** Whether the pattern tells the time with AM and PM. */
export function twelveHour(pattern: string): boolean {
    return pattern.indexOf("h") >= 0;
}

/** The day and minute of `yyyy-mm-ddTHH:mm`, or null. */
export function momentOf(text: string | undefined): {day: number; minute: number} | null {
    const m = text ? /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(text) : null;
    return m ? {day: Date.UTC(+m[1], +m[2] - 1, +m[3]) / DAY_MS, minute: Number(m[4]) * 60 + Number(m[5])} : null;
}

function slots(config: TimeConfig): Slot[] {
    const out: Slot[] = [];
    for (const entry of config.unavailable || []) {
        const at = entry.indexOf("T");
        const day = at >= 0 ? entry.slice(0, at) : "";
        const [from, to] = (at >= 0 ? entry.slice(at + 1) : entry).split("-");
        const a = minutesOf(from), b = to ? minutesOf(to) : a;
        if (!isNaN(a) && !isNaN(b)) out.push({day, from: a, to: b});
    }
    return out;
}

/**
 * Whether `minute` cannot be picked on `day` (whole days since 1970; null: no day chosen, so only every day's rules).
 * `iso` writes a day as the entries name one.
 */
export function timeOff(config: TimeConfig, day: number | null, minute: number, iso: (day: number) => string): boolean {
    if (day !== null) {
        const min = momentOf(config.min), max = momentOf(config.max);
        if (min && (day < min.day || (day === min.day && minute < min.minute))) return true;
        if (max && (day > max.day || (day === max.day && minute > max.minute))) return true;
    }
    const named = day === null ? "" : iso(day);
    return slots(config).some(function (s) {
        return (s.day === "" || s.day === named) && minute >= s.from && minute <= s.to;
    });
}

/** The minute a list opens on, among `listed`: see THE RULES. */
export function startMinute(listed: number[], chosen: number | null, openTo: number | undefined, now: number,
                            off: (minute: number) => boolean): number | null {
    if (chosen !== null && listed.indexOf(chosen) >= 0) return chosen;
    const open = listed.filter(function (m) { return !off(m); });
    const at = openTo !== undefined ? openTo : now;
    let start: number | null = null;
    for (const m of open) if (m <= at) start = m;
    return start !== null ? start : open.length ? open[0] : null;
}

/** The browser's time of day, in minutes. */
export function nowMinutes(): number {
    const d = new Date();
    return d.getHours() * 60 + d.getMinutes();
}
