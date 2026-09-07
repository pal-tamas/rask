// Paired to ScopedWidget by file name, so the assembly carries a scoped-JS registration too — the bake
// writes one registry per kind and a fixture with only CSS would leave half the path untested.
export function mount(): void {
  // Deliberately empty: the bake reads the registration, never executes this.
}
