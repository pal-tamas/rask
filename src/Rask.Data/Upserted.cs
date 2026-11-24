namespace Rask.Data;

/// <summary>What <c>X.Upsert</c> saved: the aggregate, and whether it was made new rather than changed.</summary>
/// <typeparam name="T">The aggregate.</typeparam>
/// <param name="Aggregate">The aggregate as saved, its key, version and times filled in.</param>
/// <param name="Created">True when none matched and the factory made it; false when the one that matched was changed.</param>
public sealed record Upserted<T>(T Aggregate, bool Created)
    where T : class;
