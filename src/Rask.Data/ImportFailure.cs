namespace Rask.Data;

/// <summary>What <c>X.Import</c> does with a row its factory or its rules refuse: <see cref="ImportOptions{T, TRow}.OnInvalid" />.</summary>
public enum ImportFailure
{
    /// <summary>The import stops with the refusal; the batches before the refused one stay committed.</summary>
    Stop,

    /// <summary>The refused row is set aside in <see cref="ImportResult{TRow}.Failures" />, and the rest are imported.</summary>
    Collect,
}
