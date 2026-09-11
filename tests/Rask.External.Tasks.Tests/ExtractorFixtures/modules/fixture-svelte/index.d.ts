// Svelte package components in the shapes their declarations take in the wild.

import type {Component, Snippet, SvelteComponent} from "svelte";
import type {FullAutoFill, HTMLButtonAttributes} from "svelte/elements";

export type SwitchRootProps = Omit<HTMLButtonAttributes, "type"> & {
    /** Whether the switch is on. */
    checked?: boolean;
    onCheckedChange?: (checked: boolean) => void;
    /** Redeclared here, so it is this package's prop even though the DOM declares one too. */
    id?: string;
    /** Declared here with the DOM's own alias as its type. */
    autocomplete?: FullAutoFill;
    /** The same alias on a required prop, which must serialize exactly as the optional one does. */
    fill: FullAutoFill;
    children?: Snippet;
    child?: Snippet<[{props: Record<string, unknown>}]>;
};

/** A namespace of parts, as bits-ui exports them: the component is `Switch.Root`. */
export declare const Switch: {Root: Component<SwitchRootProps, {}, "checked">};

/** A Svelte 5 component as the default export. */
declare const Toaster: Component<{position?: "top-left" | "bottom-right"; expand?: boolean}>;
export default Toaster;

/** What svelte-package writes for a Svelte 4 component: a class AND a function, events and slots inside the props. */
interface $$__sveltets_2_IsomorphicComponent<
    Props extends Record<string, any> = any,
    Events extends Record<string, any> = any,
    Slots extends Record<string, any> = any,
    Exports = {},
    Bindings = string,
> {
    new (options: {target: Element; props?: Props}): SvelteComponent<Props, Events, Slots> & {$$bindings?: Bindings} & Exports;
    (internal: unknown, props: Props & {$$events?: Events; $$slots?: Slots}): Exports & {$set?: any; $on?: any};
    z_$$bindings?: Bindings;
}

export declare const Dropdown: $$__sveltets_2_IsomorphicComponent<
    {open?: boolean; label: string},
    {select: CustomEvent<string>},
    {default: {}},
    {},
    ""
>;

/** Svelte 4 typings: props on the instance, and events only as `on:` directives. */
export declare class LegacySelect extends SvelteComponent<
    {items?: string[]; value?: string},
    {change: CustomEvent<string>; clear: CustomEvent<void>},
    {}
> {}
