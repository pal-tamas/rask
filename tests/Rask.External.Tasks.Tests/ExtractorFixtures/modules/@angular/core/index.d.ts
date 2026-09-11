// A cut-down @angular/core: the partial-compilation declarations a library's .d.ts carries, and the signal and emitter
// types its inputs and outputs are typed with.

/** Resolves to unknown, as Angular's does — the checker cannot see the type arguments; only the syntax carries them. */
export type ɵɵComponentDeclaration<
    T,
    Selector extends string,
    ExportAs extends string[],
    InputMap extends {[key: string]: string | {alias: string | null; required: boolean; isSignal?: boolean}},
    OutputMap extends {[key: string]: string},
    QueryFields extends string[],
    NgContentSelectors extends string[],
    IsStandalone extends boolean = false,
    HostDirectives = never,
> = unknown;

export type ɵɵDirectiveDeclaration<
    T,
    Selector extends string,
    ExportAs extends string[],
    InputMap,
    OutputMap,
    QueryFields extends string[],
    NgContentSelectors = never,
    IsStandalone extends boolean = false,
    HostDirectives = never,
> = unknown;

export declare class EventEmitter<T> {
    emit(value?: T): void;
    subscribe(next?: (value: T) => void): unknown;
}

export interface OutputEmitterRef<T> {
    emit(value: T): void;
    subscribe(callback: (value: T) => void): unknown;
}

export interface InputSignal<T> {
    (): T;
}

export interface InputSignalWithTransform<T, TransformT> {
    (): T;
}

export interface ModelSignal<T> {
    (): T;
    set(value: T): void;
    subscribe(callback: (value: T) => void): unknown;
}
