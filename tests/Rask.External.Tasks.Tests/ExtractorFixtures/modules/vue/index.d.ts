// A cut-down Vue: the component shapes package declarations take, and the framework's own attributes that every
// component inherits and the extractor must leave out.

export interface VNode {
    type: unknown;
}

export type Slot = (...args: any[]) => VNode[];

export interface VNodeProps {
    key?: string | number | symbol;
    ref?: unknown;
}

export interface AllowedComponentProps {
    class?: unknown;
    style?: unknown;
}

export type PublicProps = VNodeProps & AllowedComponentProps;

export interface HTMLAttributes {
    id?: string;
    title?: string;
    tabindex?: number | string;
    onClick?: (payload: MouseEvent) => void;
}

/** What vue-tsc writes for a `<script setup>` component and `defineComponent` returns: emits are already on* props. */
export type DefineComponent<P = {}, S = {}> = new (...args: any[]) => {
    $props: P & PublicProps;
    $slots: S;
};

/** Vue's own mapping of declared emits onto handler props: `{remove: (id: number) => void}` → `onRemove`. */
export type EmitsToProps<T> = {
    [K in string & keyof T as `on${Capitalize<K>}`]?: (...args: T[K] extends (...args: infer P) => any ? P : any[]) => any;
};

/** A functional component: props (emits folded in as handlers) first, the setup context second. */
export interface FunctionalComponent<P = {}, E = {}> {
    (props: P & EmitsToProps<E> & PublicProps, ctx: {emit: E; slots: {default?: Slot}}): VNode | null;
}
