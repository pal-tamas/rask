// A cut-down lit: the base classes every element inherits members from, which the extractor must leave out.

export declare class ReactiveElement extends HTMLElement {
    static get observedAttributes(): string[];
    renderRoot: HTMLElement | DocumentFragment;
    isUpdatePending: boolean;
    hasUpdated: boolean;
    requestUpdate(name?: PropertyKey): void;
    protected updated(changed: Map<PropertyKey, unknown>): void;
}

export declare class LitElement extends ReactiveElement {
    protected render(): unknown;
}
