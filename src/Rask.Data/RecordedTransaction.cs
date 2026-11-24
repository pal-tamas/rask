namespace Rask.Data;

/// <summary>One committed <c>Db.Transaction</c>, as <see cref="Db.Record" /> saw it.</summary>
/// <param name="Aggregates">The aggregates it wrote, each as <c>Type key</c> — <c>Supply 42</c> — in the order first written.</param>
/// <param name="ManyAggregates">The reason its <c>manyAggregates:</c> stated, or <see langword="null" />.</param>
public sealed record RecordedTransaction(IReadOnlyList<string> Aggregates, string? ManyAggregates);
