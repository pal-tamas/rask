// A cut-down Svelte: Svelte 5's Component and Snippet, and the Svelte 4 class packages still ship typings for.

declare const brand: unique symbol;

export interface ComponentInternals {
    [brand]: "ComponentInternals";
}

/** A Svelte 5 component. `this` is not a parameter, so the props are the SECOND one, after the internals. */
export interface Component<Props extends Record<string, any> = {}, Exports extends Record<string, any> = {}, Bindings extends string = string> {
    (this: void, internals: ComponentInternals, props: Props): Exports;
    z_$$bindings?: Bindings;
}

declare const snippet: unique symbol;

/** Markup passed as a prop, rendered with {@render}. */
export interface Snippet<Parameters extends unknown[] = []> {
    (this: void, ...args: Parameters): {[snippet]: true};
}

/** A Svelte 4 component, typed as a class whose instance carries its props, events and slots. */
export declare class SvelteComponent<
    Props extends Record<string, any> = any,
    Events extends Record<string, any> = any,
    Slots extends Record<string, any> = any,
> {
    constructor(options: {target: Element; props?: Props});
    $$prop_def: Props;
    $$events_def: Events;
    $$slot_def: Slots;
    [prop: string]: any;
}
