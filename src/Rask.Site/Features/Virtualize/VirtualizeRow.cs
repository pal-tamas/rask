namespace Rask.Site.Features;

// Shared sample data for the /virtualize demos. Deliberately kept out of the code-sample
// tabs (the demos reference VirtualizeData.Rows) so each snippet stays focused on the
// VirtualizeModel usage rather than the row-building boilerplate.
public sealed record VirtualizeRow(int Index, string Name, string City, decimal Balance);
