// A React package component shaped like the ones the extractor meets in the wild: its props extend the DOM's,
// redeclare some of them, and use every kind the snapshot schema knows plus the ones it has to skip.

import type { ButtonHTMLAttributes, MouseEvent, ReactNode, Ref } from "react";

/** A colour to show beside the label. */
export interface Swatch {
    /** The hex value, with its leading '#'. */
    hex: string;
    alpha?: number;
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
    /**
     * The variant to use.
     * @default 'text'
     */
    variant?: "text" | "outlined" | "contained";
    /** The size of the component; any other string names a size the theme adds. */
    size?: "small" | "medium" | "large" | (string & {});
    disabled?: boolean;
    elevation?: number;
    label: string;
    children?: ReactNode;
    onClick?: (event: MouseEvent<HTMLButtonElement>) => void;
    onChange?: (event: Event, value: number) => void;
    onClose?: () => void;
    /** MUI's shape: a boolean or a literal — one enum of three values the package accepts. */
    scrollButtons?: "auto" | true | false;
    density?: boolean | "sm" | "md";
    /** A boolean or any string: a union, not an enum of "false" and "true". */
    wrap?: string | boolean;
    startedAt?: Date;
    tags?: string[];
    swatch?: Swatch;
    ref?: Ref<HTMLButtonElement>;
    container?: HTMLElement | null;
    primary?: Option<string>;
    secondary?: Option<number>;
    sx?: any;
    count?: bigint;
    $internal?: string;
}

declare function Button(props: ButtonProps): ReactNode;
export default Button;

export declare function Badge(props: { tone?: "info" | "warn"; max?: 9 | 99 }): ReactNode;

/** A choice with a typed value; two instantiations must not collapse into one named type. */
export interface Option<T> {
    value: T;
    label: string;
}

/** Controlled or uncontrolled, never both — so neither half's props are required. */
export declare function Switch(
    props: { checked: boolean; onToggle?: (checked: boolean) => void } | { defaultChecked: boolean },
): ReactNode;
