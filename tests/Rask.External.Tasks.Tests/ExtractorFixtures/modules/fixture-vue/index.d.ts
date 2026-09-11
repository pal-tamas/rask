// Vue package components in the three shapes their declarations take in the wild.

import type {DefineComponent, FunctionalComponent, HTMLAttributes, Slot, VNode} from "vue";

export interface ToggleProps {
    /** Whether the toggle is on. */
    modelValue?: boolean;
    disabled?: boolean;
    size?: "sm" | "lg";
    "onUpdate:modelValue"?: (value: boolean) => any;
    "onValue-change"?: (...args: [value: number, source: string]) => any;
}

/** vue-tsc's output for a `<script setup>` component: emits are on* props already, and it has a default slot. */
declare const Toggle: DefineComponent<ToggleProps, {default?: Slot}>;
export default Toggle;

/** PrimeVue's shape: DOM attributes spread in, and emits only as `$emit` overloads. */
export interface ButtonEmits {
    (e: "click", event: MouseEvent): void;
    (e: "update:pressed", ...args: [pressed: boolean]): void;
}

export declare const PrimeButton: new () => {
    $props: HTMLAttributes & {
        label?: string;
        severity?: "info" | "warn" | (string & {});
        badge?: string | boolean;
    };
    $emit: ButtonEmits;
};

/** vue-tsc's output for a generic `<script setup lang="ts" generic="T">`: a call signature, T defaulted. */
export declare const Picker: <T = string>(
    props: {options: T[]; modelValue?: T; "onUpdate:modelValue"?: (value: T) => any},
    ctx?: {slots: {}},
) => VNode;

type __VLS_WithTemplateSlots<T, S> = T & {new (): {$slots: S}};

/** vue-tsc's older output: the component intersected with a second construct signature that carries only `$slots`. */
export declare const Card: __VLS_WithTemplateSlots<DefineComponent<{title: string}>, {default?: Slot; header?: Slot}>;

/** Real Vue's EmitFn for array emits — one overload over a union of names, arguments untyped — beside an object one. */
export declare const Tabs: new () => {
    $props: {active?: string};
    $emit: ((event: "open" | "update:active", ...args: any[]) => void) & ((event: "close") => void);
};

/** A functional component whose only event is declared as its emit type. */
export declare const Chip: FunctionalComponent<{label: string}, {remove: (id: number) => void}>;
