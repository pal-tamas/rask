// Scoped JS for the Gantt component — the browser half of wrapping a third-party DOM library.
//   * mount(host, id, pathBase, optionsJson) — load frappe-gantt, draw the chart into the host, and
//     forward its callbacks to the [JSInvokable]s in GanttInterop.
//   * update(host, optionsJson) — push new tasks / a new view mode at the live instance.
//   * destroy(host) — drop the instance and clear the host.
//
// frappe-gantt is vendored under wwwroot/lib/frappe-gantt (MIT). It's an ordinary UMD bundle: loading it
// sets a `Gantt` global. Vendored rather than CDN-loaded so the showcase works offline, under the GitHub
// Pages sub-path, and inside the Native hosts.

const ASSEMBLY = "Rask.Example.Shared";

// One instance per host element. A WeakMap, so a host that goes away with the page doesn't pin its chart.
const charts = new WeakMap();
let loadPromise = null;

// Resolve the library's URL from the PathBase the .NET side passes in, rather than from document.baseURI:
// PathBase is what's correct behind a reverse proxy (Server), under /rask/ (WASM on Pages), and on Native,
// where the WebView interceptor serves _content/ out of the app bundle.
function libUrl(pathBase, file) {
    return `${pathBase || ""}/_content/Rask.Example.Shared/lib/frappe-gantt/${file}`;
}

function loadGantt(pathBase) {
    if (globalThis.Gantt) return Promise.resolve(globalThis.Gantt);
    if (loadPromise) return loadPromise;

    loadPromise = new Promise((resolve, reject) => {
        // The stylesheet goes into <head> at runtime, so it isn't part of any .NET render. Rask notices
        // foreign head nodes and preserves them across re-renders on its own — that's the behaviour this
        // very demo sits under in docs/js-interop.md, and why there's no marking code here.
        const css = document.createElement("link");
        css.rel = "stylesheet";
        css.href = libUrl(pathBase, "frappe-gantt.css");
        document.head.appendChild(css);

        const script = document.createElement("script");
        script.src = libUrl(pathBase, "frappe-gantt.umd.js");
        script.onload = () => resolve(globalThis.Gantt);
        script.onerror = () => {
            // Drop the failed attempt and take the half-loaded tags with it. Memoizing a rejected promise
            // would turn one flaky fetch into a permanently broken chart for the rest of the page's life,
            // with no way back short of a reload.
            loadPromise = null;
            css.remove();
            script.remove();
            reject(new Error("frappe-gantt failed to load"));
        };
        document.head.appendChild(script);
    });
    return loadPromise;
}

// The chart's DOM is created by the library, so it is absent from every .NET render payload. Not every
// frame is a diff — the first interactive frame after page load ships full HTML, and the client applies
// a full frame by morphing the document. The morph pairs the host's live children against the rendered
// zero and deletes the chart. Remove this and the chart vanishes a moment after it first draws.
// The marker is the opt-out: the reconciler skips marked from-side nodes, so the host pairs as empty.
// Mark the library's children, never the host itself — the host IS in the render tree, and marking it
// would make the morph treat it as missing and append a duplicate.
function markManaged(host) {
    for (const child of host.children) child.setAttribute("data-rask-managed", "");
}

function toLibTasks(options) {
    return options.tasks.map((t) => ({
        id: t.id,
        name: t.name,
        start: t.start,
        end: t.end,
        progress: t.progress
    }));
}

// frappe-gantt keys holidays by the CSS colour to paint them with; weekends are its default entry, so
// keep that and add ours alongside rather than replacing it.
function toLibHolidays(options) {
    const holidays = { "var(--g-weekend-highlight-color)": "weekend" };
    if (options.holidays.length > 0) {
        holidays["#ffd7d7"] = options.holidays.map((h) => ({ date: h.date, label: h.label }));
    }
    return holidays;
}

export async function mount(host, id, pathBase, optionsJson) {
    if (!host) return;
    // A host that already carries a chart means a *new* component instance landed on the same element
    // (the .NET side rebuilt it — e.g. its position among its parent's children shifted). Rebuild rather
    // than bailing out: returning early would leave the previous instance's chart on screen, wired to a
    // component that no longer renders, so it would ignore every later prop change.
    if (charts.has(host)) destroy(host);

    const options = JSON.parse(optionsJson);
    let Lib;
    try {
        Lib = await loadGantt(pathBase);
    } catch {
        // Say so rather than leaving an empty box. It goes in as a marked *element*, not a bare text
        // node: the .NET side renders this host empty, so anything unmarked in here is something the next
        // full-frame morph deletes — and markManaged only reaches elements.
        const note = document.createElement("p");
        note.className = "text-danger small mb-0";
        note.textContent = "The Gantt library could not be loaded.";
        host.replaceChildren(note);
        markManaged(host);
        return;
    }

    // Re-entrancy: mount is awaited, so a second call could have landed while the script was loading.
    if (charts.has(host)) return;

    const chart = new Lib(host, toLibTasks(options), {
        view_mode: options.viewMode,
        holidays: toLibHolidays(options),
        infinite_padding: false,
        popup_on: "click",
        on_click: (task) => {
            window.DotNet.invokeMethodAsync(ASSEMBLY, "RaskGanttTaskClicked", id, String(task.id));
        },
        on_date_change: (task, start, end) => {
            window.DotNet.invokeMethodAsync(
                ASSEMBLY, "RaskGanttDateChanged", id, String(task.id), localIso(start), localIso(end));
        },
        on_progress_change: (task, progress) => {
            window.DotNet.invokeMethodAsync(
                ASSEMBLY, "RaskGanttProgressChanged", id, String(task.id), Number(progress));
        }
    });

    // Remember what the chart is currently showing, so update() can apply only real changes.
    charts.set(host, { chart, viewMode: options.viewMode, tasksJson: JSON.stringify(toLibTasks(options)) });
    markManaged(host);
}

export function update(host, optionsJson) {
    const entry = host && charts.get(host);
    if (!entry) return;

    const options = JSON.parse(optionsJson);
    const tasks = toLibTasks(options);
    const tasksJson = JSON.stringify(tasks);

    // Apply only what actually changed. The library's refresh() already re-applies the current view mode
    // internally, so calling both unconditionally rebuilt the entire SVG twice per update — and did it on
    // every prop change, including ones that touched neither.
    if (entry.viewMode !== options.viewMode) {
        // maintain_pos: keep the user where they were scrolled to while the axis rescales.
        entry.chart.change_view_mode(options.viewMode, true);
        entry.viewMode = options.viewMode;
    }

    if (entry.tasksJson !== tasksJson) {
        entry.chart.refresh(tasks);
        entry.tasksJson = tasksJson;
    }

    // Rebuilt nodes are new nodes, so they need the marker again.
    markManaged(host);
}

export function destroy(host) {
    if (!host || !charts.has(host)) return;
    // The library has no destroy() of its own, so drop our reference and clear what it drew. The host
    // itself belongs to the .NET render tree — leave it in place.
    charts.delete(host);
    host.replaceChildren();
}

// The library hands back Date objects holding a *wall-clock* day — the bar sits on the column the user
// sees. toISOString() would convert that to UTC and shift the calendar day for every user who isn't on
// UTC (a bar dropped on the 8th coming back as the 9th, or the 7th, depending on which side you're on),
// so format the local fields instead and let the .NET side read them as an unzoned timestamp.
function localIso(date) {
    if (!(date instanceof Date)) return String(date);
    const pad = (n) => String(n).padStart(2, "0");
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
        + `T${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}
