// A cut-down svelte/elements: the DOM attributes a component spreads into its props, which the extractor must leave
// out, and an autofill alias large enough to turn into a useless enum if it were serialized member by member.

export type FullAutoFill = "on" | "off" | "name" | "email" | "username" | "new-password" | `section-${string}`;

export interface HTMLAttributes<T extends EventTarget = HTMLElement> {
    class?: string | null;
    id?: string | null;
    title?: string | null;
    autocomplete?: FullAutoFill | null;
    onclick?: ((event: MouseEvent & {currentTarget: T}) => any) | null;
}

export interface HTMLButtonAttributes extends HTMLAttributes<HTMLButtonElement> {
    disabled?: boolean | null;
    type?: "button" | "submit" | "reset" | null;
}
