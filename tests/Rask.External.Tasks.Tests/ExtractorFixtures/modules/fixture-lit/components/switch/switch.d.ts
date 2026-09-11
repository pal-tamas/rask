// The class module, as Spectrum lays it out: it declares the element and registers nothing.

import {LitElement} from "lit";

export declare class FxSwitch extends LitElement {
    /** Whether the switch is on. */
    checked: boolean;
    size: "small" | "medium" | "large";
    /** Help shown under the switch. */
    helpText: string;
    /** A public field that is not reactive: the manifest gives it no attribute. */
    input: HTMLInputElement;
    readonly form: string;
    private _pressed;
    protected handleClick(): void;
    focus(options?: FocusOptions): void;
    get validity(): ValidityState;
    static styles: unknown;
}
