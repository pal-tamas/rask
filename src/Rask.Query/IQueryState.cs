namespace Rask.Querying;

/// <summary>What a component drawing a query's result reads of its progress, whatever the result's type.</summary>
/// <remarks>For the kit: <c>Ui.Table.Of(query)</c> draws skeleton rows by it.</remarks>
internal interface IQueryState
{
    /// <inheritdoc cref="Query{TResult}.IsLoading" />
    bool IsLoading { get; }

    /// <inheritdoc cref="Query{TResult}.IsPlaceholderData" />
    bool IsPlaceholderData { get; }
}
