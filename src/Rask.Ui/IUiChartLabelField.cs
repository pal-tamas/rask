namespace Rask;

/// <summary>A chart part that names a row by one of its members: what <c>LabelField(row =&gt; …)</c> can be called on.</summary>
internal interface IUiChartLabelField
{
    /// <summary>What names a row.</summary>
    UiChartField? LabelField { get; set; }
}
