// Scoped TypeScript for ElementRefDemo. The C# side passes an ElementRef; the runtime reviver
// resolves it to the live DOM element before this runs, so `el` is the actual node.
//
// The parameter is nullable on purpose rather than for tidiness: the reviver hands over null when
// the element has already been removed — a ref read during teardown, or after a navigation — and
// before this was typed, that arrived as a TypeError from getBoundingClientRect on null.
export function width(el: HTMLElement | null): number {
    return el ? el.getBoundingClientRect().width : 0;
}
