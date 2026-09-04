// DOM changes — MutationObserver.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// For reacting to DOM somebody ELSE wrote: a third-party widget, a browser extension, a chart library
// that rebuilds its own subtree. Watching your own framework's output with this is a sign the state
// belongs somewhere you can read directly.

export interface MutationChange {
    /** "childList", "attributes" or "characterData". */
    type: string;
    addedCount: number;
    removedCount: number;
    /** Set only for an attribute mutation. */
    attributeName: string | null;
}

export interface MutationOptions {
    childList?: boolean;
    attributes?: boolean;
    characterData?: boolean;
    subtree?: boolean;
    /** Watch only these attributes. Implies `attributes`. */
    attributeFilter?: string[] | null;
}

/** Observe an element. Returns the stop function. */
export function observe(
    element: Node,
    onChange: (change: MutationChange) => void,
    options?: MutationOptions): () => void {
    const init: MutationObserverInit = {
        childList: !!(options && options.childList),
        attributes: !!(options && options.attributes),
        characterData: !!(options && options.characterData),
        subtree: !!(options && options.subtree)
    };

    if (options && options.attributeFilter && options.attributeFilter.length) {
        // An attributeFilter without attributes:true makes observe() throw. Honouring the implication
        // is friendlier than making every caller remember to set both.
        init.attributeFilter = options.attributeFilter;
        init.attributes = true;
    }

    const observer = new MutationObserver((records) => {
        for (let i = 0; i < records.length; i++) {
            const r = records[i];
            onChange({
                type: r.type,
                addedCount: r.addedNodes ? r.addedNodes.length : 0,
                removedCount: r.removedNodes ? r.removedNodes.length : 0,
                attributeName: r.attributeName
            });
        }
    });
    observer.observe(element, init);

    let stopped = false;
    return () => {
        if (stopped) {
            return;
        }
        stopped = true;
        observer.disconnect();
    };
}
