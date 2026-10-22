namespace Rask.Printing.Tests.Host;

/// <summary>How <see cref="LongTableSheet"/> draws its table.</summary>
public enum LongTableLook
{
    /// <summary>Table, Tr, Td and nothing else.</summary>
    Plain,

    /// <summary>The kit's Ui.Table, without its stylesheet.</summary>
    Kit,

    /// <summary>The kit's Ui.Table on the kit's precompiled sheet.</summary>
    KitSheet,

    /// <summary>The kit's Ui.Table on its sheet, with an app's 7pt print stylesheet.</summary>
    KitSheetSmall,
}
