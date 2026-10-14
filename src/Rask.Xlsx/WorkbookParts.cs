using System.IO.Compression;
using System.Text;

namespace Rask.Workbooks;

/// <summary>The parts of the package around the sheets: content types, relations, the workbook and its styles.</summary>
internal static class WorkbookParts
{
    private const string Declaration = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
    private const string Package = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void Write(ZipArchive zip, Xlsx.Sheet[] sheets)
    {
        var numbers = Enumerable.Range(1, sheets.Length).ToList();
        Add(zip, "[Content_Types].xml", ContentTypes(numbers));
        Add(zip, "_rels/.rels", Relations("officeDocument", "xl/workbook.xml"));
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRelations(numbers));
        Add(zip, "xl/workbook.xml", Workbook(sheets));
        Add(zip, "xl/styles.xml", Styles);
        foreach (var number in numbers)
        {
            using var writer = Open(zip, $"xl/worksheets/sheet{number}.xml");
            WorksheetWriter.Write(writer, sheets[number - 1]);
        }
    }

    private static string Workbook(Xlsx.Sheet[] sheets) =>
        $"{Declaration}<workbook xmlns=\"{WorksheetWriter.Main}\" xmlns:r=\"{WorksheetWriter.Office}\"><bookViews><workbookView/></bookViews><sheets>"
        + string.Concat(sheets.Select((sheet, at) => $"<sheet name=\"{XmlText.Escape(sheet.Name)}\" sheetId=\"{at + 1}\" r:id=\"rId{at + 1}\"/>"))
        + "</sheets><calcPr fullCalcOnLoad=\"1\"/></workbook>"; // Excel works the formulas out anew as it opens the file

    private static StreamWriter Open(ZipArchive zip, string path) =>
        new(zip.CreateEntry(path, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));

    private static void Add(ZipArchive zip, string path, string content)
    {
        using var writer = Open(zip, path);
        writer.Write(content);
    }

    private static string ContentTypes(IEnumerable<int> sheets) => Declaration
        + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
        + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
        + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
        + "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>"
        + string.Concat(sheets.Select(number => $"<Override PartName=\"/xl/worksheets/sheet{number}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"))
        + "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>"
        + "</Types>";

    private static string Relations(string type, string target) =>
        $"{Declaration}<Relationships xmlns=\"{Package}\"><Relationship Id=\"rId1\" Type=\"{WorksheetWriter.Office}/{type}\" Target=\"{target}\"/></Relationships>";

    // the sheets take the first ids, in order; the styles come after them
    private static string WorkbookRelations(List<int> sheets) => Declaration
        + "<Relationships xmlns=\"" + Package + "\">"
        + string.Concat(sheets.Select(number => $"<Relationship Id=\"rId{number}\" Type=\"{WorksheetWriter.Office}/worksheet\" Target=\"worksheets/sheet{number}.xml\"/>"))
        + $"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"{WorksheetWriter.Office}/styles\" Target=\"styles.xml\"/>"
        + "</Relationships>";

    // style 0 plain, 1 a moment, 2 a day, 3 bold, 4 an amount, 5 an amount in bold
    private const string Styles = Declaration
        + "<styleSheet xmlns=\"" + WorksheetWriter.Main + "\">"
        + "<numFmts count=\"3\"><numFmt numFmtId=\"164\" formatCode=\"yyyy.mm.dd hh:mm\"/><numFmt numFmtId=\"165\" formatCode=\"yyyy.mm.dd\"/><numFmt numFmtId=\"166\" formatCode=\"#,##0.00##\"/></numFmts>"
        + "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>"
        + "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>"
        + "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>"
        + "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>"
        + "<cellXfs count=\"6\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>"
        + "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>"
        + "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>"
        + "<xf numFmtId=\"166\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\"/></cellXfs>"
        + "</styleSheet>";
}
