// A cut-down @types/react: just enough of the framework's own types for the extractor to tell a package's props
// from the DOM attributes and events every React component inherits.

export type ReactNode = ReactElement | string | number | bigint | boolean | null | undefined | Iterable<ReactNode>;

export interface ReactElement {
    type: unknown;
    props: unknown;
    key: string | null;
}

export interface SyntheticEvent<T = Element> {
    currentTarget: T;
}

export interface MouseEvent<T = Element> extends SyntheticEvent<T> {
    button: number;
}

export interface RefObject<T> {
    current: T | null;
}

export type Ref<T> = RefObject<T> | ((instance: T | null) => void) | null;

export interface DOMAttributes<T> {
    children?: ReactNode;
    onClick?: (event: MouseEvent<T>) => void;
}

export interface HTMLAttributes<T> extends DOMAttributes<T> {
    className?: string;
    id?: string;
}

export interface ButtonHTMLAttributes<T> extends HTMLAttributes<T> {
    type?: "button" | "submit" | "reset";
}
