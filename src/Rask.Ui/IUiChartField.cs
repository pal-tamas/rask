namespace Rask;

/// <summary>A chart part that reads one member of a row: what <c>Field(row =&gt; …)</c> can be called on.</summary>
public interface IUiChartField
{
    /// <summary>What the part reads from a row.</summary>
    UiChartField? Field { get; set; }
}
