using System.Globalization;
using System.Xml;

namespace Rask.Workbooks;

/// <summary>
///     One sheet's XML, read into a <see cref="Grid" />: every cell of <c>sheetData</c> at its place, and every
///     <c>mergeCell</c>. A row or cell that leaves its place out (<c>r</c>) is the one after the last, as Excel reads it.
/// </summary>
internal static class WorksheetReader
{
    public static Grid Read(XmlReader reader, CellDecoder decoder, ReadLimits limits)
    {
        var grid = new Grid(limits);
        var row = 0;
        var column = 0;
        reader.Read();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && Ooxml.IsMain(reader.NamespaceURI))
            {
                switch (reader.LocalName)
                {
                    case "row":
                        row = RowNumber(reader.GetAttribute("r"), row);
                        column = 0;
                        break;
                    case "c" when row > 0:
                        column = ReadCell(reader, grid, decoder, row, column);
                        continue; // the cell is read to its end, and the reader stands on what follows it
                    case "mergeCell" when reader.GetAttribute("ref") is { } range:
                        grid.Merge(CellReference.Range(range));
                        break;
                }
            }

            reader.Read();
        }

        return grid;
    }

    private static int RowNumber(string? r, int previous)
    {
        if (r is null)
        {
            return previous + 1;
        }

        return int.TryParse(r, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= CellReference.MaxRows
            ? number
            : throw Package.Malformed($"'{r}' is not a row Excel has (1 to {CellReference.MaxRows})");
    }

    private static int ReadCell(XmlReader reader, Grid grid, CellDecoder decoder, int row, int previous)
    {
        var at = reader.GetAttribute("r");
        var column = at is null ? previous + 1 : CellReference.Parse(at).Column;
        if (column > CellReference.MaxColumns)
        {
            throw Package.Malformed($"row {row} holds more than {CellReference.MaxColumns} cells");
        }

        var type = reader.GetAttribute("t");
        var style = reader.GetAttribute("s");
        at ??= WorksheetWriter.ColumnName(column - 1) + row.ToString(CultureInfo.InvariantCulture);
        string? value = null;
        string? inline = null;
        if (reader.IsEmptyElement)
        {
            reader.Read();
        }
        else
        {
            var depth = reader.Depth;
            reader.Read();
            while (!reader.EOF && reader.Depth > depth)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Depth == depth + 1 && Ooxml.IsMain(reader.NamespaceURI))
                {
                    switch (reader.LocalName)
                    {
                        case "v":
                            value = reader.ReadElementContentAsString();
                            continue;
                        case "is":
                            inline = RichText.Read(reader);
                            continue;
                    }
                }

                reader.Read();
            }

            reader.Read();
        }

        grid.Set(row, column, decoder.Decode(type, style, value, inline, at));
        return column;
    }
}
