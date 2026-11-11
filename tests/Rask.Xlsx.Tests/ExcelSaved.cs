using System.IO.Compression;
using System.Text;

namespace Rask.Workbooks.Tests;

/// <summary>
///     Workbooks built part by part as Excel itself saves them: shared strings with rich-text runs and a phonetic guide,
///     styles with numFmts, the x14ac/mc noise Excel adds, formulas with cached values - and the ELEKÁFA layout, a title
///     over merged two-row headings over the data.
/// </summary>
internal static class ExcelSaved
{
    public const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n";

    /// <summary>A workbook of one sheet named <paramref name="sheet" />, with the shared strings and styles given.</summary>
    public static byte[] Workbook(string sheet, string sheetData, string[] shared, string merges = "", bool date1904 = false, string? styles = null) =>
        Zip(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["[Content_Types].xml"] = Declaration + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/></Types>",
            ["_rels/.rels"] = Declaration + $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/extended-properties\" Target=\"docProps/app.xml\"/><Relationship Id=\"rId1\" Type=\"{Office}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>",
            ["xl/_rels/workbook.xml.rels"] = Declaration + $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId3\" Type=\"{Office}/styles\" Target=\"styles.xml\"/><Relationship Id=\"rId2\" Type=\"{Office}/theme\" Target=\"theme/theme1.xml\"/><Relationship Id=\"rId1\" Type=\"{Office}/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId5\" Type=\"{Office}/worksheet\" Target=\"/xl/worksheets/sheet2.xml\"/><Relationship Id=\"rId4\" Type=\"{Office}/sharedStrings\" Target=\"sharedStrings.xml\"/></Relationships>",
            ["xl/workbook.xml"] = Declaration + $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Office}\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"x15 xr xr6 xr10 xr2\"><fileVersion appName=\"xl\" lastEdited=\"7\" lowestEdited=\"7\" rupBuild=\"28304\"/><workbookPr{(date1904 ? " date1904=\"1\"" : "")} defaultThemeVersion=\"166925\"/><bookViews><workbookView xWindow=\"0\" yWindow=\"500\" windowWidth=\"28800\" windowHeight=\"16000\"/></bookViews><sheets><sheet name=\"{sheet}\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Notes\" sheetId=\"2\" r:id=\"rId5\"/></sheets><calcPr calcId=\"191029\"/></workbook>",
            ["xl/styles.xml"] = Declaration + (styles ?? Styles),
            ["xl/sharedStrings.xml"] = Declaration + $"<sst xmlns=\"{Main}\" count=\"{shared.Length}\" uniqueCount=\"{shared.Length}\">{string.Concat(shared.Select(s => s.StartsWith('<') ? $"<si>{s}</si>" : $"<si><t>{s}</t></si>"))}</sst>",
            ["xl/worksheets/sheet1.xml"] = Declaration + Sheet(sheetData, merges),
            ["xl/worksheets/sheet2.xml"] = Declaration + Sheet("<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Note</t></is></c></row>", ""),
        });

    public static byte[] Zip(IReadOnlyDictionary<string, string> parts)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, xml) in parts)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }
        }

        return buffer.ToArray();
    }

    // 0 General, 1 a date (built-in 14), 2 a custom "yyyy\.mm\.dd\.;@", 3 a moment (built-in 22), 4 an amount, 5 a custom with a quoted "d"
    private const string Styles =
        $"<styleSheet xmlns=\"{Main}\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"x14ac x16r2 xr\" xmlns:x14ac=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac\">"
        + "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\.mm\\.dd\\.;@\"/><numFmt numFmtId=\"165\" formatCode=\"#,##0\\ &quot;Ft&quot;;[Red]\\-#,##0\\ &quot;d&quot;\"/></numFmts>"
        + "<fonts count=\"1\" x14ac:knownFonts=\"1\"><font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Aptos Narrow\"/><family val=\"2\"/><scheme val=\"minor\"/></font></fonts>"
        + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
        + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
        + "<cellStyleXfs count=\"1\"><xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
        + "<cellXfs count=\"6\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
        + "<xf numFmtId=\"14\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "<xf numFmtId=\"22\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "<xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs>"
        + "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";

    private static string Sheet(string sheetData, string merges) =>
        $"<worksheet xmlns=\"{Main}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" mc:Ignorable=\"x14ac xr xr2 xr3\" xmlns:x14ac=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac\">"
        + "<dimension ref=\"A1:F7\"/><sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\"/></sheetViews><sheetFormatPr baseColWidth=\"10\" defaultRowHeight=\"16\" x14ac:dyDescent=\"0.2\"/>"
        + $"<sheetData>{sheetData}</sheetData>"
        + (merges.Length > 0 ? $"<mergeCells count=\"{merges.Split(' ').Length}\">{string.Concat(merges.Split(' ').Select(m => $"<mergeCell ref=\"{m}\"/>"))}</mergeCells>" : "")
        + "<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>";
}
