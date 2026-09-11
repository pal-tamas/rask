// The define module, as Spectrum lays it out: importing it registers the tag, and it exports nothing.

import type {FxSwitch} from "./components/switch/switch.js";

declare global {
    interface HTMLElementTagNameMap {
        "fx-switch": FxSwitch;
    }
}

export {};
