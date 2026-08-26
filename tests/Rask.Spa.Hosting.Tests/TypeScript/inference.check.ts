// Type-level proof of the two claims the dispatcher design rests on:
//   1. the result type is inferred from the message, with no cast at the call site;
//   2. a command cannot be handed to a query-only API.
import { message, rask } from './client'
import { raskQuery } from './query'

interface Greeting {
  message: string
  serverTime: string
}

const getGreeting = message<{ name: string }, Greeting, 'query'>({
  name: 'Shop.Shared.GetGreeting',
  kind: 'query',
})

const recordVisit = message<{ name: string }, number, 'command'>({
  name: 'Shop.Shared.RecordVisit',
  kind: 'command',
})

// 1. Inference. Assignment to an explicitly typed variable is the assertion: if dispatch returned
//    unknown or any, `Greeting` would not be satisfied / would silently pass, so the second half
//    checks a wrong type is genuinely rejected.
export async function inferred(): Promise<void> {
  const greeting: Greeting = await rask.dispatch(getGreeting({ name: 'Ada' }))
  const visits: number = await rask.dispatch(recordVisit({ name: 'Ada' }))
  void greeting
  void visits

  // @ts-expect-error the result is Greeting, not number
  const wrong: number = await rask.dispatch(getGreeting({ name: 'Ada' }))
  void wrong

  // @ts-expect-error the payload must match the message
  await rask.dispatch(getGreeting({ nmae: 'Ada' }))
}

// 2. A command is not a query. This is the compiler enforcing client-side exactly what the server
//    enforces by answering 405 to a command sent as a GET.
export function queryOnly(): void {
  void raskQuery(getGreeting({ name: 'Ada' }))

  // @ts-expect-error a command must not be auto-fired by a query hook
  void raskQuery(recordVisit({ name: 'Ada' }))
}

// 3. The factory carries its own wire name, so invalidation is never a string literal.
export const key: string = getGreeting.messageName
