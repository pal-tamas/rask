// Angular library components as ng-packagr writes their declarations: inputs and outputs live only in the type
// arguments of `static ɵcmp`.

import * as i0 from "@angular/core";
import type {EventEmitter, InputSignal, InputSignalWithTransform, ModelSignal, OutputEmitterRef} from "@angular/core";

/** What the change output carries: its `source` is the live component, which cannot cross the wire. */
export declare class ToggleChange {
    source: FxToggle;
    checked: boolean;
}

/** A decorator-based component. */
export declare class FxToggle {
    /** Whether the toggle is on. */
    checked: boolean;
    ariaLabel: string | null;
    disabled: boolean;
    readonly change: EventEmitter<ToggleChange>;
    readonly toggleChange: EventEmitter<void>;
    toggle(): void;
    static ngAcceptInputType_checked: unknown;
    static ngAcceptInputType_disabled: unknown;
    static ɵcmp: i0.ɵɵComponentDeclaration<
        FxToggle,
        "fx-toggle",
        ["fxToggle"],
        {
            "checked": {"alias": "checked"; "required": false};
            "ariaLabel": {"alias": "aria-label"; "required": false};
            "disabled": {"alias": "disabled"; "required": false};
        },
        {"change": "change"; "toggleChange": "toggleChange"},
        never,
        never,
        true,
        never
    >;
}

/** A signal-based component: a model, a transformed input, a required aliased input and an output ref. */
export declare class FxSlider {
    readonly value: ModelSignal<number>;
    readonly min: InputSignal<number>;
    readonly step: InputSignalWithTransform<number, string | number>;
    readonly label: InputSignal<string>;
    readonly dragEnd: OutputEmitterRef<number>;
    static ɵcmp: i0.ɵɵComponentDeclaration<
        FxSlider,
        "fx-slider",
        never,
        {
            "value": {"alias": "value"; "required": false; "isSignal": true};
            "min": {"alias": "min"; "required": false; "isSignal": true};
            "step": {"alias": "step"; "required": false; "isSignal": true};
            "label": {"alias": "for"; "required": true; "isSignal": true};
        },
        {"value": "valueChange"; "dragEnd": "dragEnd"},
        never,
        never,
        true,
        never
    >;
}

/** A directive, which is not a component. */
export declare class FxTooltip {
    static ɵdir: i0.ɵɵDirectiveDeclaration<FxTooltip, "[fxTooltip]", never, {}, {}, never, never, true, never>;
}

/** A component from a library that predates standalone components. */
export declare class FxLegacy {
    static ɵcmp: i0.ɵɵComponentDeclaration<FxLegacy, "fx-legacy", never, {}, {}, never, never, false, never>;
}
