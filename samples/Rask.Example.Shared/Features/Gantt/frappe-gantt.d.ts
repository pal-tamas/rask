// frappe-gantt (MIT), vendored under wwwroot/lib/frappe-gantt as a UMD bundle that sets a `Gantt`
// global. This describes the slice of it Gantt.ts uses, and nothing else.
//
// Hand-written rather than taken from the package's own typings, because there is no node_modules
// to take them from — and a narrow declaration that matches how this demo drives the library is
// worth more here than a complete one nobody reads. The library's own option names are snake_case;
// they are reproduced verbatim, because renaming them in the declaration would describe an API that
// does not exist.

/** A bar on the chart, in the shape the library expects. */
interface GanttTask {
    id: string;
    name: string;
    start: string;
    end: string;
    progress: number;
}

/** One shaded day. The library's default entry is the string "weekend". */
interface GanttHoliday {
    date: string;
    label: string;
}

/**
 * Holidays are keyed by the CSS colour to paint them with, which is why this is an index signature
 * rather than a list: the key carries meaning.
 */
type GanttHolidays = Record<string, "weekend" | GanttHoliday[]>;

interface GanttOptions {
    view_mode: string;
    holidays: GanttHolidays;
    infinite_padding: boolean;
    popup_on: string;

    /** The library hands back `Date` objects holding a wall-clock day, not an instant. */
    on_click?: (task: GanttTask) => void;
    on_date_change?: (task: GanttTask, start: Date, end: Date) => void;
    on_progress_change?: (task: GanttTask, progress: number) => void;
}

declare class GanttChart {
    constructor(host: HTMLElement, tasks: GanttTask[], options: GanttOptions);

    /** Re-draws with a new task set. */
    refresh(tasks: GanttTask[]): void;

    /** @param maintainPos keeps the user's scroll position while the axis rescales. */
    change_view_mode(mode: string, maintainPos?: boolean): void;
}

/**
 * The global the UMD bundle installs, once its <script> has run.
 *
 * `var` rather than only a `Window` member, because the loader tests `globalThis.Gantt` to decide
 * whether the library is already there — and a property that TypeScript does not know about on
 * globalThis is an error, however plainly it exists at runtime. Optional for the same reason: before
 * the script loads there is genuinely nothing there, and pretending otherwise would let a caller
 * skip the load and construct undefined.
 */
declare var Gantt: typeof GanttChart | undefined;
