import { Component, signal } from '@angular/core';

import { rask } from '@rask/client';
import { getGreeting, recordVisit } from '@rask/messages';

// Rask's typed browser layer, the same TypeScript the Server and WASM clients run. Imported at module
// scope on purpose: Analog renders this component in NODE before any browser sees it, so a module
// that touched `window` on import would crash the render rather than degrade.
import { prefersDark } from '@rask/browser/mediaQuery';

@Component({
  selector: 'app-home',
  template: `
    <main class="mx-auto max-w-xl p-8 font-sans">
      <h1 class="text-2xl font-semibold">Rask + Analog</h1>

      <p class="mt-2 text-sm text-slate-500">
        Analog owns this page. Kestrel owns the port, answers <code>/_rask</code> itself, and forwards
        everything else to Analog's Nitro server on loopback.
      </p>

      <article data-testid="greeting" class="mt-6 rounded border border-slate-200 p-4">
        <h2 class="font-medium">From C#</h2>
        <p data-testid="greeting-message">{{ greeting() ?? 'asking…' }}</p>
      </article>

      <section class="mt-6 rounded border border-slate-200 p-4">
        <h2 class="font-medium">A command, from the browser</h2>
        <button class="rounded border border-slate-300 px-3 py-1 hover:bg-slate-50" data-testid="visit" (click)="visit()">Record a visit</button>
        <p class="mt-2 text-sm" data-testid="visits">{{ visits() === null ? 'not yet' : 'visits: ' + visits() }}</p>
        <p class="text-sm" data-testid="prefers-dark">{{ dark() === null ? 'asking…' : 'prefers dark: ' + dark() }}</p>
      </section>
    </main>
  `,
})
export default class Home {
  readonly greeting = signal<string | null>(null);
  readonly visits = signal<number | null>(null);
  readonly dark = signal<boolean | null>(null);

  constructor() {
    // Dispatched from the component rather than from a server-only loader. Analog's Angular components
    // run in both places, and Angular's own SSR data-transfer story is a bigger thing than this sample
    // needs to show — what it is here to prove is that the generated wire and the browser layer both
    // resolve and work under Analog's build, which is the strictest TypeScript of the six.
    void rask.dispatch(getGreeting({ name: 'meta' })).then((g) => this.greeting.set(g.message));
  }

  async visit(): Promise<void> {
    // A command over the same wire: POST, because the C# record implements ICommand.
    this.visits.set(await rask.dispatch(recordVisit({ name: 'meta' })));
    this.dark.set(prefersDark());
  }
}
