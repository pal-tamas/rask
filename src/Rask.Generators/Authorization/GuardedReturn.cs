namespace Rask.Generators.Authorization;

/// <summary>What a guarded method hands back, which decides how its interceptor waits for the check.</summary>
internal enum GuardedReturn
{
    Void,
    Value,
    Task,
    ValueTask,
}
