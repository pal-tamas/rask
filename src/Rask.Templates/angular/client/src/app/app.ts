import { Component, effect, signal } from '@angular/core'

import { Auth } from './auth'
import { rask } from '../rask/client'
import { getGreeting, recordVisit } from '../rask/messages'
import type { Greeting } from '../rask/contracts'

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.css',
  imports: [Auth],
})
export class App {
  // No router, deliberately — see the note in the React template. Angular's is provided in
  // app.config.ts and app.routes.ts is yours to fill in; this reads the path once so the
  // starter does not decide your routing for you.
  protected readonly route = window.location.pathname

  protected readonly name = signal('world')
  protected readonly greeting = signal<Greeting | null>(null)
  protected readonly error = signal<string | null>(null)
  protected readonly busy = signal(false)

  private inFlight: AbortController | null = null

  constructor() {
    // Reading name() inside an effect is what makes this refetch when the name changes.
    // Aborting the previous request is what stops a slow earlier one landing after a later
    // one and showing the wrong answer.
    effect(() => {
      const value = this.name()
      void this.load(value)
    })
  }

  protected setName(value: string): void {
    this.name.set(value)
  }

  protected async record(): Promise<void> {
    this.busy.set(true)
    try {
      await rask.dispatch(recordVisit({ name: this.name() }))
      await this.load(this.name())
    } finally {
      this.busy.set(false)
    }
  }

  // seenAt is a real Date, revived because the C# type said it was an instant. `undefined` as
  // the locale means the visitor's own, and their own time zone.
  protected time(value: Date): string {
    return new Intl.DateTimeFormat(undefined, { timeStyle: 'medium' }).format(value)
  }

  private async load(name: string): Promise<void> {
    this.inFlight?.abort()
    const controller = new AbortController()
    this.inFlight = controller
    this.error.set(null)

    try {
      // The message carries its own result type, so this is a Greeting with no cast and no
      // wire name spelled out here.
      this.greeting.set(await rask.dispatch(getGreeting({ name }), { signal: controller.signal }))
    } catch (e) {
      if (!controller.signal.aborted) {
        this.error.set(e instanceof Error ? e.message : String(e))
      }
    }
  }
}
