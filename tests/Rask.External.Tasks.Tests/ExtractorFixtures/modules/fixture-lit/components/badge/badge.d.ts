// Shoelace's layout: one module exports the class as its default and registers the tag.

import {LitElement} from "lit";

export default class FxBadge extends LitElement {
    variant: "primary" | "neutral" | "danger";
    pill: boolean;
}

declare global {
    interface HTMLElementTagNameMap {
        "fx-badge": FxBadge;
    }
}
