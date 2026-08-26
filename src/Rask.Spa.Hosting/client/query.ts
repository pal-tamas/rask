// rask-client vendored from Rask.Spa.Hosting
//
// The bridge between a Rask message and TanStack Query. Deleting it is how you opt out —
// `rask.dispatch` works on its own.
//
// Rask deliberately ships no cache, no request dedup, no stale-while-revalidate and no
// window-focus refetch. Those are TanStack's job and it does them far better than a framework
// hook bolted onto a dispatcher would.
//
// It imports NOTHING from TanStack, on purpose. Every adapter — react-query, solid-query,
// svelte-query, lit-query — exports its own `queryOptions`, so importing one would tie this file to
// one framework for the sake of a helper that is an identity function with a type signature. What it
// returns is the same plain options object those helpers hand back, so `useQuery`, `createQuery` and
// `createQueryController` all take it as-is.

import { rask, RaskDispatchError, type CallOptions, type Dispatchable } from './client'

/**
 * Query options for a Rask query message.
 *
 * The key is `[wire name, payload]`. TanStack hashes the payload deterministically, so two payloads
 * are two cache entries — and because `invalidateQueries` matches on a key PREFIX, invalidating the
 * wire name alone clears every variant of that query:
 *
 *     queryClient.invalidateQueries({ queryKey: [getOrders.messageName] })
 *
 * The parameter is `Dispatchable<T, 'query'>`, not `Dispatchable<T>`. A command auto-firing on
 * render is a mutation on mount, so the compiler refuses it here — the same rule the server enforces
 * by answering 405 to a command sent as a GET.
 */
export function raskQuery<T>(message: Dispatchable<T, 'query'>, options?: CallOptions) {
  return {
    queryKey: [message.name, message.payload] as const,
    // TanStack aborts this signal when the query is superseded or the component unmounts, so a
    // stale request is really cancelled rather than merely ignored.
    queryFn: ({ signal }: { signal: AbortSignal }): Promise<T> =>
      rask.dispatch(message, { ...options, signal }),
  }
}

/**
 * Mutation options for a command or notification factory.
 *
 *     const ship = useMutation({
 *       ...raskMutation(shipOrder),
 *       onSuccess: () => queryClient.invalidateQueries({ queryKey: [getOrders.messageName] }),
 *     })
 */
export function raskMutation<TPayload, TResult>(
  factory: (payload: TPayload) => Dispatchable<TResult>,
  options?: CallOptions,
) {
  return {
    mutationFn: (payload: TPayload): Promise<TResult> => rask.dispatch(factory(payload), options),
  }
}

/**
 * Retry predicate for the QueryClient:
 *
 *     new QueryClient({ defaultOptions: { queries: { retry: raskRetry } } })
 *
 * A 4xx will never succeed on a retry. TanStack's default of three turns one refused request into
 * four and delays telling the user anything by several seconds — and a 403 is not a network blip.
 * Anything else keeps the default budget.
 */
export function raskRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof RaskDispatchError && error.status >= 400 && error.status < 500) {
    return false
  }
  return failureCount < 3
}
