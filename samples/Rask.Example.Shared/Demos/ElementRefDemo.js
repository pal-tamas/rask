// Scoped JS for ElementRefDemo. The C# side passes an ElementRef; the runtime reviver resolves
// it to the live DOM element before this runs, so `el` is the actual node.
export function width(el) {
    return el ? el.getBoundingClientRect().width : 0;
}
