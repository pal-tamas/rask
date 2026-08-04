// HarnessDbContext.Mapped is static (EF's DbContextFactory requires a single options-only constructor,
// so the per-test model shape can't be a constructor argument). Running the classes serially keeps one
// test's mapping from leaking into another's context. Matches the other SQLite-backed suites.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
