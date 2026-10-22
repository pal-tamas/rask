using Rask.Core;

namespace Rask.Printing.Tests.Host;

// The measured page through its chain: inside a component the bare name is the chain's entry, not the type.
public sealed partial class LongTables : Component
{
    internal static Component Of(int rows, LongTableLook look) => LongTableSheet.Rows(rows).Look(look);

    protected override Component? Render() => null;
}
